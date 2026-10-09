using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using WKMPMod.Core;

namespace WKMPMod.NetWork;

/// <summary>
/// Explicit public-field JSON for checkpoint DTOs. Unity JsonUtility can omit
/// nested plugin types (observed: progress), while ordinary Vector3/Quaternion
/// JSON contracts include recursive/computed properties. Neither is a wire contract.
/// </summary>
public static class CheckpointSnapshotJson {
	private sealed class PublicFields : DefaultContractResolver {
		protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization serialization) =>
			type.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(field => {
				var property = base.CreateProperty(field, MemberSerialization.Fields);
				property.Readable = true;
				property.Writable = !field.IsInitOnly;
				return property;
			}).ToList();
	}

	private static readonly JsonSerializerSettings Settings = new() {
		ContractResolver = new PublicFields(), TypeNameHandling = TypeNameHandling.None,
		MaxDepth = 32, FloatParseHandling = FloatParseHandling.Double
	};

	public static string Serialize<T>(T snapshot) {
		if (snapshot == null) throw new InvalidDataException("Missing checkpoint snapshot.");
		string json = JsonConvert.SerializeObject(snapshot, Settings);
		RequireProgress(Parse(json), null);
		return json;
	}

	public static T Deserialize<T>(string json) => Deserialize<T>(json, null);

	/// <summary>
	/// Only the locally owned/validated native-save metadata may repair a legacy
	/// 1.8.3.4 snapshot whose progress was dropped. Network replies never get this fallback.
	/// </summary>
	public static T DeserializeSaved<T>(string json, CheckpointProgress? trustedSavedProgress) =>
		Deserialize<T>(json, trustedSavedProgress);

	public static T Clone<T>(T snapshot) => Deserialize<T>(Serialize(snapshot));

	private static T Deserialize<T>(string json, CheckpointProgress? savedProgress) {
		var root = Parse(json);
		RequireProgress(root, savedProgress);
		try {
			var result = root.ToObject<T>(JsonSerializer.Create(Settings));
			if (result == null) throw new InvalidDataException("Missing checkpoint snapshot.");
			return result;
		} catch (JsonException ex) {
			throw new InvalidDataException("Invalid checkpoint snapshot fields.", ex);
		}
	}

	private static JObject Parse(string json) {
		if (string.IsNullOrWhiteSpace(json) || json.Length > CheckpointSnapshotCodec.MaxJsonBytes)
			throw new InvalidDataException("Invalid checkpoint JSON size.");
		try {
			using var input = new StringReader(json);
			using var reader = new JsonTextReader(input) { MaxDepth = 32, FloatParseHandling = FloatParseHandling.Double };
			return JObject.Load(reader);
		} catch (JsonException ex) { throw new InvalidDataException("Invalid checkpoint JSON.", ex); }
	}

	private static void RequireProgress(JObject root, CheckpointProgress? savedProgress) {
		if (root["progress"] is not JObject) {
			if (!CheckpointPolicy.IsValid(savedProgress))
				throw new InvalidDataException("Checkpoint snapshot is missing progress. Both players must update the mod; the world was not aligned.");
			root["progress"] = JObject.FromObject(savedProgress!, JsonSerializer.Create(Settings));
		}
		var progress = root["progress"]!;
		foreach (string field in new[] { "route", "levelOrdinal", "travelDistance", "reached" })
			if (progress[field] == null || progress[field]!.Type == JTokenType.Null)
				throw new InvalidDataException($"Checkpoint progress is missing '{field}'.");
	}
}
