namespace WKMPMod.Core;

public enum CheckpointResolution { NotAttempted, Normal, Applied, Failed }

/// <summary>Loading a same-seed starting world is not a successful checkpoint handshake.</summary>
public sealed class WorldJoinAdmission {
	public bool IsReady { get; private set; }
	public void Begin() => IsReady = false;
	public bool Complete(CheckpointResolution result) => IsReady =
		result is CheckpointResolution.Normal or CheckpointResolution.Applied;
}
