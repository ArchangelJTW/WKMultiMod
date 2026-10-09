using Newtonsoft.Json.Linq;
using NUnit.Framework;
using WKMPMod.Core;
using WKMPMod.NetWork;

namespace WKMultiMod.Progress.Tests;

public class CheckpointSnapshotJsonTests {
	// Public fields mirror Unity's Vector3/Quaternion contract. If normal JSON
	// property reflection runs, these computed/self-referencing getters explode.
	public struct Vector {
		public float x, y, z;
		public Vector normalized => throw new InvalidOperationException("Computed Unity property must not be serialized.");
		public float magnitude => throw new InvalidOperationException("Computed Unity property must not be serialized.");
	}

	public struct Rotation {
		public float x, y, z, w;
		public Rotation normalized => throw new InvalidOperationException("Computed Unity property must not be serialized.");
	}

	public class Level {
		public int id;
		public string level = "";
		public string region = "";
		public string subRegion = "";
	}

	public class Snapshot {
		public int version = 1;
		public bool nativeContinuation;
		public ulong lobbyId;
		public int seed;
		public string modeSignature = "";
		public CheckpointProgress progress = new();
		public string levelName = "";
		public string regionName = "";
		public string subregionName = "";
		public int levelSeed;
		public List<Level> generation = new();
		public List<string> activeStates = new();
		public Vector levelPosition, relativePosition;
		public Rotation levelRotation, playerRotation, cameraRotation;
	}

	private static Snapshot Interlude() => new() {
		lobbyId = 42, seed = 2724293, modeSignature = new string('A', 64),
		progress = new() { route = "branch-root", levelOrdinal = 0, travelDistance = 600, reached = true },
		levelName = "Campaign_Interlude_Silo_To_Pipeworks_01",
		regionName = "Region_Pipeworks_Campaign", subregionName = "Subregion_Silos_Ventilation", levelSeed = 2734283,
		generation = new() {
			new() { id = 0, level = "Campaign_Interlude_Silo_To_Pipeworks_01", region = "Region_Pipeworks_Campaign", subRegion = "Subregion_Silos_Ventilation" },
			new() { id = 1, level = "Next_Room", region = "Region_Pipeworks_Campaign", subRegion = "Subregion_Silos_Ventilation" }
		},
		activeStates = new() { "onLoad" }, relativePosition = new() { x = 3.5520067f, y = 53.917236f, z = 30.161041f },
		levelRotation = new() { w = 1 }, playerRotation = new() { y = -1 }, cameraRotation = new() { w = 1 }
	};

	[Test]
	public void PublicFieldSerializerDoesNotDropNestedCheckpointProgress() {
		var json = JObject.Parse(CheckpointSnapshotJson.Serialize(Interlude()));
		Assert.That(json["progress"]!["route"]!.Value<string>(), Is.EqualTo("branch-root"));
		Assert.That(json["progress"]!["travelDistance"]!.Value<double>(), Is.EqualTo(600));
		Assert.That(json["progress"]!["reached"]!.Value<bool>(), Is.True);
	}

	[Test]
	public void FullCheckpointNetworkCompressionRoundTripRetainsIdentityAndPose() {
		var wire = CheckpointSnapshotCodec.Encode(CheckpointSnapshotJson.Serialize(Interlude()));
		var result = CheckpointSnapshotJson.Deserialize<Snapshot>(CheckpointSnapshotCodec.Decode(wire));
		Assert.That(result.lobbyId, Is.EqualTo(42));
		Assert.That(result.seed, Is.EqualTo(2724293));
		Assert.That(result.progress.route, Is.EqualTo("branch-root"));
		Assert.That(result.progress.reached, Is.True);
		Assert.That(result.generation[0].level, Is.EqualTo(result.levelName));
		Assert.That(result.generation[0].id, Is.EqualTo(result.progress.levelOrdinal));
		Assert.That(result.relativePosition.y, Is.EqualTo(53.917236f));
		Assert.That(result.playerRotation.y, Is.EqualTo(-1));
		Assert.That(result.levelRotation.w, Is.EqualTo(1));
	}

	[Test]
	public void CloneUsedByNativeContinueDoesNotEraseProgressOrAliasCollections() {
		var original = Interlude();
		var clone = CheckpointSnapshotJson.Clone(original);
		Assert.That(clone.progress.reached, Is.True);
		clone.progress.route = "changed";
		clone.generation[0].id = 99;
		Assert.That(original.progress.route, Is.EqualTo("branch-root"));
		Assert.That(original.generation[0].id, Is.Zero);
	}

	[Test]
	public void UnityComputedPropertiesAreNeverSerialized() {
		string json = CheckpointSnapshotJson.Serialize(Interlude());
		Assert.That(json, Does.Not.Contain("normalized"));
		Assert.That(json, Does.Not.Contain("magnitude"));
	}

	[Test]
	public void LargeSteamLobbyIdIsNotRoundedThroughFloatingPointJson() {
		var snapshot = Interlude();
		snapshot.lobbyId = ulong.MaxValue - 1;
		Assert.That(CheckpointSnapshotJson.Clone(snapshot).lobbyId, Is.EqualTo(snapshot.lobbyId));
	}

	[Test]
	public void Captured134FailureMissingProgressIsRejectedWithSpecificDiagnostic() {
		// Same structural defect as the decoded in-game packet, without personal IDs.
		var json = JObject.Parse(CheckpointSnapshotJson.Serialize(Interlude()));
		json.Remove("progress");
		var ex = Assert.Throws<InvalidDataException>(() => CheckpointSnapshotJson.Deserialize<Snapshot>(json.ToString()));
		Assert.That(ex!.Message, Does.Contain("missing progress"));
	}

	[Test]
	public void LegacyOwnedSavedGeometryCanRecoverProgressFromValidatedLocalMetadata() {
		var source = Interlude();
		var json = JObject.Parse(CheckpointSnapshotJson.Serialize(source));
		json.Remove("progress");
		var result = CheckpointSnapshotJson.DeserializeSaved<Snapshot>(json.ToString(), source.progress);
		Assert.That(result.progress.route, Is.EqualTo(source.progress.route));
		Assert.That(result.progress.travelDistance, Is.EqualTo(600));
		Assert.That(result.progress.reached, Is.True);
	}

	[Test]
	public void LegacyRepairNeverInventsAProgressMark() {
		var json = JObject.Parse(CheckpointSnapshotJson.Serialize(Interlude()));
		json.Remove("progress");
		Assert.Throws<InvalidDataException>(() => CheckpointSnapshotJson.DeserializeSaved<Snapshot>(json.ToString(), null));
		Assert.Throws<InvalidDataException>(() => CheckpointSnapshotJson.DeserializeSaved<Snapshot>(json.ToString(), new()));
	}

	[TestCase("route")]
	[TestCase("levelOrdinal")]
	[TestCase("travelDistance")]
	[TestCase("reached")]
	public void IncompleteProgressIsNotSilentlyFilledWithDefaults(string field) {
		var json = JObject.Parse(CheckpointSnapshotJson.Serialize(Interlude()));
		((JObject)json["progress"]!).Remove(field);
		Assert.Throws<InvalidDataException>(() => CheckpointSnapshotJson.Deserialize<Snapshot>(json.ToString()));
	}

	[Test]
	public void NativeContinuationFlagSurvivesRoundTripWithoutBecomingAReachedCheckpoint() {
		var snapshot = Interlude();
		snapshot.nativeContinuation = true;
		snapshot.progress.reached = false;
		var result = CheckpointSnapshotJson.Clone(snapshot);
		Assert.That(result.nativeContinuation, Is.True);
		Assert.That(result.progress.reached, Is.False);
	}
}

public class WorldJoinAdmissionTests {
	[Test]
	public void SteamMembershipAndFreshMapAloneDoNotAdmitPlayer() =>
		Assert.That(new WorldJoinAdmission().IsReady, Is.False);

	[TestCase(CheckpointResolution.NotAttempted)]
	[TestCase(CheckpointResolution.Failed)]
	public void TimeoutBadJsonOrFailedPlacementCannotEnableSync(CheckpointResolution result) {
		var gate = new WorldJoinAdmission();
		Assert.That(gate.Complete(result), Is.False);
		Assert.That(gate.IsReady, Is.False);
	}

	[TestCase(CheckpointResolution.Normal)]
	[TestCase(CheckpointResolution.Applied)]
	public void ExplicitNormalResponseOrCompletedAlignmentCanEnableSync(CheckpointResolution result) {
		var gate = new WorldJoinAdmission();
		Assert.That(gate.Complete(result), Is.True);
		Assert.That(gate.IsReady, Is.True);
	}

	[Test]
	public void RejoinResetsPriorAdmission() {
		var gate = new WorldJoinAdmission();
		gate.Complete(CheckpointResolution.Applied);
		gate.Begin();
		Assert.That(gate.IsReady, Is.False);
	}

	[Test]
	public void FailedNewJoinCannotReuseOldSuccessfulAdmission() {
		var gate = new WorldJoinAdmission();
		gate.Complete(CheckpointResolution.Applied);
		gate.Begin();
		gate.Complete(CheckpointResolution.Failed);
		Assert.That(gate.IsReady, Is.False);
	}
}
