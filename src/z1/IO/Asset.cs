using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using ExtractLoz;
using SkiaSharp;

namespace z1.IO;

internal readonly struct Asset
{
    private class AssetMetadata
    {
        public const int AssetVersion = 1;
        public int Version { get; set; }

        public static bool IsValid(string basepath)
        {
            var file = Path.Combine(basepath, "info.json");
            if (!File.Exists(file)) return false;
            try
            {
                var json = File.ReadAllText(file);
                var metadata = JsonSerializer.Deserialize<AssetMetadata>(json);
                if (metadata == null) return false;
                return metadata.Version == AssetVersion;
            }
            catch (Exception e)
            {
                _log.Write($"Error parsing {file}: {e}");
                return false;
            }
        }

        public static void Write(string basepath)
        {
            var file = Path.Combine(basepath, "info.json");
            var metadata = new AssetMetadata { Version = AssetVersion };
            var json = JsonSerializer.SerializeToUtf8Bytes(metadata);
            File.WriteAllBytes(file, json);
        }
    }

    private static readonly DebugLog _log = new(nameof(Asset));

    private static readonly Dictionary<string, byte[]> _assets = new();

    public string Filename { get; }

    private readonly byte[] _assetData;

    public Asset(string filename)
    {
        Filename = filename;
        _assetData = _assets[filename];
    }

    public static void Initialize()
    {
        // If the assets are already present, no need to scan for a new romfile.
        if (TryLoadAssetsDirectory()) return;

        // Throws an exception instead of false.
        if (!TryFindRomFile(out var rom)) return;

        InitializeAssets(rom);
    }

    private static bool TryFindRomFile([MaybeNullWhen(false)] out string romfile)
    {
        var directories = new HashSet<string>
        {
            Directories.Executable,
            Environment.CurrentDirectory,
        };

        var errors = new StringBuilder();

        foreach (var directory in directories)
        {
            var roms = Directory.GetFiles(directory, "*.nes");

            if (roms.Length == 0)
            {
                _log.Write($"No roms in \"{directory}\"");
                continue;
            }

            foreach (var rom in roms)
            {
                var result = LozExtractor.CheckRomFile(rom);
                _log.Write($"{rom}: {result}");
                if (result == LozExtractor.RomCheckResult.Valid)
                {
                    romfile = rom;
                    return true;
                }

                errors.AppendLine($"{rom}: {result}");
            }
        }

        if (errors.Length == 0)
        {
            throw new FileNotFoundException($"Unable to find \"{LozExtractor.CorrectFilename}\" in \"{string.Join(", ", directories)}\"");
        }

        throw new Exception(errors.ToString().Trim());
    }

    private static void InitializeAssets(string romfile)
    {
        var assets = LozExtractor.Extract([romfile, "all"]);

        if (!Directory.Exists(Directories.Assets))
        {
            Directory.CreateDirectory(Directories.Assets);
        }

        foreach (var asset in assets)
        {
            _assets[asset.Key] = asset.Value;
            File.WriteAllBytes(Path.Combine(Directories.Assets, asset.Key), asset.Value);
        }

        // Write last because it doubles as a "finished" marker.
        AssetMetadata.Write(Directories.Assets);
        _log.Write($"Initialized using \"{romfile}\" to \"{Directories.Assets}\"");
    }

    private static bool TryLoadAssetsDirectory()
    {
        var dir = Directories.Assets;

        if (!Directory.Exists(dir))
        {
            _log.Write($"No assets directory at \"{dir}\"");
            return false;
        }

        if (!AssetMetadata.IsValid(dir))
        {
            _log.Write($"Invalid metadata.");
            return false;
        }

        foreach (var file in Directory.GetFiles(dir, "*"))
        {
            _assets[Path.GetFileName(file)] = File.ReadAllBytes(file);
        }

        _log.Write($"Successfully loaded {_assets.Count} assets.");
        return true;
    }

    public byte[] ReadAllBytes()
    {
        return _assetData;
    }

    public MemoryStream GetStream()
    {
        return new MemoryStream(_assetData);
    }

    public SKBitmap DecodeSKBitmap()
    {
        return SKBitmap.Decode(_assetData);
    }

    public SKBitmap DecodeSKBitmapTileData()
    {
        var bitmap = DecodeSKBitmap(SKAlphaType.Unpremul);
        Graphics.PreprocessPalette(bitmap);
        return bitmap;
    }

    public SKBitmap DecodeSKBitmap(SKAlphaType alphaType)
    {
        using var original = SKBitmap.Decode(_assetData);
        var bitmap = new SKBitmap(original.Width, original.Height, original.ColorType, alphaType);
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawBitmap(original, 0, 0);
        return bitmap;
    }
}