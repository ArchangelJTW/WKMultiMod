using System;

namespace WKMPMod.NetWork;

/// <summary>Authenticate and correlate a checkpoint response, including the persistent run ID, before decoding its snapshot.</summary>
public sealed class CheckpointRequestScope {
	public ulong Attempt { get; }
	public ulong LobbyId { get; }
	public ulong HostId { get; }
	public int Seed { get; }
	public int WorldRevision { get; }
	public string RunId { get; }
	public string ModeSignature { get; }

	public CheckpointRequestScope(ulong attempt, ulong lobbyId, ulong hostId, int seed, int worldRevision, string signature, string runId = "") {
		Attempt = attempt; LobbyId = lobbyId; HostId = hostId; Seed = seed;
		WorldRevision = worldRevision; ModeSignature = signature; RunId = runId;
	}

	public bool Accepts(ulong sender, ulong attempt, ulong lobbyId, int seed, string signature,
		ulong currentLobby, ulong currentHost, int currentWorldRevision, string runId = "", string currentRunId = "") =>
		Attempt != 0 && sender == HostId && attempt == Attempt && lobbyId == LobbyId
		&& currentLobby == LobbyId && currentHost == HostId && seed == Seed
		&& currentWorldRevision == WorldRevision && RunId == runId && RunId == currentRunId
		&& string.Equals(signature, ModeSignature, StringComparison.Ordinal);
}
