using NUnit.Framework;
using WKMPMod.Core;
using WKMPMod.NetWork;

namespace WKMultiMod.Progress.Tests;

public class RunScopedCheckpointTests {
	private const string OldRun = "68e3e2099144408181d0a7b98309462a";
	private const string NewRun = "be18d5ca70ca4e41b7b5a5040dc6db78";
	private static CheckpointProgress Mark(int level = 50, string route = "branch-root") =>
		new() { route = route, levelOrdinal = level, travelDistance = 600, reached = true };

	[Test]
	public void SameLobbySeedAndModeButNewRunCannotInheritDeathOrEarlierCheckpoint() {
		var state = new CheckpointSessionState();
		state.Bind(42, 123, "mode", OldRun);
		state.SetCheckpoint(Mark());
		state.RecordDeath(Mark());
		state.Bind(42, 123, "mode", NewRun);
		Assert.That(state.RunId, Is.EqualTo(NewRun));
		Assert.That(state.PendingDeath, Is.False);
		Assert.That(state.LocalCheckpoint, Is.Null);
		Assert.That(state.DeathCheckpoint, Is.Null);
	}

	[Test]
	public void AliveLeaveJoinOfSameRunPreservesCheckpointAndPendingDeathCannotBeBypassed() {
		var state = new CheckpointSessionState();
		state.Bind(42, 123, "mode", OldRun);
		state.SetCheckpoint(Mark());
		state.RecordDeath(Mark());
		state.Bind(42, 123, "mode", OldRun);
		Assert.That(state.PendingDeath, Is.True);
		Assert.That(state.DeathCheckpoint!.levelOrdinal, Is.EqualTo(50));
	}

	[Test]
	public void NewRunClearsOldBranchProgressRatherThanMakingFreshJoinUseOldDeathPolicy() {
		var state = new CheckpointSessionState();
		state.Bind(42, 123, "mode", OldRun);
		state.SetCheckpoint(Mark(route: "branch-root/garbage"));
		state.RecordDeath(Mark());
		state.Bind(42, 123, "mode", NewRun);
		var reason = state.PendingDeath ? CheckpointSpawnReason.DeathRespawn : CheckpointSpawnReason.LateJoin;
		var choice = CheckpointPolicy.Evaluate(reason, true, Mark(9), Mark(0));
		Assert.That(choice.Decision, Is.EqualTo(CheckpointSpawnDecision.HostWithOwnItems));
		Assert.That(choice.Reason, Is.EqualTo(CheckpointDecisionReason.LateJoin));
	}

	[Test]
	public void NewRunInSameLobbyCannotWarmResumeTheOldLoadedWorld() {
		var state = new LobbyResumeState();
		state.Remember(42, "mode", 1, OldRun);
		Assert.That(state.CanResume(42, "mode", 1, OldRun), Is.True);
		Assert.That(state.CanResume(42, "mode", 1, NewRun), Is.False);
	}

	[Test]
	public void HostAdvertisedNewRunImmediatelyGatesOldWorldPoseAndSaveSync() {
		var gate = new WorldJoinAdmission();
		gate.Complete(CheckpointResolution.Applied, OldRun);
		Assert.That(gate.IsReadyFor(OldRun), Is.True);
		Assert.That(gate.IsReadyFor(NewRun), Is.False);
		Assert.That(gate.RunId, Is.EqualTo(OldRun), "Host advertising another run must not relabel the loaded world.");
		gate.Begin();
		Assert.That(gate.RunId, Is.Empty);
		Assert.That(gate.IsReadyFor(OldRun), Is.False);
	}

	[Test]
	public void SuccessfulNewRunAdmissionCanEnableOnlyTheNewWorld() {
		var gate = new WorldJoinAdmission();
		gate.Complete(CheckpointResolution.Applied, OldRun);
		gate.Begin();
		gate.Complete(CheckpointResolution.Applied, NewRun);
		Assert.That(gate.IsReadyFor(NewRun), Is.True);
		Assert.That(gate.IsReadyFor(OldRun), Is.False);
	}

	[Test]
	public void NewRunTicketCanReplaceOldRunWithoutChangingLobbyOrWorldRevision() {
		var state = new LobbyResumeState();
		state.Remember(42, "mode", 1, OldRun);
		state.Remember(42, "mode", 1, NewRun);
		Assert.That(state.CanResume(42, "mode", 1, OldRun), Is.False);
		Assert.That(state.CanResume(42, "mode", 1, NewRun), Is.True);
	}

	[Test]
	public void ClearDropsPersistentRunFromLiveResumeTicket() {
		var state = new LobbyResumeState();
		state.Remember(42, "mode", 1, OldRun);
		state.Clear();
		Assert.That(state.RunId, Is.Empty);
		Assert.That(state.HasSession, Is.False);
	}

	[TestCase(OldRun, NewRun)]
	[TestCase(NewRun, OldRun)]
	[TestCase(NewRun, NewRun)]
	public void ReplyFromDifferentRunOrChangedCurrentRunCannotBeAccepted(string replyRun, string currentRun) {
		var scope = new CheckpointRequestScope(7, 42, 99, 123, 5, "mode", OldRun);
		Assert.That(scope.Accepts(99, 7, 42, 123, "mode", 42, 99, 5, replyRun, currentRun), Is.False);
	}

	[Test]
	public void CurrentReplyMustMatchBothExpectedAndCurrentlyAdvertisedRun() {
		var scope = new CheckpointRequestScope(7, 42, 99, 123, 5, "mode", OldRun);
		Assert.That(scope.Accepts(99, 7, 42, 123, "mode", 42, 99, 5, OldRun, OldRun), Is.True);
	}

	[Test]
	public void NoCheckpointAndDifferentRouteNormalSpawnsHaveDistinctDiagnostics() {
		var absent = CheckpointPolicy.Evaluate(CheckpointSpawnReason.LateJoin, true, null, Mark(0));
		var branch = CheckpointPolicy.Evaluate(CheckpointSpawnReason.LateJoin, true, Mark(9), Mark(0, "other"));
		Assert.That(absent.Decision, Is.EqualTo(CheckpointSpawnDecision.Normal));
		Assert.That(absent.Reason, Is.EqualTo(CheckpointDecisionReason.NoHostCheckpoint));
		Assert.That(branch.Reason, Is.EqualTo(CheckpointDecisionReason.DifferentRoutes));
	}

	[Test]
	public void DeathAtLaterCheckpointDoesNotRewindAndHasObservableReason() {
		var choice = CheckpointPolicy.Evaluate(CheckpointSpawnReason.DeathRespawn, true, Mark(9), Mark(50));
		Assert.That(choice.Decision, Is.EqualTo(CheckpointSpawnDecision.Normal));
		Assert.That(choice.Reason, Is.EqualTo(CheckpointDecisionReason.HostNotLater));
	}

	[Test]
	public void InvalidHostProgressCannotMasqueradeAsSuccessfulNormalSpawn() {
		var invalid = Mark(-1);
		var choice = CheckpointPolicy.Evaluate(CheckpointSpawnReason.LateJoin, true, invalid, Mark(0));
		Assert.That(choice.Decision, Is.EqualTo(CheckpointSpawnDecision.Failed));
		Assert.That(choice.Reason, Is.EqualTo(CheckpointDecisionReason.InvalidHostProgress));
	}

	[Test]
	public void InvalidOwnProgressCannotMasqueradeAsSuccessfulNormalSpawn() {
		var choice = CheckpointPolicy.Evaluate(CheckpointSpawnReason.LateJoin, true, Mark(9), Mark(-1));
		Assert.That(choice.Decision, Is.EqualTo(CheckpointSpawnDecision.Failed));
		Assert.That(choice.Reason, Is.EqualTo(CheckpointDecisionReason.InvalidOwnProgress));
	}

	[Test]
	public void DisabledRuleIsDistinguishableFromMissingCheckpoint() {
		var choice = CheckpointPolicy.Evaluate(CheckpointSpawnReason.LateJoin, false, Mark(9), Mark(0));
		Assert.That(choice.Decision, Is.EqualTo(CheckpointSpawnDecision.Normal));
		Assert.That(choice.Reason, Is.EqualTo(CheckpointDecisionReason.Disabled));
	}
}

public class SavedItemHistoryNormalizationTests {
	private static List<SavedItemTombstones> History() => new() {
		new() { team = "default", ids = new() { 0, 20, 21, 20 } },
		new() { team = "", ids = new() { 0, 22 } },
		new() { team = " ", ids = new() { 22, 23 } },
		new() { team = "other", ids = new() { 0, 30 } }
	};

	[Test]
	public void UnassignedZeroIdsCannotInvalidateExportedSaveMetadata() {
		var result = SavedItemTombstones.Normalize(History(), "default");
		Assert.That(result.SelectMany(team => team.ids), Does.Not.Contain(0UL));
		Assert.That(result.Single(team => team.team == "default").ids, Is.EqualTo(new ulong[] { 20, 21, 22, 23 }));
		Assert.That(result.Single(team => team.team == "other").ids, Is.EqualTo(new ulong[] { 30 }));
	}

	[Test]
	public void UnknownTeamsMergeWithoutDroppingTheirValidDeletionHistory() {
		var result = SavedItemTombstones.Normalize(History(), "default");
		Assert.That(result.Count, Is.EqualTo(2));
		Assert.That(result.Select(team => team.team), Does.Not.Contain(""));
		Assert.That(result.Single(team => team.team == "default").ids, Does.Contain(23UL));
	}

	[Test]
	public void NormalizeDoesNotMutateRuntimeHistory() {
		var source = History();
		var result = SavedItemTombstones.Normalize(source, "default");
		result[0].ids.Clear();
		Assert.That(source[0].ids, Is.EqualTo(new ulong[] { 0, 20, 21, 20 }));
		Assert.That(source[1].team, Is.Empty);
	}

	[Test]
	public void CleanHistoryRoundTripsThroughStillStrictProductionCodec() {
		const string mode = "{\"gameModeName\":\"GM_Campaign\",\"seed\":123}";
		var metadata = new RunSaveData {
			runId = "68e3e2099144408181d0a7b98309462a", playerId = 100, seed = 123,
			modeJson = mode, modeSignature = RunSaveCodec.Signature(mode), wasHost = true,
			itemTombstones = SavedItemTombstones.Normalize(History(), "default")
		};
		var result = RunSaveCodec.Decode(RunSaveCodec.Encode(metadata));
		Assert.That(result.itemTombstones.SelectMany(team => team.ids), Is.EquivalentTo(new ulong[] { 20, 21, 22, 23, 30 }));
	}

	[Test]
	public void StrictDecoderStillRejectsZeroIdsInsteadOfAcceptingCorruptWireData() {
		const string mode = "{\"gameModeName\":\"GM_Campaign\",\"seed\":123}";
		var metadata = new RunSaveData {
			runId = "68e3e2099144408181d0a7b98309462a", playerId = 100, seed = 123,
			modeJson = mode, modeSignature = RunSaveCodec.Signature(mode), itemTombstones = History()
		};
		Assert.Throws<InvalidDataException>(() => RunSaveCodec.Encode(metadata));
	}
}
