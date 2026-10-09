using NUnit.Framework;
using WKMPMod.Core;

namespace WKMultiMod.Progress.Tests;

public class CheckpointPolicyTests {
	private static CheckpointProgress Mark(int level, double distance = 10, string route = "main", bool reached = true) =>
		new() { levelOrdinal = level, travelDistance = distance, route = route, reached = reached };

	[Test]
	public void LiveReconnectAlwaysKeepsOwnRunAndItems() => Assert.That(
		CheckpointPolicy.Decide(CheckpointSpawnReason.LiveReconnect, true, Mark(20), Mark(2)),
		Is.EqualTo(CheckpointSpawnDecision.KeepOwnRun));
	[Test]
	public void LateJoinUsesTheHostCheckpointWithOwnItems() => Assert.That(
		CheckpointPolicy.Decide(CheckpointSpawnReason.LateJoin, true, Mark(20), Mark(0, reached: false)),
		Is.EqualTo(CheckpointSpawnDecision.HostWithOwnItems));
	[Test]
	public void DeathAndLaterHostCheckpointMeansNoItems() => Assert.That(
		CheckpointPolicy.Decide(CheckpointSpawnReason.DeathRespawn, true, Mark(20), Mark(2)),
		Is.EqualTo(CheckpointSpawnDecision.HostEmpty));
	[Test]
	public void DeathNeverMovesBackwards() => Assert.That(
		CheckpointPolicy.Decide(CheckpointSpawnReason.DeathRespawn, true, Mark(2), Mark(20)),
		Is.EqualTo(CheckpointSpawnDecision.Normal));
	[Test]
	public void EqualCheckpointUsesNormalRespawn() => Assert.That(
		CheckpointPolicy.Decide(CheckpointSpawnReason.DeathRespawn, true, Mark(2), Mark(2)),
		Is.EqualTo(CheckpointSpawnDecision.Normal));
	[Test]
	public void LaterCheckpointWithinTheSameLevelCanAdvance() => Assert.That(
		CheckpointPolicy.Decide(CheckpointSpawnReason.DeathRespawn, true, Mark(2, 100), Mark(2, 10)),
		Is.EqualTo(CheckpointSpawnDecision.HostEmpty));
	[Test]
	public void TinyDistanceJitterDoesNotAdvance() => Assert.That(CheckpointPolicy.IsLater(Mark(2, 10.01), Mark(2, 10)), Is.False);
	[TestCase(CheckpointSpawnReason.LateJoin)]
	[TestCase(CheckpointSpawnReason.DeathRespawn)]
	public void UnrelatedBranchesCannotBeCompared(CheckpointSpawnReason reason) => Assert.That(
		CheckpointPolicy.Decide(reason, true, Mark(20, route: "main/side"), Mark(2)), Is.EqualTo(CheckpointSpawnDecision.Normal));
	[Test]
	public void DisabledRuleKeepsNormalSpawn() => Assert.That(
		CheckpointPolicy.Decide(CheckpointSpawnReason.LateJoin, false, Mark(20), Mark(0)), Is.EqualTo(CheckpointSpawnDecision.Normal));
	[Test]
	public void NoHostCheckpointKeepsNormalSpawn() => Assert.That(
		CheckpointPolicy.Decide(CheckpointSpawnReason.LateJoin, true, null, Mark(0)), Is.EqualTo(CheckpointSpawnDecision.Normal));
	[Test]
	public void HostCurrentPositionIsNotACheckpoint() => Assert.That(
		CheckpointPolicy.Decide(CheckpointSpawnReason.LateJoin, true, Mark(20, reached: false), Mark(0)), Is.EqualTo(CheckpointSpawnDecision.Normal));
	[Test]
	public void InvalidProgressIsRejected() {
		Assert.That(CheckpointPolicy.IsValid(Mark(-1)), Is.False);
		Assert.That(CheckpointPolicy.IsValid(Mark(1, double.NaN)), Is.False);
		Assert.That(CheckpointPolicy.IsValid(Mark(1, double.PositiveInfinity)), Is.False);
		Assert.That(CheckpointPolicy.IsValid(Mark(1, route: "")), Is.False);
	}
	[Test]
	public void DeathSurvivesBindingSameLobbyAfterRejoin() {
		var state = new CheckpointSessionState();
		state.Bind(42, 123, "mode"); state.SetCheckpoint(Mark(5)); state.RecordDeath(Mark(10));
		state.Bind(42, 123, "mode");
		Assert.That(state.PendingDeath, Is.True);
		Assert.That(state.DeathCheckpoint!.levelOrdinal, Is.EqualTo(5));
	}
	[Test]
	public void PostDeathStartingCheckpointsCannotReplaceFrozenProgress() {
		var state = new CheckpointSessionState();
		state.Bind(42, 123, "mode"); state.SetCheckpoint(Mark(5)); state.RecordDeath(Mark(10)); state.SetCheckpoint(Mark(0));
		Assert.That(state.DeathCheckpoint!.levelOrdinal, Is.EqualTo(5));
		Assert.That(state.LocalCheckpoint!.levelOrdinal, Is.EqualTo(5));
	}
	[Test]
	public void SuccessfulHostRespawnEstablishesNewOwnCheckpoint() {
		var state = new CheckpointSessionState();
		state.Bind(42, 123, "mode"); state.RecordDeath(Mark(0)); state.CompleteRespawn(Mark(20));
		Assert.That(state.PendingDeath, Is.False);
		Assert.That(state.LocalCheckpoint!.levelOrdinal, Is.EqualTo(20));
	}
	[TestCase(43UL, 123, "mode")]
	[TestCase(42UL, 456, "mode")]
	[TestCase(42UL, 123, "another mode")]
	public void AnotherRunCannotInheritDeathState(ulong lobby, int seed, string mode) {
		var state = new CheckpointSessionState();
		state.Bind(42, 123, "mode"); state.RecordDeath(Mark(0)); state.Bind(lobby, seed, mode);
		Assert.That(state.PendingDeath, Is.False);
		Assert.That(state.LocalCheckpoint, Is.Null);
	}
}
