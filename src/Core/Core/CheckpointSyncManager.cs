using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using WKMPMod.Components;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.Patch;
using WKMPMod.Util;
using WKMPMod.World;
using static WKMPMod.Data.MPWriterPool;

namespace WKMPMod.Core;

/// <summary>A geometry-only snapshot. No host inventory, perks, save lives or entity saves go over the wire.</summary>
[Serializable]
public sealed class HostCheckpointSnapshot {
	public int version = 1;
	public bool nativeContinuation;
	public ulong lobbyId;
	public int seed;
	public string modeSignature = string.Empty;
	public CheckpointProgress progress = new();
	public string levelName = string.Empty;
	public string regionName = string.Empty;
	public string subregionName = string.Empty;
	public int levelSeed;
	public List<WorldLoader.BranchInfo.LevelId> generation = new();
	public List<string> activeStates = new();
	public Vector3 levelPosition;
	public Quaternion levelRotation;
	public Vector3 relativePosition;
	public Quaternion playerRotation;
	public Quaternion cameraRotation;
}

public sealed class CheckpointSyncManager : MonoSingleton<CheckpointSyncManager> {
	public const byte ProtocolVersion = 2;
	public const string LobbyProtocol = "2";
	public static CheckpointSyncManager? Current { get; private set; }
	public static bool IsApplying { get; private set; }
	public bool HasPendingDeath => _session.PendingDeath;
	public bool IsResolving => _resolving;
	public CheckpointResolution LastResolution { get; private set; } = CheckpointResolution.NotAttempted;
	private bool _autoDeathAttempt;
	public bool SkipJoinCommands { get; private set; }
	private readonly CheckpointSessionState _session = new();
	private HostCheckpointSnapshot? _hostCheckpoint;
	private HostCheckpointSnapshot? _hostContinuation;
	private CheckpointRequestScope? _request;
	private HostCheckpointSnapshot? _reply;
	private CheckpointSpawnDecision _decision;
	private bool _receivedReply;
	private bool _resolving;
	private ulong _attempt;
	private float _nextDeathRetry;
	private int _sceneRevision;
	private readonly Dictionary<ulong, float> _requestTimes = new();
	private static readonly AccessTools.FieldRef<WorldLoader.LevelInfo, WorldLoader.BranchInfo.LevelId> LevelId =
		AccessTools.FieldRefAccess<WorldLoader.LevelInfo, WorldLoader.BranchInfo.LevelId>("levelID");
	private static readonly AccessTools.FieldRef<WorldLoader, int> GenerationCounter =
		AccessTools.FieldRefAccess<WorldLoader, int>("idCounter");
	private static readonly AccessTools.FieldRef<Inventory, List<Inventory.Pouch>> NativePouches =
		AccessTools.FieldRefAccess<Inventory, List<Inventory.Pouch>>("pouches");
	private static readonly Action<Inventory> ClearBagAndHands =
		AccessTools.MethodDelegate<Action<Inventory>>(AccessTools.Method(typeof(Inventory), "ClearInventory"));

	protected override void Awake() {
		base.Awake();
		Current = this;
		SceneManager.sceneLoaded += SceneLoaded;
	}

	protected override void OnDestroy() {
		SceneManager.sceneLoaded -= SceneLoaded;
		CancelRequest();
		if (Current == this) Current = null;
		base.OnDestroy();
	}

	private void SceneLoaded(Scene scene, LoadSceneMode mode) {
		if (mode != LoadSceneMode.Single) return;
		_sceneRevision++;
		CancelRequest();
		_hostCheckpoint = null;
		_hostContinuation = null;
		// Loaded transforms/geometry cannot survive a scene generation. Keep only
		// the guest's death tombstone, which BindCurrentSession now scopes to RunId.
	}

	private void Update() {
		if (!Ready() || !BindCurrentSession()) return;
		if (_session.PendingDeath && !MPSteamworks.IsHost && !_resolving && !_autoDeathAttempt
			&& MPCore.IsReady && Time.unscaledTime >= _nextDeathRetry) {
			_nextDeathRetry = Time.unscaledTime + 5f;
			_autoDeathAttempt = true;
			StartCoroutine(AutomaticDeathSpawn());
		}
	}

	private IEnumerator AutomaticDeathSpawn() {
		try { yield return ResolveSpawn(CheckpointSpawnReason.DeathRespawn); }
		finally { _autoDeathAttempt = false; _nextDeathRetry = Time.unscaledTime + 5f; }
	}

	private static bool Ready() => MPCore.IsInLobby && MPSteamworks.Instance.IsInLobby
		&& SceneManager.GetActiveScene().name == "Game-Main" && WorldLoader.instance != null
		&& WorldLoader.isLoaded && ENT_Player.GetPlayer() != null && !ENT_Player.GetPlayer().IsDead()
		&& !CL_GameManager.IsLoading() && !CL_GameManager.runHasEnded;

	private bool BindCurrentSession() {
		var data = MPGameModeManager.CurrentData;
		if (data?.seed == null || WorldLoader.instance == null || data.seed.Value != WorldLoader.instance.seed) return false;
		string signature = Signature(data);
		ulong lobby = MPSteamworks.Instance.LobbyId;
		string runId = MPSteamworks.Instance._currentLobby.GetData(MPKeys.RUN_ID);
		if (!SavedRunPolicy.IsRunId(runId)) return false;
		if (!_session.Matches(lobby, data.seed.Value, signature, runId)) {
			MPMain.LogInfo($"[MP Checkpoint] Binding run {runId}; clearing checkpoint/death state from the previous run.");
			if (_request != null) CancelRequest();
			_session.Bind(lobby, data.seed.Value, signature, runId);
			_hostCheckpoint = null;
			_hostContinuation = null;
			_requestTimes.Clear();
		}
		return true;
	}

	private static string Signature(MPGameModeManager.GameModeData data) => NativeRunPersistence.Signature(data);

	private static string Route(WorldLoader.BranchInfo branch) {
		var parts = new List<string>();
		for (var b = branch; b != null; b = b.parent) {
			string parent = b.parentLevel != null ? $"@{LevelId(b.parentLevel).level}:{LevelId(b.parentLevel).id}" : string.Empty;
			parts.Add(b.id + parent);
		}
		parts.Reverse();
		return string.Join("/", parts);
	}

	private static CheckpointProgress PositionProgress(bool reached) {
		var info = WorldLoader.GetCurrentLevelFromBounds();
		if (info == null || info.branch == null) throw new InvalidOperationException("No checkpoint level is loaded.");
		return new CheckpointProgress {
			route = Route(info.branch), levelOrdinal = LevelId(info).id,
			travelDistance = Math.Max(0, CL_GameManager.GetPlayerLastTravelDistance()), reached = reached
		};
	}

	/// <summary>Called only by actual checkpoint/save events, never by periodic autosaves.</summary>
	public void CaptureCheckpoint(CL_SaveManager.SaveState? nativeSave = null) {
		if (IsApplying || _session.PendingDeath || !Ready() || !BindCurrentSession()) return;
		try {
			var mark = PositionProgress(true);
			_session.SetCheckpoint(mark);
			if (!MPSteamworks.IsHost) return;
			var info = WorldLoader.GetCurrentLevelFromBounds();
			var level = info.GetLevel();
			if (level == null) return;
			// Use the game's checkpoint saver only to capture relative pose/generation.
			// This unregistered in-memory object never consumes lives or writes a file.
			var save = nativeSave ?? new CL_SaveManager.SaveState {
				type = CL_SaveManager.SaveState.SaveType.session, gamemode = CL_GameManager.gamemode.name
			};
			if (nativeSave == null) save.Save(new[] { "savetype:session" });
			if (save.playerData == null || save.levelData == null || save.levelData.Count == 0) return;
			var savedLevel = save.levelData[0];
			var ids = info.branch.GetLevelIds();
			int index = ids.IndexOf(LevelId(info));
			if (index < 0) throw new InvalidOperationException("Checkpoint is missing from the branch generation history.");
			var snapshot = new HostCheckpointSnapshot {
				lobbyId = _session.LobbyId, seed = _session.Seed, modeSignature = _session.ModeSignature,
				progress = mark.Copy(), levelName = savedLevel.levelName, regionName = savedLevel.regionName,
				subregionName = savedLevel.subregionName, levelSeed = savedLevel.seed,
				generation = ids.Skip(index).Take(256).Select(x => new WorldLoader.BranchInfo.LevelId {
					id = x.id, level = x.level, region = x.region, subRegion = x.subRegion
				}).ToList(),
				activeStates = savedLevel.activeStates?.ToList() ?? new(),
				levelPosition = level.transform.position, levelRotation = level.transform.rotation,
				relativePosition = save.playerData.positionRelativeToLevel,
				playerRotation = save.playerData.playerRotation, cameraRotation = save.playerData.playerCameraRotation
			};
			Validate(snapshot);
			_hostCheckpoint = snapshot;
			MPMain.LogInfo(Localization.Get("Checkpoint.Captured", savedLevel.levelName, mark.levelOrdinal));
			NativeRunPersistence.RefreshBeforeSave();
		} catch (Exception ex) {
			MPMain.LogWarning(Localization.Get("Checkpoint.Failed", ex.Message));
		}
	}

	/// <summary>Capture only the native load event's saved respawn pose, never the host's later walking position.</summary>
	public static HostCheckpointSnapshot? CaptureNativeContinuation(CL_SaveManager.SaveState save) {
		if (!MPCore.IsWorldReady() || save.playerData == null || save.levelData?.Count is not > 0) return null;
		var info = WorldLoader.GetCurrentLevelFromBounds();
		var level = info?.GetLevel();
		var savedLevel = save.levelData[0];
		if (info?.branch == null || level == null || LevelId(info).level != savedLevel.levelName) return null;
		var ids = info.branch.GetLevelIds();
		int index = ids.IndexOf(LevelId(info));
		if (index < 0) return null;
		var mode = MPGameModeManager.CaptureCurrentModeData(setCurrent: false);
		var snapshot = new HostCheckpointSnapshot {
			nativeContinuation = true, seed = WorldLoader.instance.seed, modeSignature = Signature(mode),
			progress = new CheckpointProgress {
				route = Route(info.branch), levelOrdinal = LevelId(info).id,
				travelDistance = Math.Max(0, save.playerTravelDistance), reached = false
			},
			levelName = savedLevel.levelName, regionName = savedLevel.regionName,
			subregionName = savedLevel.subregionName, levelSeed = savedLevel.seed,
			generation = ids.Skip(index).Take(256).Select(id => new WorldLoader.BranchInfo.LevelId {
				id = id.id, level = id.level, region = id.region, subRegion = id.subRegion
			}).ToList(),
			activeStates = savedLevel.activeStates?.ToList() ?? new(),
			levelPosition = level.transform.position, levelRotation = level.transform.rotation,
			relativePosition = savedLevel.playerRespawnPositionRelativeToLevel,
			playerRotation = save.playerData.playerRotation, cameraRotation = save.playerData.playerCameraRotation
		};
		Validate(snapshot);
		return snapshot;
	}

	public void AttachNativeContinuation(HostCheckpointSnapshot? native) {
		if (native == null || !Ready() || !BindCurrentSession() || native.seed != _session.Seed
			|| native.modeSignature != _session.ModeSignature) return;
		_hostContinuation = CheckpointSnapshotJson.Clone(native);
		_hostContinuation.lobbyId = _session.LobbyId;
	}

	public static SavedLevelAnchor? CaptureSaveAnchor() {
		var info = WorldLoader.GetCurrentLevelFromBounds();
		var level = info?.GetLevel();
		if (info?.branch == null || level == null) return null;
		var position = level.transform.position;
		var rotation = level.transform.rotation;
		return new SavedLevelAnchor {
			route = Route(info.branch), levelName = LevelId(info).level, levelOrdinal = LevelId(info).id,
			position = new[] { position.x, position.y, position.z },
			rotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w }
		};
	}

	private static WorldLoader.LevelInfo? FindLoadedSaveAnchor(SavedLevelAnchor? saved) {
		if (!SavedRunPolicy.IsValidAnchor(saved) || WorldLoader.instance == null) return null;
		return WorldLoader.instance.branchTracker.SelectMany(branch => branch.levelTracker)
			.FirstOrDefault(info => info.GetLevel() != null && LevelId(info).id == saved!.levelOrdinal
				&& LevelId(info).level == saved.levelName);
	}

	public static SavedLevelAnchor? RefreshSaveAnchor(SavedLevelAnchor? saved) {
		var info = FindLoadedSaveAnchor(saved);
		var level = info?.GetLevel();
		if (level == null) return null;
		var position = level.transform.position;
		var rotation = level.transform.rotation;
		return new SavedLevelAnchor {
			route = Route(info!.branch), levelName = saved!.levelName, levelOrdinal = saved.levelOrdinal,
			position = new[] { position.x, position.y, position.z },
			rotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w }
		};
	}

	/// <summary>Serialize checkpoint geometry relative to the native save's level, never a world-space spawn guess.</summary>
	public void ExportSavedRun(RunSaveData save, SavedLevelAnchor? anchor) {
		if (_session.Seed != save.seed || _session.ModeSignature != save.modeSignature || _session.RunId != save.runId
			|| !SavedRunPolicy.IsValidAnchor(anchor)) return;
		save.checkpoint = _session.LocalCheckpoint?.Copy();
		save.deathCheckpoint = _session.DeathCheckpoint?.Copy();
		save.pendingDeath = _session.PendingDeath;
		save.worldAnchor = anchor;
		save.checkpointGeometry = string.Empty;
		if (!save.wasHost || _hostCheckpoint == null || _hostCheckpoint.progress.route != anchor!.route) return;
		// Clone before converting coordinate frames so the live network snapshot is unchanged.
		var snapshot = CheckpointSnapshotJson.Clone(_hostCheckpoint);
		var position = new Vector3(anchor.position[0], anchor.position[1], anchor.position[2]);
		var rotation = new Quaternion(anchor.rotation[0], anchor.rotation[1], anchor.rotation[2], anchor.rotation[3]);
		snapshot.levelPosition = Quaternion.Inverse(rotation) * (snapshot.levelPosition - position);
		snapshot.levelRotation = Quaternion.Inverse(rotation) * snapshot.levelRotation;
		Validate(snapshot);
		save.checkpointGeometry = Convert.ToBase64String(CheckpointSnapshotCodec.Encode(CheckpointSnapshotJson.Serialize(snapshot)));
	}

	public void RestoreSavedRun(RunSaveData save) {
		if (!Ready() || !BindCurrentSession() || _session.Seed != save.seed || _session.ModeSignature != save.modeSignature
			|| _session.RunId != save.runId) return;
		try {
			var anchorInfo = FindLoadedSaveAnchor(save.worldAnchor);
			string? route = anchorInfo?.branch != null ? Route(anchorInfo.branch) : null;
			CheckpointProgress? Remap(CheckpointProgress? progress) {
				var result = progress?.Copy();
				if (result != null && route != null && result.route == save.worldAnchor?.route) result.route = route;
				return result;
			}
			_session.RestoreProgress(Remap(save.checkpoint), Remap(save.deathCheckpoint), save.pendingDeath);
			if (MPSteamworks.IsHost && _session.PendingDeath) _session.CompleteRespawn();
			if (!MPSteamworks.IsHost || !save.wasHost || anchorInfo?.GetLevel() == null || string.IsNullOrEmpty(save.checkpointGeometry)) return;
			var snapshot = CheckpointSnapshotJson.DeserializeSaved<HostCheckpointSnapshot>(
				CheckpointSnapshotCodec.Decode(Convert.FromBase64String(save.checkpointGeometry)), save.checkpoint);
			Validate(snapshot);
			if (snapshot.seed != _session.Seed || snapshot.modeSignature != _session.ModeSignature
				|| snapshot.progress.route != save.worldAnchor!.route) return;
			// Native Continue can rebase the world. Align to a matching generation ID,
			// not the first prefab with the same room name and not the current player pose.
			var level = anchorInfo.GetLevel();
			snapshot.levelPosition = level.transform.TransformPoint(snapshot.levelPosition);
			snapshot.levelRotation = level.transform.rotation * snapshot.levelRotation;
			snapshot.progress.route = route!;
			snapshot.lobbyId = _session.LobbyId;
			Validate(snapshot);
			_hostCheckpoint = snapshot;
			MPMain.LogInfo($"[MP Save] Restored host checkpoint geometry: {snapshot.levelName} (level {snapshot.progress.levelOrdinal}).");
		} catch (Exception ex) {
			MPMain.LogWarning($"[MP Save] Could not restore checkpoint metadata; native player/save is unchanged: {ex}");
		}
	}

	public void OffsetHostCheckpoint(float offset) {
		NativeRunPersistence.OffsetContinuation(offset);
		if (!MPSteamworks.IsHost) return;
		if (_hostCheckpoint != null && _hostCheckpoint.lobbyId == MPSteamworks.Instance.LobbyId)
			_hostCheckpoint.levelPosition += Vector3.up * offset;
		if (_hostContinuation != null && _hostContinuation.lobbyId == MPSteamworks.Instance.LobbyId)
			_hostContinuation.levelPosition += Vector3.up * offset;
	}

	public void RecordDeath() {
		if (IsApplying || _session.LobbyId == 0 || WorldLoader.instance == null || MPSteamworks.IsHost) return;
		if (_session.Seed != WorldLoader.instance.seed) return;
		try {
			_session.RecordDeath(PositionProgress(false));
			// Die is patched before the native player becomes dead. Persist the
			// tombstone into the existing session flags before native quit/death saving.
			NativeRunPersistence.RefreshBeforeSave();
			MPCore.Instance.InvalidateReconnectAfterDeath();
		} catch (Exception ex) { MPMain.LogWarning(Localization.Get("Checkpoint.Failed", ex.Message)); }
	}

	public void CancelRequest() {
		_attempt++;
		_request = null;
		_reply = null;
		_receivedReply = false;
	}

	/// <summary>Resolve a late join or a post-death spawn. An alive reconnect never calls this.</summary>
	public IEnumerator ResolveSpawn(CheckpointSpawnReason reason) {
		if (_resolving) yield break;
		LastResolution = CheckpointResolution.Failed;
		if (SceneManager.GetActiveScene().name == "Playground" && MPCore.IsInLobby) {
			LastResolution = CheckpointResolution.Normal;
			yield break; // Playground has no generated campaign checkpoint world.
		}
		if (!Ready() || !BindCurrentSession()) yield break;
		if (_session.PendingDeath) reason = CheckpointSpawnReason.DeathRespawn;
		SkipJoinCommands = reason == CheckpointSpawnReason.DeathRespawn;
		if (reason == CheckpointSpawnReason.LiveReconnect || MPSteamworks.IsHost) {
			LastResolution = CheckpointResolution.Normal;
			yield break;
		}
		var net = MPSteamworks.Instance;
		string rule = reason == CheckpointSpawnReason.DeathRespawn ? MPKeys.RESPAWN_AT_CHECKPOINT : MPKeys.JOIN_AT_CHECKPOINT;
		string protocol = net._currentLobby.GetData(MPKeys.CHECKPOINT_PROTOCOL);
		if (!bool.TryParse(net._currentLobby.GetData(rule), out var enabled) || !enabled) {
			MPMain.LogInfo($"[MP Checkpoint] {reason}: checkpoint rule disabled; keeping normal spawn.");
			if (reason == CheckpointSpawnReason.DeathRespawn) _session.CompleteRespawn();
			LastResolution = CheckpointResolution.Normal;
			yield break;
		}
		if (protocol != LobbyProtocol) {
			MPMain.LogWarning($"[MP Checkpoint] Host protocol '{protocol}' cannot provide a run-scoped checkpoint reply; both players must update. Refusing normal-spawn fallback.");
			yield break;
		}
		CheckpointProgress own;
		try { own = (_session.DeathCheckpoint ?? _session.LocalCheckpoint ?? PositionProgress(false)).Copy(); }
		catch (Exception ex) { MPMain.LogWarning(Localization.Get("Checkpoint.Failed", ex.Message)); yield break; }
		_resolving = true;
		ulong attempt = ++_attempt;
		_request = new CheckpointRequestScope(attempt, net.LobbyId, net.HostSteamId,
			_session.Seed, _sceneRevision, _session.ModeSignature, _session.RunId);
		MPMain.LogInfo($"[MP Checkpoint] Requesting {reason} for run {_session.RunId}; own route={own.route}, level={own.levelOrdinal}, distance={own.travelDistance}, pendingDeath={_session.PendingDeath}.");
		_reply = null;
		_receivedReply = false;
		float deadline = Time.unscaledTime + 30f;
		float nextSend = -1f;
		try {
			while (!_receivedReply && _request != null && _attempt == attempt && Ready()
				&& net.LobbyId == _request.LobbyId && net._currentLobby.GetData(MPKeys.RUN_ID) == _request.RunId
				&& Time.unscaledTime < deadline) {
				if (net.IsConnectedTo(_request.HostId) && Time.unscaledTime >= nextSend) {
					nextSend = Time.unscaledTime + 2f;
					var writer = GetWriter(MPSteamworks.UserSteamId, _request.HostId, PacketType.CheckpointSpawn);
					writer.Put(ProtocolVersion).Put(false).Put(attempt).Put(_request.LobbyId).Put(_request.Seed)
						.Put(_request.ModeSignature).Put(_request.RunId).Put((byte)reason).Put(JsonConvert.SerializeObject(own));
					net.SendToHost(writer);
				}
				yield return null;
			}
			if (_attempt != attempt || _request == null || !Ready()
				|| net._currentLobby.GetData(MPKeys.RUN_ID) != _request.RunId) yield break;
			if (!_receivedReply) {
				MPMain.LogWarning(Localization.Get("Checkpoint.Timeout"));
				yield break; // Keep a death tombstone; retry rather than treating it as an alive reconnect.
			}
			if (_decision == CheckpointSpawnDecision.Normal) {
				if (reason == CheckpointSpawnReason.DeathRespawn) _session.CompleteRespawn();
				LastResolution = CheckpointResolution.Normal;
				yield break;
			}
			if (_reply == null || CheckpointPolicy.Decide(reason, true, _reply.progress, own, _reply.nativeContinuation) != _decision) yield break;
			bool empty = _decision == CheckpointSpawnDecision.HostEmpty;
			bool applied = false;
			yield return GuardedApply(_reply, empty, attempt, value => applied = value);
			if (applied && _attempt == attempt) {
				LastResolution = CheckpointResolution.Applied;
				_session.CompleteRespawn(_reply.progress);
				MPCore.Instance.RefreshAfterCheckpoint();
				MPMain.LogInfo(Localization.Get(empty ? "Checkpoint.SpawnedEmpty" : "Checkpoint.Spawned", _reply.levelName));
			}
		} finally {
			if (_attempt == attempt) { _request = null; _reply = null; }
			_resolving = false;
		}
	}

	public void HandlePacket(ulong sender, DataReader reader) {
		if (reader.AvailableBytes < 22 || reader.GetByte() != ProtocolVersion) return;
		bool response = reader.GetBool();
		ulong attempt = reader.GetULong();
		ulong lobby = reader.GetULong();
		int seed = reader.GetInt();
		if (reader.AvailableBytes < 4) return;
		string signature = reader.GetString();
		if (signature.Length != 64 || reader.AvailableBytes < 4) return;
		string runId = reader.GetString();
		if (!SavedRunPolicy.IsRunId(runId)) return;
		if (!response) {
			if (!Ready() || !BindCurrentSession() || !MPSteamworks.IsHost || !MPCore.IsReady || attempt == 0
				|| !MPSteamworks.Instance.IsMemberInLobby(sender) || !_session.Matches(lobby, seed, signature, runId)) return;
			if (_requestTimes.TryGetValue(sender, out var last) && Time.unscaledTime - last < 0.5f) return;
			_requestTimes[sender] = Time.unscaledTime;
			var reason = (CheckpointSpawnReason)reader.GetByte();
			string ownJson = reader.GetString();
			if (ownJson.Length > 2048 || reason is not (CheckpointSpawnReason.LateJoin or CheckpointSpawnReason.DeathRespawn)) return;
			var own = JsonConvert.DeserializeObject<CheckpointProgress>(ownJson);
			bool enabled = reason == CheckpointSpawnReason.LateJoin ? MPConfig.JoinAtHostCheckpoint : MPConfig.RespawnAtHostCheckpoint;
			var target = _hostCheckpoint ?? (reason == CheckpointSpawnReason.LateJoin ? _hostContinuation : null);
			var choice = CheckpointPolicy.Evaluate(reason, enabled, target?.progress, own, target?.nativeContinuation ?? false);
			var decision = choice.Decision;
			var replyReason = choice.Reason;
			byte[]? snapshot = null;
			if (decision is CheckpointSpawnDecision.HostEmpty or CheckpointSpawnDecision.HostWithOwnItems) {
				try { snapshot = CheckpointSnapshotCodec.Encode(CheckpointSnapshotJson.Serialize(target)); }
				catch (Exception ex) {
					MPMain.LogWarning(Localization.Get("Checkpoint.Failed", ex.Message));
					decision = CheckpointSpawnDecision.Failed;
					replyReason = CheckpointDecisionReason.SnapshotEncodingFailed;
				}
			}
			MPMain.LogInfo($"[MP Checkpoint] Host reply: request={reason}, decision={decision}, reason={replyReason}, run={runId}; target={target?.levelName ?? "none"}, route={target?.progress.route ?? "none"}, level={target?.progress.levelOrdinal}, distance={target?.progress.travelDistance}; guest route={own?.route}, level={own?.levelOrdinal}, distance={own?.travelDistance}.");
			var writer = GetWriter(MPSteamworks.UserSteamId, sender, PacketType.CheckpointSpawn);
			writer.Put(ProtocolVersion).Put(true).Put(attempt).Put(lobby).Put(seed).Put(signature).Put(runId)
				.Put((byte)decision).Put((byte)replyReason);
			if (snapshot != null) writer.Put(snapshot.AsSpan());
			MPSteamworks.Instance.SendToPeer(sender, writer);
		} else {
			var net = MPSteamworks.Instance;
			if (_request == null || !_request.Accepts(sender, attempt, lobby, seed, signature,
				net.LobbyId, net.HostSteamId, _sceneRevision, runId, net._currentLobby.GetData(MPKeys.RUN_ID))
				|| _receivedReply || !Ready() || reader.AvailableBytes < 2) return;
			var decision = (CheckpointSpawnDecision)reader.GetByte();
			var replyReason = (CheckpointDecisionReason)reader.GetByte();
			if (decision is not (CheckpointSpawnDecision.Normal or CheckpointSpawnDecision.HostEmpty
				or CheckpointSpawnDecision.HostWithOwnItems or CheckpointSpawnDecision.Failed)
				|| !Enum.IsDefined(typeof(CheckpointDecisionReason), replyReason)) return;
			if (decision is CheckpointSpawnDecision.HostEmpty or CheckpointSpawnDecision.HostWithOwnItems) {
				if (reader.AvailableBytes < 4 || reader.AvailableBytes > CheckpointSnapshotCodec.MaxCompressedBytes + 4) return;
				var payload = reader.GetMemory();
				try {
					var snapshot = CheckpointSnapshotJson.Deserialize<HostCheckpointSnapshot>(CheckpointSnapshotCodec.Decode(payload));
					Validate(snapshot);
					if (snapshot.lobbyId != lobby || snapshot.seed != seed || snapshot.modeSignature != signature)
						throw new InvalidDataException("Checkpoint snapshot does not match the requested lobby/run.");
					_reply = snapshot;
				} catch (Exception ex) {
					// A bad authenticated response is a failed handshake, not a normal
					// start-of-run spawn. Stop retries and keep the client unadmitted.
					_reply = null;
					_decision = CheckpointSpawnDecision.Failed;
					_receivedReply = true;
					MPMain.LogWarning(Localization.Get("Checkpoint.Failed", ex.Message));
					return;
				}
			}
			if (reader.AvailableBytes != 0) return;
			MPMain.LogInfo($"[MP Checkpoint] Host response: decision={decision}, reason={replyReason}, run={runId}; target={_reply?.levelName ?? "none"}.");
			_decision = decision;
			_receivedReply = true;
		}
	}

	private static void Validate(HostCheckpointSnapshot snapshot) {
		if (snapshot == null || snapshot.version != 1 || !CheckpointPolicy.IsValid(snapshot.progress)
			|| (snapshot.nativeContinuation ? snapshot.progress.reached : !snapshot.progress.reached) || string.IsNullOrWhiteSpace(snapshot.levelName) || snapshot.levelName.Length > 256
			|| string.IsNullOrWhiteSpace(snapshot.regionName) || snapshot.regionName.Length > 256
			|| snapshot.subregionName == null || snapshot.subregionName.Length > 256
			|| snapshot.modeSignature == null || snapshot.modeSignature.Length != 64
			|| snapshot.generation == null || snapshot.generation.Count is 0 or > 256
			|| snapshot.activeStates == null || snapshot.activeStates.Count > 128
			|| snapshot.generation[0] == null || snapshot.generation[0].level != snapshot.levelName
			|| snapshot.generation[0].id != snapshot.progress.levelOrdinal)
			throw new InvalidDataException("Invalid checkpoint geometry/identity.");
		foreach (var id in snapshot.generation)
			if (id == null || id.id < 0 || string.IsNullOrWhiteSpace(id.level) || id.level.Length > 256
				|| id.region?.Length > 256 || id.subRegion?.Length > 256) throw new InvalidDataException("Invalid checkpoint generation list.");
		foreach (var state in snapshot.activeStates)
			if (state == null || state.Length > 256) throw new InvalidDataException("Invalid checkpoint level state.");
		if (!Finite(snapshot.levelPosition) || !Finite(snapshot.relativePosition)
			|| !Finite(snapshot.levelRotation) || !Finite(snapshot.playerRotation) || !Finite(snapshot.cameraRotation))
			throw new InvalidDataException("Invalid checkpoint pose.");
	}
	private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x) && Math.Abs(x) < 10000000f;
	private static bool Finite(Vector3 x) => Finite(x.x) && Finite(x.y) && Finite(x.z);
	private static bool Finite(Quaternion x) => Finite(x.x) && Finite(x.y) && Finite(x.z) && Finite(x.w)
		&& x.x * x.x + x.y * x.y + x.z * x.z + x.w * x.w is > 0.9f and < 1.1f;

	private IEnumerator GuardedApply(HostCheckpointSnapshot snapshot, bool empty, ulong attempt, Action<bool> completed) {
		var routine = ApplyCheckpoint(snapshot, empty, attempt, completed);
		try {
			while (true) {
				bool next;
				try { next = routine.MoveNext(); }
				catch (Exception ex) {
					MPMain.LogWarning(Localization.Get("Checkpoint.Failed", ex.Message));
					completed(false);
					yield break;
				}
				if (!next) yield break;
				yield return routine.Current;
			}
		} finally { (routine as IDisposable)?.Dispose(); }
	}

	private IEnumerator ApplyCheckpoint(HostCheckpointSnapshot snapshot, bool empty, ulong attempt, Action<bool> completed) {
		var asset = CL_AssetManager.GetLevelAsset(snapshot.levelName, string.Empty);
		var region = CL_AssetManager.GetRegionAsset(snapshot.regionName, string.Empty);
		var player = ENT_Player.GetPlayer();
		if (asset == null || region == null || player == null)
			throw new InvalidOperationException($"Checkpoint assets/player unavailable: {snapshot.levelName}, {snapshot.regionName}.");
		MPMain.LogInfo($"[MP Checkpoint] Loading host level {snapshot.levelName} (id {snapshot.progress.levelOrdinal}, seed {snapshot.levelSeed}); aligning world before admission.");
		IsApplying = true;
		bool success = false;
		try {
			player.Lock();
			WorldSyncManager.Instance.PrepareForCheckpointLoad();
			var levelSave = new M_Level.SaveData {
				levelName = snapshot.levelName, regionName = snapshot.regionName, subregionName = snapshot.subregionName,
				seed = snapshot.levelSeed, activeStates = snapshot.activeStates,
				baseEntitySaves = new(), saveInfos = new(), playerRespawnLevel = true,
				playerRespawnPositionRelativeToLevel = snapshot.relativePosition
			};
			// Native UnloadAllLevels replaces the branch but retains idCounter;
			// saveLoad allocates fresh IDs instead of using saveLevels[].id. Without
			// this, host target 0 becomes client target 50 and admission rejects it.
			var loader = WorldLoader.instance;
			int counter = CheckpointGenerationIdentity.CounterStart(snapshot.progress.levelOrdinal);
			MPMain.LogInfo($"[MP Checkpoint] Resetting native generation counter {GenerationCounter(loader)} -> {counter} for the isolated host checkpoint world.");
			GenerationCounter(loader) = counter;
			loader.RestartGenerationFromLevelList(new WorldLoader.GenerationParameters {
				generationType = WorldLoader.GenerationParameters.GenerationType.saveLoad,
				targetLevel = asset.id, saveLevels = snapshot.generation, levelData = new() { levelSave },
				preloadLevels = new() { asset }, lastRegion = region, startRegion = region,
				setPlayerPositionToTargetLevel = true, loadSavedEntities = false, seedOffset = 0, flags = new()
			});
			float deadline = Time.unscaledTime + 60f;
			yield return null;
			while (!WorldLoader.isLoaded || CL_GameManager.IsLoading()) {
				if (!RequestStillCurrent(attempt, snapshot) || Time.unscaledTime >= deadline) yield break;
				yield return null;
			}
			if (!RequestStillCurrent(attempt, snapshot)) yield break;
			var currentInfo = loader.GetCurrentLevel();
			var level = currentInfo?.GetLevel();
			var actualId = currentInfo != null ? LevelId(currentInfo) : null;
			if (level == null || actualId == null || !CheckpointGenerationIdentity.MatchesTarget(
				snapshot.levelName, snapshot.progress.levelOrdinal, actualId.level, actualId.id))
				throw new InvalidOperationException($"Regenerated checkpoint level mismatch: expected {snapshot.levelName} (id {snapshot.progress.levelOrdinal}), actual {actualId?.level ?? "none"} (id {actualId?.id}).");
			MPMain.LogInfo($"[MP Checkpoint] Generated target verified: {actualId.level} (id {actualId.id}); completing alignment before multiplayer admission.");
			var root = GameObject.Find("World_Root(Clone)")?.transform;
			if (root == null || !level.transform.IsChildOf(root)) throw new InvalidOperationException("Checkpoint world root was not found.");
			Quaternion deltaRotation = snapshot.levelRotation * Quaternion.Inverse(level.transform.rotation);
			Vector3 nextRootPosition = snapshot.levelPosition + deltaRotation * (root.position - level.transform.position);
			root.SetPositionAndRotation(nextRootPosition, deltaRotation * root.rotation);
			Physics.SyncTransforms();
			foreach (var info in WorldLoader.instance.currentBranch.levelTracker) info.GetLevel()?.CycleColliders();
			yield return new WaitForFixedUpdate();
			if (!RequestStillCurrent(attempt, snapshot)) yield break;
			player.Lock();
			LocalPlayer.Instance.TriggerTeleport();
			Vector3 position = level.transform.TransformPoint(snapshot.relativePosition);
			player.Teleport(position, snapshot.playerRotation * level.transform.rotation, false);
			player.camTransform.localRotation = snapshot.cameraRotation;
			player.ResetCameraInterpolation();
			player.AddIgnoreFallDamageTime(1f);
			CL_GameManager.gMan.ResetPlayerDistanceCheckPosition(position, (float)snapshot.progress.travelDistance,
				0f, 1f, (float)snapshot.progress.travelDistance);
			if (empty) EmptyInventory();
			Physics.SyncTransforms();
			yield return new WaitForFixedUpdate();
			if (!RequestStillCurrent(attempt, snapshot)) yield break;
			if (empty) EmptyInventory(); // Clear any deferred starting-kit additions as well.
			success = true;
		} finally {
			IsApplying = false;
			if (player != null) player.UnLock();
			// Initial joins rebind/request history AFTER Core admits the aligned
			// world. Never start multiplayer snapshot work from a failed load.
			if (success && MPCore.IsReady && WorldLoader.instance != null && WorldLoader.isLoaded) {
				WorldSyncManager.Instance.RebindAfterCheckpointLoad();
				SceneItemModule.Instance.SendSceneRemoveChunkRequest();
				EnemySyncModule.Instance.SendKillChunkRequest();
				ClimbableSyncModule.Instance.SendChunkRequest();
			}
			completed(success);
		}
	}

	private bool RequestStillCurrent(ulong attempt, HostCheckpointSnapshot snapshot) =>
		_request != null && _attempt == attempt && _request.WorldRevision == _sceneRevision
		&& MPSteamworks.Instance.LobbyId == snapshot.lobbyId
		&& MPSteamworks.Instance.HostSteamId == _request.HostId
		&& MPSteamworks.Instance._currentLobby.GetData(MPKeys.RUN_ID) == _request.RunId
		&& MPGameModeManager.CurrentData != null && Signature(MPGameModeManager.CurrentData) == snapshot.modeSignature
		&& WorldLoader.instance != null && WorldLoader.instance.seed == snapshot.seed
		&& ENT_Player.GetPlayer() != null && !ENT_Player.GetPlayer().IsDead();

	private static void EmptyInventory() {
		var inventory = Inventory.instance;
		if (inventory == null) return;
		// Clear native bag/hands and all pouches, including pockets/extra pouches.
		var pouches = NativePouches(inventory).Concat(inventory.GetAllPouches()).Where(p => p != null).Distinct().ToArray();
		var items = inventory.GetItems(true, true, true)
			.Concat(pouches.SelectMany(p => p.pouchItems)).Distinct().ToArray();
		foreach (var item in items) if (item != null) item.Destroy(true);
		ClearBagAndHands(inventory);
		foreach (var pouch in pouches) pouch.pouchItems.Clear();
		inventory.RescanInventory();
		inventory.CalculateEncumberance(true);
	}
}
