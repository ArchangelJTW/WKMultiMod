using NUnit.Framework;
using WKMPMod.NetWork;

namespace WKMultiMod.Progress.Tests;

public class CheckpointRequestTests {
	private readonly CheckpointRequestScope scope = new(7, 42, 99, 123, 5, "mode");
	[Test]
	public void CurrentAuthenticatedHostReplyIsAccepted() => Assert.That(scope.Accepts(99, 7, 42, 123, "mode", 42, 99, 5), Is.True);
	[TestCase(98UL, 7UL, 42UL, 123, "mode", 42UL, 99UL, 5)]
	[TestCase(99UL, 6UL, 42UL, 123, "mode", 42UL, 99UL, 5)]
	[TestCase(99UL, 7UL, 43UL, 123, "mode", 42UL, 99UL, 5)]
	[TestCase(99UL, 7UL, 42UL, 456, "mode", 42UL, 99UL, 5)]
	[TestCase(99UL, 7UL, 42UL, 123, "another mode", 42UL, 99UL, 5)]
	[TestCase(99UL, 7UL, 42UL, 123, "mode", 43UL, 99UL, 5)]
	[TestCase(99UL, 7UL, 42UL, 123, "mode", 42UL, 98UL, 5)]
	[TestCase(99UL, 7UL, 42UL, 123, "mode", 42UL, 99UL, 6)]
	public void SpoofedCancelledOrStaleReplyIsRejected(ulong sender, ulong attempt, ulong lobby, int seed,
		string mode, ulong currentLobby, ulong host, int revision) =>
		Assert.That(scope.Accepts(sender, attempt, lobby, seed, mode, currentLobby, host, revision), Is.False);
}
