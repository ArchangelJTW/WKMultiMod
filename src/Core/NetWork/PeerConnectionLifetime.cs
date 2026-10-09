namespace WKMPMod.NetWork;

/// <summary>Protect a replacement connection from a delayed close callback.</summary>
public static class PeerConnectionLifetime {
	public static bool IsCurrentClose(uint? activeHandle, uint? pendingHandle, uint? closedHandle) {
		// An unscoped close is an explicit lobby-member leave/cleanup.
		if (!closedHandle.HasValue) return true;
		if (activeHandle.HasValue) return activeHandle.Value == closedHandle.Value;
		return pendingHandle.HasValue && pendingHandle.Value == closedHandle.Value;
	}
}
