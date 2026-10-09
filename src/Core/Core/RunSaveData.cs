using System;
using System.Collections.Generic;
using System.Linq;

namespace WKMPMod.Core;

/// <summary>Mod bookkeeping only. Native session saves remain responsible for each player's inventory/lives.</summary>
[Serializable]
public sealed class RunSaveData {
	public int version = 1;
	public string runId = string.Empty;
	public ulong playerId;
	public int seed;
	public string modeSignature = string.Empty;
	public string modeJson = string.Empty;
	public bool wasHost;
	public CheckpointProgress? checkpoint;
	public CheckpointProgress? deathCheckpoint;
	public bool pendingDeath;
	public string checkpointGeometry = string.Empty;
	public SavedLevelAnchor? worldAnchor;
	public List<SavedItemTombstones> itemTombstones = new();
	public List<SavedEnemyDeath> enemyDeaths = new();
}

[Serializable]
public sealed class SavedLevelAnchor {
	public string route = string.Empty;
	public string levelName = string.Empty;
	public int levelOrdinal;
	public float[] position = Array.Empty<float>();
	public float[] rotation = Array.Empty<float>();
}

[Serializable]
public sealed class SavedItemTombstones {
	public string team = string.Empty;
	public List<ulong> ids = new();

	/// <summary>Zero denotes an unassigned identity, never a real deletion. Merge unknown teams into the default.</summary>
	public static List<SavedItemTombstones> Normalize(IEnumerable<SavedItemTombstones> source, string defaultTeam) {
		if (string.IsNullOrWhiteSpace(defaultTeam)) throw new ArgumentException("A default team is required.", nameof(defaultTeam));
		var teams = new Dictionary<string, HashSet<ulong>>(StringComparer.Ordinal);
		foreach (var saved in source) {
			string team = string.IsNullOrWhiteSpace(saved.team) ? defaultTeam : saved.team;
			if (!teams.TryGetValue(team, out var ids)) teams[team] = ids = new();
			ids.UnionWith(saved.ids.Where(id => id != 0));
		}
		return teams.Select(pair => new SavedItemTombstones { team = pair.Key, ids = pair.Value.OrderBy(id => id).ToList() }).ToList();
	}
}

[Serializable]
public sealed class SavedEnemyDeath {
	public ulong id;
	public string type = string.Empty;
}

public static class SavedRunPolicy {
	public static bool IsRunId(string? value) => value?.Length == 32 && Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty;

	public static bool HasValidIdentity(RunSaveData? data) {
		if (data == null || data.version != 1 || !IsRunId(data.runId) || data.playerId == 0
			|| data.modeSignature?.Length != 64 || string.IsNullOrWhiteSpace(data.modeJson)) return false;
		foreach (char c in data.modeSignature)
			if (!(c is >= '0' and <= '9' or >= 'A' and <= 'F')) return false;
		return true;
	}

	public static bool CanResume(RunSaveData? data, string? runId, ulong playerId, int seed, string signature) =>
		HasValidIdentity(data) && IsRunId(runId) && data!.runId == runId
		&& data.playerId == playerId && data.seed == seed && data.modeSignature == signature;

	public static bool CanHost(RunSaveData? data, ulong playerId, int seed, string signature) =>
		HasValidIdentity(data) && data!.playerId == playerId && data.seed == seed && data.modeSignature == signature;

	public static bool IsValidAnchor(SavedLevelAnchor? anchor) {
		if (anchor == null || string.IsNullOrWhiteSpace(anchor.route) || anchor.route.Length > 512
			|| string.IsNullOrWhiteSpace(anchor.levelName) || anchor.levelName.Length > 256 || anchor.levelOrdinal < 0
			|| anchor.position?.Length != 3 || anchor.rotation?.Length != 4) return false;
		foreach (float value in anchor.position)
			if (!Finite(value)) return false;
		float norm = 0;
		foreach (float value in anchor.rotation) {
			if (!Finite(value)) return false;
			norm += value * value;
		}
		return norm is > 0.9f and < 1.1f;
	}

	private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) < 10000000f;
}
