using System;

namespace WKMPMod.Core;

public enum CheckpointSpawnReason : byte { LateJoin = 0, DeathRespawn = 1, LiveReconnect = 2 }
public enum CheckpointSpawnDecision : byte { Normal = 0, KeepOwnRun = 1, HostWithOwnItems = 2, HostEmpty = 3 }

[Serializable]
public sealed class CheckpointProgress {
	public string route = string.Empty;
	public int levelOrdinal;
	public double travelDistance;
	public bool reached;

	public CheckpointProgress Copy() => new() {
		route = route, levelOrdinal = levelOrdinal, travelDistance = travelDistance, reached = reached
	};
}

/// <summary>Gameplay policy independent of Unity, Steam, wall-clock time and world-space height.</summary>
public static class CheckpointPolicy {
	public static bool IsValid(CheckpointProgress? mark) => mark != null
		&& !string.IsNullOrWhiteSpace(mark.route) && mark.route.Length <= 512
		&& mark.levelOrdinal >= 0 && !double.IsNaN(mark.travelDistance)
		&& !double.IsInfinity(mark.travelDistance) && mark.travelDistance >= 0;

	public static bool IsLater(CheckpointProgress host, CheckpointProgress own) {
		if (!IsValid(host) || !IsValid(own) || !host.reached
			|| !string.Equals(host.route, own.route, StringComparison.Ordinal)) return false;
		return host.levelOrdinal > own.levelOrdinal
			|| (host.levelOrdinal == own.levelOrdinal && host.travelDistance > own.travelDistance + 0.1);
	}

	public static CheckpointSpawnDecision Decide(CheckpointSpawnReason reason, bool enabled,
		CheckpointProgress? host, CheckpointProgress? own) {
		if (reason == CheckpointSpawnReason.LiveReconnect) return CheckpointSpawnDecision.KeepOwnRun;
		if (!enabled || !IsValid(host) || !host!.reached || !IsValid(own)
			|| !string.Equals(host.route, own!.route, StringComparison.Ordinal)) return CheckpointSpawnDecision.Normal;
		if (reason == CheckpointSpawnReason.LateJoin) return CheckpointSpawnDecision.HostWithOwnItems;
		if (reason == CheckpointSpawnReason.DeathRespawn && IsLater(host, own)) return CheckpointSpawnDecision.HostEmpty;
		return CheckpointSpawnDecision.Normal;
	}
}

/// <summary>Death cannot be cleared by leaving, scene loading or a failed join.</summary>
public sealed class CheckpointSessionState {
	public ulong LobbyId { get; private set; }
	public int Seed { get; private set; }
	public string ModeSignature { get; private set; } = string.Empty;
	public CheckpointProgress? LocalCheckpoint { get; private set; }
	public CheckpointProgress? DeathCheckpoint { get; private set; }
	public bool PendingDeath { get; private set; }

	public void Bind(ulong lobbyId, int seed, string signature) {
		if (lobbyId == 0 || string.IsNullOrWhiteSpace(signature)) throw new ArgumentException("Invalid checkpoint session.");
		if (LobbyId == lobbyId && Seed == seed && ModeSignature == signature) return;
		LobbyId = lobbyId; Seed = seed; ModeSignature = signature;
		LocalCheckpoint = null; DeathCheckpoint = null; PendingDeath = false;
	}

	public void SetCheckpoint(CheckpointProgress progress) {
		if (!CheckpointPolicy.IsValid(progress)) throw new ArgumentException("Invalid checkpoint progress.");
		if (!PendingDeath) LocalCheckpoint = progress.Copy();
	}

	public void RecordDeath(CheckpointProgress fallback) {
		if (LobbyId == 0 || PendingDeath) return;
		PendingDeath = true;
		DeathCheckpoint = (LocalCheckpoint ?? fallback).Copy();
	}

	public void CompleteRespawn(CheckpointProgress? checkpoint = null) {
		if (checkpoint != null) LocalCheckpoint = checkpoint.Copy();
		PendingDeath = false;
		DeathCheckpoint = null;
	}

	public bool Matches(ulong lobbyId, int seed, string signature) =>
		LobbyId == lobbyId && Seed == seed && ModeSignature == signature;
}
