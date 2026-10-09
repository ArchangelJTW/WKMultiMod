namespace WKMPMod.NetWork;

/// <summary>
/// Steam can complete the async join/create before or after OnLobbyEntered.
/// The current lobby handle is not evidence that entry initialization ran.
/// </summary>
public sealed class LobbyEntryState {
	private ulong _notifiedLobbyId;

	public bool TryNotify(ulong lobbyId, ulong requestedLobbyId) {
		if (lobbyId == 0 || lobbyId != requestedLobbyId || lobbyId == _notifiedLobbyId) return false;
		_notifiedLobbyId = lobbyId;
		return true;
	}

	public void Clear() => _notifiedLobbyId = 0;
}
