using System;

namespace WKMPMod.Core;

/// <summary>
/// A reconnect ticket for a world that is still loaded in this process.
/// This is deliberately not a persistent save: a restart, scene change, different
/// lobby, seed, or game-mode configuration must not resume an unrelated run.
/// </summary>
public sealed class LobbyResumeState {
	public ulong LobbyId { get; private set; }
	public int WorldRevision { get; private set; } = -1;
	public string ModeSignature { get; private set; } = string.Empty;
	public bool HasSession => LobbyId != 0;

	public void Remember(ulong lobbyId, string modeSignature, int worldRevision) {
		if (lobbyId == 0) throw new ArgumentOutOfRangeException(nameof(lobbyId));
		if (string.IsNullOrWhiteSpace(modeSignature)) throw new ArgumentException("A game-mode signature is required.", nameof(modeSignature));
		LobbyId = lobbyId;
		ModeSignature = modeSignature;
		WorldRevision = worldRevision;
	}

	public bool CanResume(ulong lobbyId, string modeSignature, int worldRevision) {
		return HasSession && LobbyId == lobbyId && WorldRevision == worldRevision
			&& string.Equals(ModeSignature, modeSignature, StringComparison.Ordinal);
	}

	public void Clear() {
		LobbyId = 0;
		ModeSignature = string.Empty;
		WorldRevision = -1;
	}
}
