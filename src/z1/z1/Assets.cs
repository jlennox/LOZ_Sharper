using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using Silk.NET.Core;

namespace z1;

internal sealed class Assets
{
    // https://github.com/joshbirnholz/cardconjurer
    private static readonly Lazy<string> _baseAssetsDir = new(() => Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "assets"));

    public static readonly Assets Root = new();

    private readonly string _base;

    public Assets(params string[] dirs)
    {
        _base = dirs.Length > 0
            ? Path.Combine(_baseAssetsDir.Value, Path.Combine(dirs))
            : _baseAssetsDir.Value;
    }

    public string GetPath(params string[] path) => Path.Combine(_base, Path.Combine(path));

    private static readonly Lazy<string[]> _resourceNames = new(() => Assembly.GetExecutingAssembly().GetManifestResourceNames());

    public static Stream GetEmbeddedResource(string name)
    {
        var resourceName = _resourceNames.Value.FirstOrDefault(t => t.EndsWith(name))
            ?? throw new FileNotFoundException($"Resource not found: {name}");

        return Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Resource not found: {name}"); ;
    }

    public static byte[] ReadAllBytes(string name)
    {
        return File.ReadAllBytes(Root.GetPath("out", name));
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
