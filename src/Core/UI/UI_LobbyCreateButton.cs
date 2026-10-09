using Steamworks;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.Util;

namespace WKMPMod.UI;

public class UI_LobbyCreateButton : MonoBehaviour {
	public Button? button;
	public UI_GamemodeScreen_Panel? gamemodePanel;
	public bool resumeSavedRun;
	public List<Button> otherButtons = new();
	private bool _creating;

	private void Awake() {
		button = GetComponent<Button>() ?? gameObject.AddComponent<Button>();
		// Replace persistent native events too: a cloned Continue button must not
		// load the save before Steam creation succeeds, or also start a new game.
		button.onClick = new Button.ButtonClickedEvent();
		button.onClick.AddListener(CreateLobby);
	}

	private void Start() {
		if (transform.parent == null) return;
		foreach (var other in transform.parent.GetComponentsInChildren<Button>(true))
			if (other != button) otherButtons.Add(other);
	}

	public async void CreateLobby() {
		if (_creating || gamemodePanel == null) return;
		var selectedMode = gamemodePanel.GetGamemode();
		if (selectedMode == null) return;
		_creating = true;
		Creating();
		try {
			bool success = await MPCore.Instance.CreateLobbyAsync(SteamClient.Name + "'s game",
				contextIsValid: () => this != null && gamemodePanel != null && gameObject.activeInHierarchy
					&& gamemodePanel.GetGamemode() == selectedMode);
			if (this == null || gamemodePanel == null) return;
			if (!success) { CreateFailed(); return; }
			CreateSuccess();
		} catch (Exception ex) {
			if (this != null) CreateFailed();
			MPCore.Instance.ResetStateVariables();
			MPMain.LogError(Localization.Get("UI_LobbyCreateButton.CreateLobbyFailed", ex.ToString()));
		} finally { if (this != null) _creating = false; }
	}

	public void Creating() {
		SetButtons(false);
		MPEventBusGame.NotifyShowLoading(15f);
	}

	public void CreateFailed() {
		SetButtons(true);
		MPEventBusGame.NotifyHideLoading();
	}

	public void CreateSuccess() {
		MPEventBusGame.NotifyHideLoading();
		if (gamemodePanel == null) return;
		if (resumeSavedRun) {
			// The exact native Continue path sets loadSessionSaveOnLoad and restores
			// player/level/lives. Never call LoadGamemode/StartNewGame for this button.
			gamemodePanel.LoadGamemodeSave();
		} else {
			gamemodePanel.LoadGamemode();
		}
	}

	private void SetButtons(bool enabled) {
		if (button != null) button.interactable = enabled;
		foreach (var other in otherButtons) if (other != null) other.interactable = enabled;
	}
}
