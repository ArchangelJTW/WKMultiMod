using NUnit.Framework;
using System.IO.Compression;
using System.Text;
using WKMPMod.NetWork;

namespace WKMultiMod.Progress.Tests;

public class CheckpointCodecTests {
	[Test]
	public void GeometryJsonRoundTripsIncludingUnicode() {
		const string json = "{\"levelName\":\"Checkpoint 检查点\",\"seed\":123}";
		Assert.That(CheckpointSnapshotCodec.Decode(CheckpointSnapshotCodec.Encode(json)), Is.EqualTo(json));
	}
	[Test]
	public void OversizedEncodingIsRejected() => Assert.Throws<InvalidDataException>(() =>
		CheckpointSnapshotCodec.Encode(new string('x', CheckpointSnapshotCodec.MaxJsonBytes + 1)));
	[Test]
	public void EmptyEncodingIsRejected() => Assert.Throws<InvalidDataException>(() => CheckpointSnapshotCodec.Encode(""));
	[Test]
	public void OversizedWirePayloadIsRejected() => Assert.Throws<InvalidDataException>(() =>
		CheckpointSnapshotCodec.Decode(new byte[CheckpointSnapshotCodec.MaxCompressedBytes + 1]));
	[Test]
	public void CompressedBombIsBounded() {
		using var output = new MemoryStream();
		using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true)) {
			var bytes = Encoding.UTF8.GetBytes(new string('x', CheckpointSnapshotCodec.MaxJsonBytes + 1));
			gzip.Write(bytes);
		}
		Assert.Throws<InvalidDataException>(() => CheckpointSnapshotCodec.Decode(output.ToArray()));
	}
	[Test]
	public void MalformedGzipIsRejected() => Assert.Throws<InvalidDataException>(() =>
		CheckpointSnapshotCodec.Decode(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }));
	[Test]
	public void EmptyWirePayloadIsRejected() => Assert.Throws<InvalidDataException>(() => CheckpointSnapshotCodec.Decode(Array.Empty<byte>()));
}
