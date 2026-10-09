using Newtonsoft.Json.Linq;
using NUnit.Framework;
using WKMPMod.Core;
using WKMPMod.NetWork;

namespace WKMultiMod.Progress.Tests;

public class RunSaveTests {
	private const string Run = "68e3e2099144408181d0a7b98309462a";
	private const ulong Player = 100;
	private const string Mode = "{\"gameModeName\":\"GM_Campaign\",\"seed\":123}";

	private static RunSaveData Saved() => new() {
		runId = Run, playerId = Player, seed = 123, modeJson = Mode,
		modeSignature = RunSaveCodec.Signature(Mode), wasHost = true,
		checkpoint = new() { route = "main", levelOrdinal = 4, travelDistance = 250, reached = true },
		worldAnchor = new() {
			route = "main", levelOrdinal = 5, levelName = "Room_B",
			position = new[] { 1f, 200f, 3f }, rotation = new[] { 0f, 0f, 0f, 1f }
		},
		itemTombstones = new() { new() { team = "default", ids = new() { 20, 21 } } },
		enemyDeaths = new() { new() { id = 22, type = "crushed" } }
	};

	[Test]
	public void SameRunCanResumeUnderANewSteamLobbyId() =>
		Assert.That(SavedRunPolicy.CanResume(Saved(), Run, Player, 123, RunSaveCodec.Signature(Mode)), Is.True);

	[Test]
	public void SameSeedAloneCannotResumeAnUnrelatedRun() =>
		Assert.That(SavedRunPolicy.CanResume(Saved(), "be18d5ca70ca4e41b7b5a5040dc6db78", Player, 123, RunSaveCodec.Signature(Mode)), Is.False);

	[Test]
	public void AnotherPlayersCopiedSaveCannotAutoResumeInventory() =>
		Assert.That(SavedRunPolicy.CanResume(Saved(), Run, Player + 1, 123, RunSaveCodec.Signature(Mode)), Is.False);

	[Test]
	public void ChangedSeedCannotResume() =>
		Assert.That(SavedRunPolicy.CanResume(Saved(), Run, Player, 456, RunSaveCodec.Signature(Mode)), Is.False);

	[Test]
	public void ChangedModeDifficultyOrBindingsCannotResume() =>
		Assert.That(SavedRunPolicy.CanResume(Saved(), Run, Player, 123, RunSaveCodec.Signature(Mode + "changed")), Is.False);

	[Test]
	public void HostMayContinueTheirOwnNativeSave() =>
		Assert.That(SavedRunPolicy.CanHost(Saved(), Player, 123, RunSaveCodec.Signature(Mode)), Is.True);

	[Test]
	public void GuestMayRehostTheirOwnSaveWithoutBorrowingSomebodyElsesInventory() {
		var saved = Saved();
		saved.wasHost = false;
		Assert.That(SavedRunPolicy.CanHost(saved, Player, 123, saved.modeSignature), Is.True);
		Assert.That(SavedRunPolicy.CanHost(saved, Player + 1, 123, saved.modeSignature), Is.False);
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("not-a-run")]
	[TestCase("../outside-save")]
	[TestCase("00000000000000000000000000000000")]
	[TestCase("68e3e209-9144-4081-81d0-a7b98309462a")]
	public void InvalidRunIdNeverMatches(string? run) =>
		Assert.That(SavedRunPolicy.CanResume(Saved(), run, Player, 123, RunSaveCodec.Signature(Mode)), Is.False);

	[Test]
	public void LegacySaveWithoutModMetadataIsNotGuessedToBeTheSameRun() =>
		Assert.That(SavedRunPolicy.CanResume(null, Run, Player, 123, RunSaveCodec.Signature(Mode)), Is.False);

	[Test]
	public void MetadataRoundTripPreservesCheckpointAndLogicalWorldHistory() {
		var saved = Saved();
		var restored = RunSaveCodec.Decode(RunSaveCodec.Encode(saved));
		Assert.That(restored.runId, Is.EqualTo(Run));
		Assert.That(restored.playerId, Is.EqualTo(Player));
		Assert.That(restored.checkpoint!.levelOrdinal, Is.EqualTo(4));
		Assert.That(restored.worldAnchor!.levelOrdinal, Is.EqualTo(5));
		Assert.That(restored.itemTombstones[0].ids, Is.EqualTo(new ulong[] { 20, 21 }));
		Assert.That(restored.enemyDeaths[0].type, Is.EqualTo("crushed"));
	}

	[Test]
	public void MetadataHasNoInventoryOrSavedLifePayload() {
		var json = JObject.Parse(RunSaveCodec.Encode(Saved()));
		Assert.That(json.Property("inventory"), Is.Null);
		Assert.That(json.Property("playerSave"), Is.Null);
		Assert.That(json.Property("lives"), Is.Null);
		Assert.That(json.Property("playerData"), Is.Null);
	}

	[Test]
	public void PendingDeathSurvivesSerializationAndBindingToANewLobby() {
		var saved = Saved();
		saved.pendingDeath = true;
		saved.deathCheckpoint = saved.checkpoint!.Copy();
		var restored = RunSaveCodec.Decode(RunSaveCodec.Encode(saved));
		var session = new CheckpointSessionState();
		session.Bind(999, restored.seed, restored.modeSignature);
		session.RestoreProgress(restored.checkpoint, restored.deathCheckpoint, restored.pendingDeath);
		Assert.That(session.PendingDeath, Is.True);
		Assert.That(session.DeathCheckpoint!.levelOrdinal, Is.EqualTo(4));
	}

	[Test]
	public void RestoredDeathIsNotTreatedAsAnAliveReconnect() {
		var session = new CheckpointSessionState();
		session.Bind(999, 123, RunSaveCodec.Signature(Mode));
		var own = Saved().checkpoint!;
		session.RestoreProgress(own, own, true);
		var host = own.Copy();
		host.levelOrdinal++;
		Assert.That(CheckpointPolicy.Decide(CheckpointSpawnReason.DeathRespawn, true, host, session.DeathCheckpoint), Is.EqualTo(CheckpointSpawnDecision.HostEmpty));
	}

	[Test]
	public void RestoredProgressDoesNotAliasDeserializedMutableObjects() {
		var session = new CheckpointSessionState();
		session.Bind(999, 123, RunSaveCodec.Signature(Mode));
		var own = Saved().checkpoint!;
		session.RestoreProgress(own, own, true);
		own.levelOrdinal = 99;
		Assert.That(session.LocalCheckpoint!.levelOrdinal, Is.EqualTo(4));
		Assert.That(session.DeathCheckpoint!.levelOrdinal, Is.EqualTo(4));
	}

	[Test]
	public void LegacyNativeContinuationMayPlaceALateJoinWithoutBeingACheckpoint() {
		var point = Saved().checkpoint!;
		point.reached = false;
		Assert.That(CheckpointPolicy.Decide(CheckpointSpawnReason.LateJoin, true, point, point, nativeContinuation: true),
			Is.EqualTo(CheckpointSpawnDecision.HostWithOwnItems));
		Assert.That(CheckpointPolicy.Decide(CheckpointSpawnReason.LateJoin, true, point, point),
			Is.EqualTo(CheckpointSpawnDecision.Normal));
	}

	[Test]
	public void NativeContinuationNeverAdvancesADeathSpawn() {
		var own = Saved().checkpoint!;
		var point = own.Copy();
		point.levelOrdinal++;
		point.reached = false;
		Assert.That(CheckpointPolicy.Decide(CheckpointSpawnReason.DeathRespawn, true, point, own, nativeContinuation: true),
			Is.EqualTo(CheckpointSpawnDecision.Normal));
	}

	[Test]
	public void NativeContinuationCannotCrossRoutesOrOverrideLiveReconnect() {
		var own = Saved().checkpoint!;
		var point = own.Copy();
		point.route = "another-branch";
		point.reached = false;
		Assert.That(CheckpointPolicy.Decide(CheckpointSpawnReason.LateJoin, true, point, own, nativeContinuation: true),
			Is.EqualTo(CheckpointSpawnDecision.Normal));
		Assert.That(CheckpointPolicy.Decide(CheckpointSpawnReason.LiveReconnect, true, point, own, nativeContinuation: true),
			Is.EqualTo(CheckpointSpawnDecision.KeepOwnRun));
	}

	[Test]
	public void CompletedSavedDeathDropsTheTombstone() {
		var session = new CheckpointSessionState();
		session.Bind(999, 123, RunSaveCodec.Signature(Mode));
		session.RestoreProgress(Saved().checkpoint, Saved().checkpoint, true);
		session.CompleteRespawn();
		Assert.That(session.PendingDeath, Is.False);
		Assert.That(session.DeathCheckpoint, Is.Null);
	}

	[Test]
	public void FutureSchemaIsRejected() {
		var saved = Saved();
		saved.version = 2;
		Assert.Throws<InvalidDataException>(() => RunSaveCodec.Encode(saved));
	}

	[Test]
	public void MismatchedModeFingerprintIsRejected() {
		var saved = Saved();
		saved.modeJson += "changed";
		Assert.Throws<InvalidDataException>(() => RunSaveCodec.Encode(saved));
	}

	[Test]
	public void DeathWithoutAValidCheckpointIsRejected() {
		var saved = Saved();
		saved.pendingDeath = true;
		Assert.Throws<InvalidDataException>(() => RunSaveCodec.Encode(saved));
	}

	[Test]
	public void InvalidCheckpointOrAnchorIsRejected() {
		var saved = Saved();
		saved.checkpoint!.travelDistance = double.NaN;
		Assert.Throws<InvalidDataException>(() => RunSaveCodec.Encode(saved));
		saved = Saved();
		saved.worldAnchor!.rotation[3] = 0;
		Assert.Throws<InvalidDataException>(() => RunSaveCodec.Encode(saved));
	}

	[Test]
	public void InvalidWorldHistoryIsRejected() {
		var saved = Saved();
		saved.itemTombstones[0].ids.Add(0);
		Assert.Throws<InvalidDataException>(() => RunSaveCodec.Encode(saved));
	}

	[Test]
	public void OversizedWorldHistoryIsRejected() {
		var saved = Saved();
		saved.itemTombstones[0].ids = Enumerable.Range(1, RunSaveCodec.MaxWorldRecords + 1).Select(x => (ulong)x).ToList();
		Assert.Throws<InvalidDataException>(() => RunSaveCodec.Encode(saved));
	}

	[Test]
	public void OversizedInputIsRejectedBeforeDeserialization() =>
		Assert.Throws<InvalidDataException>(() => RunSaveCodec.Decode(new string('x', RunSaveCodec.MaxChars + 1)));

	[TestCase("")]
	[TestCase("{broken")]
	[TestCase("null")]
	public void InvalidMetadataFailsClosed(string json) =>
		Assert.Throws<InvalidDataException>(() => RunSaveCodec.Decode(json));
}
