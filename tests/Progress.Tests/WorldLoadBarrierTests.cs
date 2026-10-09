using NUnit.Framework;
using WKMPMod.Core;

namespace WKMultiMod.Progress.Tests;

public class WorldLoadBarrierTests {
	[Test]
	public void InGameJoinCannotUseReadyWorldFromBeforeQueuedLoadScene() {
		var barrier = new WorldLoadBarrier(5, requiresSceneLoad: true);
		Assert.That(barrier.IsReady(5, worldReady: true), Is.False,
			"The 1.8.3.6 retry sent a request from old level 50, then sceneLoaded cancelled it.");
	}

	[Test]
	public void NewSceneAloneCannotAdmitAnUnfinishedNativeGeneration() {
		var barrier = new WorldLoadBarrier(5, requiresSceneLoad: true);
		Assert.That(barrier.IsReady(6, worldReady: false), Is.False);
	}

	[Test]
	public void RequestCanStartOnlyAfterSceneCallbackAndNativeWorldReady() {
		var barrier = new WorldLoadBarrier(5, requiresSceneLoad: true);
		Assert.That(barrier.IsReady(5, true), Is.False);
		Assert.That(barrier.IsReady(6, false), Is.False);
		Assert.That(barrier.IsReady(6, true), Is.True);
	}

	[Test]
	public void SameNamedSceneReloadStillNeedsANewerRevision() {
		var barrier = new WorldLoadBarrier(0, requiresSceneLoad: true);
		Assert.That(barrier.IsReady(0, true), Is.False);
		Assert.That(barrier.IsReady(1, true), Is.True);
	}

	[Test]
	public void LiveReconnectDoesNotRequireOrScheduleAnotherSceneLoad() {
		var barrier = new WorldLoadBarrier(5, requiresSceneLoad: false);
		Assert.That(barrier.IsReady(5, true), Is.True);
		Assert.That(barrier.IsReady(5, false), Is.False);
	}

	[Test]
	public void AlreadyLoadedOwnedNativeSaveMayProceedWithoutLosingLocalInventoryToAnotherReload() {
		var barrier = new WorldLoadBarrier(5, requiresSceneLoad: false);
		Assert.That(barrier.IsReady(5, true), Is.True);
	}

	[Test]
	public void DeathRestartCannotSendCheckpointRequestBeforeItsQueuedSceneLoadCompletes() {
		var barrier = new WorldLoadBarrier(12, requiresSceneLoad: true);
		Assert.That(barrier.IsReady(12, true), Is.False);
		Assert.That(barrier.IsReady(13, false), Is.False);
		Assert.That(barrier.IsReady(13, true), Is.True);
	}
}

public class CheckpointGenerationIdentityTests {
	[Test]
	public void SavedTargetZeroMustNotBeAllocatedFromTheOldNativeCounterFifty() {
		int nativeCounter = 50;
		int hostOrdinal = 0;
		nativeCounter = CheckpointGenerationIdentity.CounterStart(hostOrdinal);
		int actualTarget = nativeCounter++; // Native GenerateLevels' exact idCounter++ allocation.
		Assert.That(actualTarget, Is.Zero);
		Assert.That(CheckpointGenerationIdentity.MatchesTarget("M3_Habitation_Shaft_Intro", hostOrdinal,
			"M3_Habitation_Shaft_Intro", actualTarget), Is.True);
	}

	[Test]
	public void InterludeNineRemainsNineAcrossCheckpointWorldRegeneration() {
		int nativeCounter = CheckpointGenerationIdentity.CounterStart(9);
		int target = nativeCounter++;
		int nextRoom = nativeCounter++;
		Assert.That(target, Is.EqualTo(9));
		Assert.That(nextRoom, Is.EqualTo(10));
	}

	[Test]
	public void StaleNumericIdentityIsNotAcceptedJustBecauseRoomNameMatches() =>
		Assert.That(CheckpointGenerationIdentity.MatchesTarget("Intro", 0, "Intro", 50), Is.False);

	[Test]
	public void MatchingCounterCannotMakeAnotherRoomPassGeometryValidation() =>
		Assert.That(CheckpointGenerationIdentity.MatchesTarget("Interlude", 9, "Habitation", 9), Is.False);

	[Test]
	public void MissingRegeneratedLevelCannotEnablePoseSync() =>
		Assert.That(CheckpointGenerationIdentity.MatchesTarget("Interlude", 9, null, 9), Is.False);

	[TestCase(-1)]
	[TestCase(int.MaxValue)]
	[TestCase(int.MaxValue - 4095)]
	public void InvalidOrOverflowingCountersFailBeforeRegeneration(int ordinal) =>
		Assert.Throws<ArgumentOutOfRangeException>(() => CheckpointGenerationIdentity.CounterStart(ordinal));

	[Test]
	public void RepeatedRegenerationDoesNotAccumulatePreviousClientLevelIds() {
		int nativeCounter = 50;
		for (int attempt = 0; attempt < 3; attempt++) {
			nativeCounter = CheckpointGenerationIdentity.CounterStart(9);
			Assert.That(nativeCounter++, Is.EqualTo(9));
			nativeCounter += 40;
		}
	}
}
