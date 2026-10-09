using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WKMPMod.Core;

namespace WKMPMod.NetWork;

/// <summary>Bounded, versioned metadata stored in a namespaced native session flag.</summary>
public static class RunSaveCodec {
	public const int MaxChars = 2 * 1024 * 1024;
	public const int MaxWorldRecords = 20000;
	private static readonly JsonSerializerSettings Settings = new() {
		TypeNameHandling = TypeNameHandling.None, MaxDepth = 32
	};

	public static string Signature(string modeJson) {
		using var sha = SHA256.Create();
		return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(modeJson))).Replace("-", "");
	}

	public static string Encode(RunSaveData data) {
		Validate(data);
		string json = JsonConvert.SerializeObject(data, Settings);
		if (json.Length > MaxChars) throw new InvalidDataException("Multiplayer save metadata is too large.");
		return json;
	}

	public static RunSaveData Decode(string json) {
		if (string.IsNullOrWhiteSpace(json) || json.Length > MaxChars) throw new InvalidDataException("Invalid multiplayer save metadata size.");
		RunSaveData? data;
		try { data = JsonConvert.DeserializeObject<RunSaveData>(json, Settings); }
		catch (JsonException ex) { throw new InvalidDataException("Invalid multiplayer save metadata JSON.", ex); }
		Validate(data);
		return data!;
	}

	private static void Validate(RunSaveData? data) {
		if (!SavedRunPolicy.HasValidIdentity(data) || data!.modeJson.Length > 65536
			|| Signature(data.modeJson) != data.modeSignature
			|| (data.checkpoint != null && !CheckpointPolicy.IsValid(data.checkpoint))
			|| (data.deathCheckpoint != null && !CheckpointPolicy.IsValid(data.deathCheckpoint))
			|| (data.pendingDeath && data.deathCheckpoint == null)
			|| data.checkpointGeometry == null || data.checkpointGeometry.Length > 180000
			|| (data.worldAnchor != null && !SavedRunPolicy.IsValidAnchor(data.worldAnchor))
			|| data.itemTombstones == null || data.itemTombstones.Count > 128
			|| data.enemyDeaths == null || data.enemyDeaths.Count > MaxWorldRecords)
			throw new InvalidDataException("Invalid multiplayer save metadata.");
		long itemCount = 0;
		foreach (var team in data.itemTombstones) {
			if (team == null || string.IsNullOrWhiteSpace(team.team) || team.team.Length > 128
				|| team.ids == null || team.ids.Any(id => id == 0)) throw new InvalidDataException("Invalid saved item history.");
			itemCount += team.ids.Count;
		}
		if (itemCount > MaxWorldRecords) throw new InvalidDataException("Saved item history is too large.");
		foreach (var enemy in data.enemyDeaths)
			if (enemy == null || enemy.id == 0 || enemy.type == null || enemy.type.Length > 256)
				throw new InvalidDataException("Invalid saved enemy history.");
	}
}
