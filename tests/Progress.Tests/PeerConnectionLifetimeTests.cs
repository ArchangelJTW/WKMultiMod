using NUnit.Framework;
using WKMPMod.NetWork;

namespace WKMultiMod.Progress.Tests;

public class PeerConnectionLifetimeTests {
	[Test]
	public void ActiveHandleCloseIsAccepted() =>
		Assert.That(PeerConnectionLifetime.IsCurrentClose(2, null, 2), Is.True);

	[Test]
	public void OldActiveHandleCannotDisconnectReplacement() =>
		Assert.That(PeerConnectionLifetime.IsCurrentClose(2, null, 1), Is.False);

	[Test]
	public void FailedPendingHandleCloseIsAccepted() =>
		Assert.That(PeerConnectionLifetime.IsCurrentClose(null, 2, 2), Is.True);

	[Test]
	public void OldPendingHandleCannotCancelNewAttempt() =>
		Assert.That(PeerConnectionLifetime.IsCurrentClose(null, 2, 1), Is.False);

	[Test]
	public void PendingCloseCannotRemoveEstablishedIncomingConnection() =>
		Assert.That(PeerConnectionLifetime.IsCurrentClose(3, 2, 2), Is.False);

	[Test]
	public void EstablishedConnectionTakesPrecedenceOverPendingHandle() =>
		Assert.That(PeerConnectionLifetime.IsCurrentClose(3, 2, 3), Is.True);

	[Test]
	public void UnknownCloseCannotStopAnUntrackedRetryCoroutine() =>
		Assert.That(PeerConnectionLifetime.IsCurrentClose(null, null, 2), Is.False);

	[Test]
	public void DuplicateCloseIsIgnoredAfterCleanup() {
		Assert.That(PeerConnectionLifetime.IsCurrentClose(2, null, 2), Is.True);
		Assert.That(PeerConnectionLifetime.IsCurrentClose(null, null, 2), Is.False);
	}

	[Test]
	public void ExplicitLobbyLeaveCanAlwaysCleanUp() =>
		Assert.That(PeerConnectionLifetime.IsCurrentClose(3, 2, null), Is.True);
}
