namespace WKMPMod.Core;

public enum CheckpointResolution { NotAttempted, Normal, Applied, Failed }

/// <summary>Loading a same-seed starting world is not a successful checkpoint handshake.</summary>
public sealed class WorldJoinAdmission {
	public bool IsReady { get; private set; }
	public string RunId { get; private set; } = string.Empty;
	public bool IsReadyFor(string runId) => IsReady && RunId == runId;
	public void Begin() { IsReady = false; RunId = string.Empty; }
	public bool Complete(CheckpointResolution result, string runId = "") {
		IsReady = result is CheckpointResolution.Normal or CheckpointResolution.Applied;
		RunId = IsReady ? runId : string.Empty;
		return IsReady;
	}
}
