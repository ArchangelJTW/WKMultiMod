using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace WKMPMod.NetWork;

/// <summary>Bound both the wire payload and decompressed UTF-8 data.</summary>
public static class CheckpointSnapshotCodec {
	public const int MaxCompressedBytes = 128 * 1024;
	public const int MaxJsonBytes = 512 * 1024;
	private static readonly UTF8Encoding Utf8 = new(false, true);

	public static byte[] Encode(string json) {
		if (string.IsNullOrWhiteSpace(json) || Utf8.GetByteCount(json) > MaxJsonBytes)
			throw new InvalidDataException("Checkpoint JSON exceeds the size limit or is empty.");
		using var output = new MemoryStream();
		using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true)) {
			var bytes = Utf8.GetBytes(json);
			gzip.Write(bytes, 0, bytes.Length);
		}
		if (output.Length > MaxCompressedBytes) throw new InvalidDataException("Checkpoint packet exceeds the size limit.");
		return output.ToArray();
	}

	public static string Decode(ReadOnlyMemory<byte> payload) {
		if (payload.Length == 0 || payload.Length > MaxCompressedBytes) throw new InvalidDataException("Invalid checkpoint packet size.");
		using var input = new MemoryStream(payload.ToArray(), false);
		using var gzip = new GZipStream(input, CompressionMode.Decompress);
		using var output = new MemoryStream();
		var buffer = new byte[4096];
		int count;
		while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0) {
			if (output.Length + count > MaxJsonBytes) throw new InvalidDataException("Checkpoint decompression limit exceeded.");
			output.Write(buffer, 0, count);
		}
		if (output.Length == 0) throw new InvalidDataException("Empty checkpoint snapshot.");
		return Utf8.GetString(output.GetBuffer(), 0, (int)output.Length);
	}
}
