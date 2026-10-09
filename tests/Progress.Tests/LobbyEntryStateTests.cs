using NUnit.Framework;
using WKMPMod.NetWork;

namespace WKMultiMod.Progress.Tests;

public class LobbyEntryStateTests {
	[Test]
	public void AsyncResultBeforeCallbackStillInitializesExactlyOnce() {
		var state = new LobbyEntryState();
		Assert.That(state.TryNotify(42, 42), Is.True); // async result assigned current lobby
		Assert.That(state.TryNotify(42, 42), Is.False); // later Steam callback
	}

	[Test]
	public void CallbackBeforeAsyncResultStillInitializesExactlyOnce() {
		var state = new LobbyEntryState();
		Assert.That(state.TryNotify(42, 42), Is.True); // Steam callback
		Assert.That(state.TryNotify(42, 42), Is.False); // async success fallback
	}

	[Test]
	public void CreateResultDoesNotDependOnReceivingAnotherSteamCallback() {
		var state = new LobbyEntryState();
		Assert.That(state.TryNotify(42, 42), Is.True);
	}

	[Test]
	public void RejoiningSameLobbyRequiresNewInitialization() {
		var state = new LobbyEntryState();
		Assert.That(state.TryNotify(42, 42), Is.True);
		state.Clear();
		Assert.That(state.TryNotify(42, 42), Is.True);
	}

	[Test]
	public void OldCallbackCannotConsumeNewLobbyNotification() {
		var state = new LobbyEntryState();
		Assert.That(state.TryNotify(41, 42), Is.False);
		Assert.That(state.TryNotify(42, 42), Is.True);
	}

	[TestCase(0ul, 0ul)]
	[TestCase(42ul, 0ul)]
	[TestCase(0ul, 42ul)]
	public void InvalidOrCancelledEntryIsIgnored(ulong lobby, ulong requested) =>
		Assert.That(new LobbyEntryState().TryNotify(lobby, requested), Is.False);
}
