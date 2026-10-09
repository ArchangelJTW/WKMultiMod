using System;

namespace WKMPMod.NetWork;

/// <summary>Authenticate and correlate a checkpoint response before decoding its snapshot.</summary>
public sealed class CheckpointRequestScope {
	public ulong Attempt { get; }
	public ulong LobbyId { get; }
	public ulong HostId { get; }
	public int Seed { get; }
	public int WorldRevision { get; }
	public string ModeSignature { get; }

	public CheckpointRequestScope(ulong attempt, ulong lobbyId, ulong hostId, int seed, int worldRevision, string signature) {
		Attempt = attempt; LobbyId = lobbyId; HostId = hostId; Seed = seed;
		WorldRevision = worldRevision; ModeSignature = signature;
	}

	public bool Accepts(ulong sender, ulong attempt, ulong lobbyId, int seed, string signature,
		ulong currentLobby, ulong currentHost, int currentWorldRevision) =>
		Attempt != 0 && sender == HostId && attempt == Attempt && lobbyId == LobbyId
		&& currentLobby == LobbyId && currentHost == HostId && seed == Seed
		&& currentWorldRevision == WorldRevision
		&& string.Equals(signature, ModeSignature, StringComparison.Ordinal);
}
