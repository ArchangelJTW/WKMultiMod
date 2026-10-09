using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.World;
using GameModeData = WKMPMod.Core.MPGameModeManager.GameModeData;

namespace WKMPMod.Core;

/// <summary>
/// Reuses the game's session-file format and Continue path. Only mod metadata is
/// stored in a namespaced session flag; inventories/lives stay entirely native/local.
/// </summary>
public static class NativeRunPersistence {
	public const string FlagName = "wkmp.multiplayer-run.v1";
	public static bool NativeContinuePending { get; private set; }
	public static bool LoadedNativeSession { get; private set; }
	private static CL_SaveManager.SaveState? _pendingContinuation;
	private static CL_SaveManager.SessionSave? _pendingNativeSnapshot;
	private static HostCheckpointSnapshot? _nativeContinuation;
	private static readonly Dictionary<CL_SaveManager.SessionSave, SavedLevelAnchor> SnapshotAnchors = new();

	public static void SceneChanged(string scene) {
		LoadedNativeSession = false;
		_pendingContinuation = null;
		_pendingNativeSnapshot = null;
		_nativeContinuation = null;
		SnapshotAnchors.Clear();
		NativeContinuePending = scene == "Game-Main" && CL_SaveManager.loadSessionSaveOnLoad;
	}

	public static void BeginNativeContinue() => NativeContinuePending = true;
	public static void NativeContinueStarted() => NativeContinuePending = false;

	public static void NativeSaveLoaded(CL_SaveManager.SaveState state, CL_SaveManager.SessionSave? save) {
		if (save == null) return;
		LoadedNativeSession = true;
		_pendingContinuation = state;
		_pendingNativeSnapshot = save;
		var savedAnchor = ReadFlags(save.sessionFlags)?.worldAnchor;
		if (savedAnchor != null) RememberAnchor(save, savedAnchor);
		// The current native LoadSession restores room data but does not assign
		// SessionSave.worldSeed. Restore it before the saved generation begins,
		// otherwise next-day lobby identities/future rooms get a new random seed.
		if (WorldLoader.instance != null) {
			WorldLoader.instance.seed = save.worldSeed;
			WorldLoader.instance.startingSeed = save.worldSeed;
		}
	}

	public static void ObserveNativeWorld() {
		if (_pendingContinuation == null || !MPCore.IsWorldReady()) return;
		var state = _pendingContinuation;
		_pendingContinuation = null;
		try {
			_nativeContinuation = CheckpointSyncManager.CaptureNativeContinuation(state);
			if (_pendingNativeSnapshot != null && !SnapshotAnchors.ContainsKey(_pendingNativeSnapshot)) {
				var anchor = CheckpointSyncManager.CaptureSaveAnchor();
				if (anchor != null) RememberAnchor(_pendingNativeSnapshot, anchor);
			}
		} catch (Exception ex) {
			MPMain.LogWarning($"[MP Save] Could not capture native continuation geometry; no player/save data was changed: {ex}");
		}
	}

	public static void OffsetContinuation(float offset) {
		if (_nativeContinuation != null) _nativeContinuation.levelPosition += Vector3.up * offset;
		foreach (var anchor in SnapshotAnchors.Values) anchor.position[1] += offset;
	}

	private static void RememberAnchor(CL_SaveManager.SessionSave save, SavedLevelAnchor anchor) {
		if (SnapshotAnchors.Count >= 8 && !SnapshotAnchors.ContainsKey(save)) SnapshotAnchors.Remove(SnapshotAnchors.Keys.First());
		SnapshotAnchors[save] = new SavedLevelAnchor {
			route = anchor.route, levelName = anchor.levelName, levelOrdinal = anchor.levelOrdinal,
			position = (float[])anchor.position.Clone(), rotation = (float[])anchor.rotation.Clone()
		};
	}

	public static void RememberNativeSnapshot(CL_SaveManager.SessionSave save) {
		if (save == null || !MPCore.IsWorldReady()) return;
		try {
			var anchor = CheckpointSyncManager.CaptureSaveAnchor();
			if (anchor != null) RememberAnchor(save, anchor);
		} catch (Exception ex) {
			MPMain.LogWarning($"[MP Save] Could not remember the native snapshot anchor; native saving was not interrupted: {ex.Message}");
		}
	}

	public static string Signature(GameModeData data) => RunSaveCodec.Signature(JsonConvert.SerializeObject(data));

	public static RunSaveData? ReadCurrent() => ReadFlags(CL_GameManager.gMan?.sessionFlags);

	private static RunSaveData? ReadFlags(List<CL_GameManager.SessionFlag>? flags) {
		var flag = flags?.FirstOrDefault(f => f != null && f.name == FlagName && f.state);
		if (flag == null) return null;
		try { return RunSaveCodec.Decode(flag.data); }
		catch (Exception ex) {
			MPMain.LogWarning($"[MP Save] Ignoring incompatible/corrupt multiplayer metadata; native save is unchanged: {ex.Message}");
			return null;
		}
	}

	private static RunSaveData Create(GameModeData data, string runId, bool host) => new() {
		runId = runId, playerId = MPSteamworks.UserSteamId, seed = data.seed!.Value,
		modeJson = JsonConvert.SerializeObject(data), modeSignature = Signature(data), wasHost = host
	};

	public static void BindHost(GameModeData mode) {
		if (!mode.seed.HasValue) return;
		ObserveNativeWorld();
		var saved = ReadCurrent();
		bool continuing = SavedRunPolicy.CanHost(saved, MPSteamworks.UserSteamId, mode.seed.Value, Signature(mode));
		string runId = continuing ? saved!.runId : Guid.NewGuid().ToString("N");
		// Scope checkpoint/death state before restoring it; the same lobby/seed
		// can now contain a different run after a native restart.
		MPSteamworks.Instance.SetLobbyData(MPKeys.RUN_ID, runId);
		if (continuing) {
			CheckpointSyncManager.Current?.RestoreSavedRun(saved!);
			SceneItemModule.Instance.RestoreSavedTombstones(saved!.itemTombstones);
			EnemySyncModule.Instance.RestoreSavedDeaths(saved.enemyDeaths);
		}
		CheckpointSyncManager.Current?.AttachNativeContinuation(_nativeContinuation);
		var current = continuing ? saved! : Create(mode, runId, true);
		current.wasHost = true;
		WriteCurrent(current);
		RefreshBeforeSave();
		MPMain.LogInfo(continuing
			? "[MP Save] Resumed the saved multiplayer run in a new lobby; native player/inventory were not reloaded."
			: "[MP Save] Attached a new multiplayer session to the loaded run without restarting it.");
	}

	/// <summary>An explicitly continued local save can be attached without another scene load.</summary>
	public static bool TryUseLoadedClientSave(GameModeData mode, string runId) {
		if (!LoadedNativeSession || !MPCore.IsWorldReady() || !mode.seed.HasValue) return false;
		var saved = ReadCurrent();
		if (!SavedRunPolicy.CanResume(saved, runId, MPSteamworks.UserSteamId, mode.seed.Value, Signature(mode))
			|| WorldLoader.instance.seed != mode.seed.Value || CL_GameManager.gamemode.name != mode.gameModeName) return false;
		MPGameModeManager.ResumeLoadedGameMode(mode);
		return true;
	}

	/// <summary>Only the local player's matching run token may opt into native Continue on join.</summary>
	public static bool HasMatchingClientSave(GameModeData mode, string runId) {
		if (!SavedRunPolicy.IsRunId(runId) || !mode.seed.HasValue
			|| !MPGameModeManager.TryGetGameMode(mode.gameModeName, out var asset)) return false;
		string name = asset.GetGamemodeName(false).ToLowerInvariant();
		foreach (string path in new[] { name, name + "-hardmode" }) {
			try {
				var save = CL_SaveManager.LoadSessionFromFile(path);
				if (save == null || save.version != CL_SaveManager.saveVersion || save.worldSeed != mode.seed.Value) continue;
				if (SavedRunPolicy.CanResume(ReadFlags(save.sessionFlags), runId, MPSteamworks.UserSteamId,
					mode.seed.Value, Signature(mode))) return true;
			} catch (Exception ex) {
				MPMain.LogWarning($"[MP Save] Could not inspect local save '{path}'; using normal join instead: {ex.Message}");
			}
		}
		return false;
	}

	public static bool RestoreClientProgress(GameModeData mode, string runId) {
		if (!mode.seed.HasValue) return false;
		var saved = ReadCurrent();
		if (!SavedRunPolicy.CanResume(saved, runId, MPSteamworks.UserSteamId, mode.seed.Value, Signature(mode))) return false;
		CheckpointSyncManager.Current?.RestoreSavedRun(saved!);
		return true;
	}

	public static void BindClient(GameModeData mode, string runId) {
		if (!SavedRunPolicy.IsRunId(runId) || !mode.seed.HasValue) return;
		var saved = ReadCurrent();
		var current = SavedRunPolicy.CanResume(saved, runId, MPSteamworks.UserSteamId, mode.seed.Value, Signature(mode))
			? saved! : Create(mode, runId, false);
		current.wasHost = false;
		WriteCurrent(current);
		RefreshBeforeSave();
	}

	private static void WriteCurrent(RunSaveData data) =>
		CL_GameManager.SetGameFlag(FlagName, true, RunSaveCodec.Encode(data), false, false);

	/// <summary>Called before a native SaveSession snapshot is made, not a separate save writer.</summary>
	public static void RefreshBeforeSave() {
		if (!MPCore.IsWorldReady() || NativeContinuePending || CL_GameManager.IsLoading()
			|| CheckpointSyncManager.IsApplying || CL_GameManager.gMan == null
			|| (MPCore.IsInLobby && !MPCore.IsReady)) return;
		try {
			var current = ReadCurrent();
			var mode = MPGameModeManager.CurrentData;
			if (mode?.seed != null && mode.seed.Value == WorldLoader.instance.seed && MPSteamworks.Instance.IsInLobby) {
				string runId = MPSteamworks.Instance._currentLobby.GetData(MPKeys.RUN_ID);
				if (!SavedRunPolicy.IsRunId(runId)) return;
				if (!SavedRunPolicy.CanResume(current, runId, MPSteamworks.UserSteamId, mode.seed.Value, Signature(mode)))
					current = Create(mode, runId, MPSteamworks.IsHost);
				current!.wasHost = MPSteamworks.IsHost;
			}
			if (current == null || current.playerId != MPSteamworks.UserSteamId || current.seed != WorldLoader.instance.seed) return;
			var anchor = CheckpointSyncManager.CaptureSaveAnchor();
			CheckpointSyncManager.Current?.ExportSavedRun(current, anchor);
			if (MPSteamworks.Instance.IsInLobby && MPCore.IsInitialized) {
				current.itemTombstones = SceneItemModule.Instance.ExportSavedTombstones();
				current.enemyDeaths = EnemySyncModule.Instance.ExportSavedDeaths();
			}
			WriteCurrent(current);
		} catch (Exception ex) {
			// Metadata failure must never prevent the native inventory/save operation.
			MPMain.LogWarning($"[MP Save] Could not refresh multiplayer metadata; native saving continues: {ex}");
		}
	}

	/// <summary>Refresh an existing native snapshot's metadata without changing its player/level data.</summary>
	public static void BeforeSaveToFile(CL_SaveManager.SessionSave save) {
		if (save == null || !MPCore.IsWorldReady() || CheckpointSyncManager.IsApplying || NativeContinuePending
			|| (MPCore.IsInLobby && !MPCore.IsReady)) return;
		try {
			var metadata = ReadFlags(save.sessionFlags);
			if (metadata == null || metadata.playerId != MPSteamworks.UserSteamId || metadata.seed != save.worldSeed
				|| metadata.seed != WorldLoader.instance.seed) return;
			// Keep the native snapshot's anchor. SaveLastSessionToFile can write an
			// earlier checkpoint rather than the room where the player is now walking.
			// Native sessionFlags are shared with the live manager. Keep a separate
			// bounded anchor per native snapshot so a newer flag update cannot pair
			// old saved room data with the player's current room by accident.
			var remembered = SnapshotAnchors.TryGetValue(save, out var known) ? known : metadata.worldAnchor;
			var anchor = CheckpointSyncManager.RefreshSaveAnchor(remembered) ?? remembered;
			if (anchor != null) CheckpointSyncManager.Current?.ExportSavedRun(metadata, anchor);
			if (MPSteamworks.Instance.IsInLobby && MPCore.IsInitialized) {
				metadata.itemTombstones = SceneItemModule.Instance.ExportSavedTombstones();
				metadata.enemyDeaths = EnemySyncModule.Instance.ExportSavedDeaths();
			}
			var flag = save.sessionFlags.First(f => f.name == FlagName);
			flag.data = RunSaveCodec.Encode(metadata);
		} catch (Exception ex) {
			MPMain.LogWarning($"[MP Save] Could not refresh saved metadata; native file writing continues: {ex}");
		}
	}
}
