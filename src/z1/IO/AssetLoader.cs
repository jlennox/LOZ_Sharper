using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using ExtractLoz;

namespace z1.IO;

internal readonly struct AssetLoader
{
    private static readonly DebugLog _log = new(nameof(AssetLoader));

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

    public static Dictionary<string, byte[]> Initialize()
    {
        // If the assets are already present, no need to scan for a new romfile.
        if (TryLoadAssetsDirectory(out var assets)) return assets;

        // Throws an exception instead of false.
        if (!TryFindRomFile(out var rom)) throw new Exception("Unresearchable code");

        return InitializeAssets(rom);
    }

    private static bool TryFindRomFile([MaybeNullWhen(false)] out string romfile)
    {
        var directories = new HashSet<string>
        {
            Directories.Executable,
            Environment.CurrentDirectory,
            Directories.Assets, // We copy the ROM here. This refinds it when the asset version changes.
        };

        var errors = new StringBuilder();

        foreach (var directory in directories)
        {
            var roms = Directory.GetFiles(directory, "*.nes");

            if (roms.Length == 0)
            {
                _log.Write($"No roms in \"{directory}\"");
                errors.AppendLine($"No roms in \"{directory}\"");
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

    private static Dictionary<string, byte[]> InitializeAssets(string romfile)
    {
        var assets = LozExtractor.Extract([romfile, "all"]);

        if (!Directory.Exists(Directories.Assets))
        {
            Directory.CreateDirectory(Directories.Assets);
        }

        foreach (var asset in assets)
        {
            File.WriteAllBytes(Path.Combine(Directories.Assets, asset.Key), asset.Value);
        }

        // Write last because it doubles as a "finished" marker.
        AssetMetadata.Write(Directories.Assets);
        // Copy the ROM incase the asset version changes.
        File.Copy(romfile, Path.Combine(Directories.Assets, "rom.nes"), true);
        _log.Write($"Initialized using \"{romfile}\" to \"{Directories.Assets}\"");
        return assets;
    }

    private static bool TryLoadAssetsDirectory([MaybeNullWhen(false)] out Dictionary<string, byte[]> assets)
    {
        var dir = Directories.Assets;
        assets = null;

        if (!Directory.Exists(dir))
        {
            _log.Write($"No assets directory at \"{dir}\"");
            return false;
        }

        if (!AssetMetadata.IsValid(dir))
        {
            _log.Write("Invalid metadata.");
            return false;
        }

        assets = new Dictionary<string, byte[]>();

        foreach (var file in Directory.GetFiles(dir, "*"))
        {
            assets[Path.GetFileName(file)] = File.ReadAllBytes(file);
        }

        _log.Write($"Successfully loaded {assets.Count} assets.");
        return true;
    }
}