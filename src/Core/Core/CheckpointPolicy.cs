using System;

namespace WKMPMod.Core;

public enum CheckpointSpawnReason : byte { LateJoin = 0, DeathRespawn = 1, LiveReconnect = 2 }
public enum CheckpointSpawnDecision : byte { Normal = 0, KeepOwnRun = 1, HostWithOwnItems = 2, HostEmpty = 3, Failed = 4 }
public enum CheckpointDecisionReason : byte {
	LiveReconnect, Disabled, NoHostCheckpoint, InvalidHostProgress, UnreachedHostPoint,
	InvalidOwnProgress, DifferentRoutes, LateJoin, NativeContinuationLateJoin,
	NativeContinuationNotForDeath, HostNotLater, HostLaterAfterDeath, UnsupportedReason, SnapshotEncodingFailed
}

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
		CheckpointProgress? host, CheckpointProgress? own, bool nativeContinuation = false) =>
		Evaluate(reason, enabled, host, own, nativeContinuation).Decision;

	/// <summary>Keep the normal-spawn reason observable instead of hiding all fallback paths behind one byte.</summary>
	public static (CheckpointSpawnDecision Decision, CheckpointDecisionReason Reason) Evaluate(CheckpointSpawnReason reason,
		bool enabled, CheckpointProgress? host, CheckpointProgress? own, bool nativeContinuation = false) {
		if (reason == CheckpointSpawnReason.LiveReconnect) return (CheckpointSpawnDecision.KeepOwnRun, CheckpointDecisionReason.LiveReconnect);
		if (!enabled) return (CheckpointSpawnDecision.Normal, CheckpointDecisionReason.Disabled);
		if (host == null) return (CheckpointSpawnDecision.Normal, CheckpointDecisionReason.NoHostCheckpoint);
		if (!IsValid(host)) return (CheckpointSpawnDecision.Failed, CheckpointDecisionReason.InvalidHostProgress);
		if (!host.reached && !nativeContinuation) return (CheckpointSpawnDecision.Normal, CheckpointDecisionReason.UnreachedHostPoint);
		if (!IsValid(own)) return (CheckpointSpawnDecision.Failed, CheckpointDecisionReason.InvalidOwnProgress);
		if (!string.Equals(host.route, own!.route, StringComparison.Ordinal))
			return (CheckpointSpawnDecision.Normal, CheckpointDecisionReason.DifferentRoutes);
		if (reason == CheckpointSpawnReason.LateJoin) return (CheckpointSpawnDecision.HostWithOwnItems,
			nativeContinuation ? CheckpointDecisionReason.NativeContinuationLateJoin : CheckpointDecisionReason.LateJoin);
		// A native Continue point may help late joiners resume a legacy save, but
		// must never masquerade as a later reached checkpoint for the death rule.
		if (nativeContinuation) return (CheckpointSpawnDecision.Normal, CheckpointDecisionReason.NativeContinuationNotForDeath);
		if (reason != CheckpointSpawnReason.DeathRespawn) return (CheckpointSpawnDecision.Failed, CheckpointDecisionReason.UnsupportedReason);
		return IsLater(host, own) ? (CheckpointSpawnDecision.HostEmpty, CheckpointDecisionReason.HostLaterAfterDeath)
			: (CheckpointSpawnDecision.Normal, CheckpointDecisionReason.HostNotLater);
	}
}

/// <summary>Death cannot be cleared by leaving, scene loading or a failed join.</summary>
public sealed class CheckpointSessionState {
	public ulong LobbyId { get; private set; }
	public int Seed { get; private set; }
	public string RunId { get; private set; } = string.Empty;
	public string ModeSignature { get; private set; } = string.Empty;
	public CheckpointProgress? LocalCheckpoint { get; private set; }
	public CheckpointProgress? DeathCheckpoint { get; private set; }
	public bool PendingDeath { get; private set; }

	public void Bind(ulong lobbyId, int seed, string signature, string runId = "") {
		if (lobbyId == 0 || string.IsNullOrWhiteSpace(signature) || runId == null
			|| (runId.Length != 0 && !SavedRunPolicy.IsRunId(runId))) throw new ArgumentException("Invalid checkpoint session.");
		if (Matches(lobbyId, seed, signature, runId)) return;
		LobbyId = lobbyId; Seed = seed; ModeSignature = signature; RunId = runId;
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

	public void RestoreProgress(CheckpointProgress? checkpoint, CheckpointProgress? deathCheckpoint, bool pendingDeath) {
		if (LobbyId == 0) throw new InvalidOperationException("Bind the run before restoring checkpoint progress.");
		if ((checkpoint != null && !CheckpointPolicy.IsValid(checkpoint))
			|| (pendingDeath && !CheckpointPolicy.IsValid(deathCheckpoint)))
			throw new ArgumentException("Invalid saved checkpoint progress.");
		LocalCheckpoint = checkpoint?.Copy();
		PendingDeath = pendingDeath;
		DeathCheckpoint = pendingDeath ? deathCheckpoint!.Copy() : null;
	}

	public void CompleteRespawn(CheckpointProgress? checkpoint = null) {
		if (checkpoint != null) LocalCheckpoint = checkpoint.Copy();
		PendingDeath = false;
		DeathCheckpoint = null;
	}

	public bool Matches(ulong lobbyId, int seed, string signature, string runId = "") =>
		LobbyId == lobbyId && Seed == seed && ModeSignature == signature && RunId == runId;
}
