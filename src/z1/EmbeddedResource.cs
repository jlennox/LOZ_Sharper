using System.Collections.Immutable;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using Silk.NET.Core;
using SkiaSharp;

namespace z1;

internal sealed class EmbeddedResource
{
    private static readonly ImmutableArray<string> _resourceNames = [..Assembly.GetExecutingAssembly().GetManifestResourceNames()];

    public static Stream GetEmbeddedResource(string name)
    {
        var resourceName = _resourceNames.FirstOrDefault(t => t.EndsWith(name))
            ?? throw new FileNotFoundException($"Resource not found: {name}");

        return Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Resource not found: {name}"); ;
    }

    public static unsafe RawImage RawImageIconFromResource(string name)
    {
        using var stream = GetEmbeddedResource(name);
        using var icon = new Icon(stream);
        using var bitmap = icon.ToBitmap();

        var bitmapData = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        var span = new Span<byte>((byte*)bitmapData.Scan0.ToPointer(), bitmapData.Stride * bitmap.Height);
        var rawImage = new RawImage(bitmap.Width, bitmap.Height, new Memory<byte>(span.ToArray()));

        bitmap.UnlockBits(bitmapData);
        return rawImage;
    }

    public static SKBitmap SKBitmapFromResource(string name)
    {
        using var stream = GetEmbeddedResource(name);
        return SKBitmap.Decode(stream);
    }
}