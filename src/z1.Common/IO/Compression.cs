using System.IO.Compression;
using z1.Common.Data;

namespace z1.Common.IO;

public static class Compression
{
    private enum CompressionType
    {
        None,
        Zlib,
        GZip,
    }

    private readonly record struct CompressionResult(byte[] Data, CompressionType Type);

    private static byte[] Zlib(ReadOnlySpan<byte> inputData)
    {
        using var ms = new MemoryStream();
        using (var compressionStream = new DeflateStream(ms, CompressionLevel.Optimal))
        {
            compressionStream.Write(inputData);
        }

        return ms.ToArray();
    }

    private static byte[] GZip(ReadOnlySpan<byte> inputData)
    {
        using var ms = new MemoryStream();
        using (var compressionStream = new GZipStream(ms, CompressionLevel.Optimal))
        {
            compressionStream.Write(inputData);
        }

        return ms.ToArray();
    }

    // This is brutally slow but it's all preprocessed so it doesn't matter.
    private static CompressionResult GetBest(ReadOnlySpan<byte> inputData)
    {
        var zlib = Zlib(inputData);
        var gzip = GZip(inputData);

        return zlib.Length < gzip.Length
            ? new CompressionResult(zlib, CompressionType.Zlib)
            : new CompressionResult(gzip, CompressionType.GZip);
    }

    public static void Compress(IHasCompression input)
    {
        var best = GetBest(input.Data);
        input.Compression = best.Type.ToString().ToLowerInvariant();
        input.Data = best.Data;
    }

    public static void Decompress(IHasCompression input)
    {
        if (string.IsNullOrWhiteSpace(input.Compression)) return;
        if (Enum.TryParse<CompressionType>(input.Compression, true, out var compressionType))
        {
            input.Data = compressionType switch
            {
                CompressionType.Zlib => Zlib(input.Data),
                CompressionType.GZip => GZip(input.Data),
                _ => input.Data,
            };
            input.Compression = "";
        }
    }
}