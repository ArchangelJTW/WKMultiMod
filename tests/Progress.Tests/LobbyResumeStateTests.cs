using NUnit.Framework;
using WKMPMod.Core;

namespace WKMultiMod.Progress.Tests;

public class LobbyResumeStateTests {
	private LobbyResumeState state = null!;
	private const string Mode = "{\"gameModeName\":\"Campaign\",\"seed\":123}";

	[SetUp]
	public void SetUp() => state = new LobbyResumeState();

	[Test]
	public void FirstJoinCannotResume() {
		Assert.That(state.HasSession, Is.False);
		Assert.That(state.CanResume(42, Mode, 1), Is.False);
	}

	[Test]
	public void SameLobbyAndStillLoadedWorldCanResume() {
		state.Remember(42, Mode, 1);
		Assert.That(state.CanResume(42, Mode, 1), Is.True);
	}

	[Test]
	public void AnotherLobbyCannotReuseProgress() {
		state.Remember(42, Mode, 1);
		Assert.That(state.CanResume(43, Mode, 1), Is.False);
	}

	[Test]
	public void ChangedSeedCannotReuseProgress() {
		state.Remember(42, Mode, 1);
		Assert.That(state.CanResume(42, "{\"gameModeName\":\"Campaign\",\"seed\":456}", 1), Is.False);
	}

	[Test]
	public void ChangedRulesCannotReuseProgress() {
		state.Remember(42, Mode, 1);
		Assert.That(state.CanResume(42, Mode + ",different bindings", 1), Is.False);
	}

	[Test]
	public void SceneReloadOrDeathRestartInvalidatesResume() {
		state.Remember(42, Mode, 1);
		Assert.That(state.CanResume(42, Mode, 2), Is.False);
	}

	[Test]
	public void ExplicitClearInvalidatesResume() {
		state.Remember(42, Mode, 1);
		state.Clear();
		Assert.That(state.LobbyId, Is.Zero);
		Assert.That(state.HasSession, Is.False);
		Assert.That(state.CanResume(42, Mode, 1), Is.False);
	}

	[Test]
	public void LaterRunReplacesEarlierTicket() {
		state.Remember(42, Mode, 1);
		state.Remember(43, Mode, 2);
		Assert.That(state.CanResume(42, Mode, 1), Is.False);
		Assert.That(state.CanResume(43, Mode, 2), Is.True);
	}

	[Test]
	public void WorldRevisionZeroIsValidForAlreadyLoadedScene() {
		state.Remember(42, Mode, 0);
		Assert.That(state.CanResume(42, Mode, 0), Is.True);
	}

	[Test]
	public void InvalidLobbyIdCannotCreateTicket() {
		Assert.Throws<ArgumentOutOfRangeException>(() => state.Remember(0, Mode, 1));
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase(" ")]
	public void MissingGameModeCannotCreateTicket(string? mode) {
		Assert.Throws<ArgumentException>(() => state.Remember(42, mode!, 1));
	}
}
