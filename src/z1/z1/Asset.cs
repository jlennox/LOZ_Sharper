using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using Silk.NET.Core;
using SkiaSharp;

namespace z1;

internal readonly struct Asset
{
    private static readonly Lazy<string> _baseAssetsDir = new(() => Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "assets", "out"));

    public readonly string FullPath;

    public Asset(string filename)
    {
        if (Path.IsPathRooted(filename)) throw new ArgumentOutOfRangeException(nameof(filename), filename, "filename must be relative path.");

        FullPath = Path.Combine(_baseAssetsDir.Value, filename);
    }

    public static string GetPath(string file)
    {
        return new Asset(file).FullPath;
    }

    public byte[] ReadAllBytes()
    {
        return File.ReadAllBytes(FullPath);
    }

    public SKBitmap DecodeSKBitmap()
    {
        return SKBitmap.Decode(FullPath);
    }

    public SKBitmap DecodeSKBitmapTileData()
    {
        var bitmap = DecodeSKBitmap(SKAlphaType.Unpremul);
        Graphics.PreprocessPalette(bitmap);
        return bitmap;
    }

    public SKBitmap DecodeSKBitmap(SKAlphaType alphaType)
    {
        using var original = SKBitmap.Decode(FullPath);
        var bitmap = new SKBitmap(original.Width, original.Height, original.ColorType, alphaType);
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawBitmap(original, 0, 0);
        return bitmap;
    }

    private static readonly Lazy<string[]> _resourceNames = new(() => Assembly.GetExecutingAssembly().GetManifestResourceNames());

    public static Stream GetEmbeddedResource(string name)
    {
        var resourceName = _resourceNames.Value.FirstOrDefault(t => t.EndsWith(name))
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
}
