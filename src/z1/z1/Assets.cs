using SkiaSharp;
using System.Reflection;

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
    public SKBitmap GetSKBitmap(params string[] path) => SKBitmap.Decode(GetPath(path));
}
