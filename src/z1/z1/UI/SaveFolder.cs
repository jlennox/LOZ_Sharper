using System.Text.Json;

namespace z1.UI;

internal static class SaveFolder
{
    public const int MaxProfiles = 3;

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public static PlayerProfile[] Profiles => _profiles.Value;
    private static readonly Lazy<PlayerProfile[]> _profiles = new(LoadProfiles);

    public static GameConfiguration Configuration => _config.Value;
    private static readonly Lazy<GameConfiguration> _config = new(LoadGameConfiguration);

    private static readonly Lazy<string> _saveDirectory = new(() =>
    {
        // TODO: Handle/report errors.
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LOZ1");
        Directory.CreateDirectory(path);
        return path;
    });

    private static readonly Lazy<string> _logFile = new(() => Path.Combine(_saveDirectory.Value, Path.Combine("logs.txt")));
    private static readonly Lazy<string> _savesFile = new(() => Path.Combine(_saveDirectory.Value, Path.Combine("saves.json")));
    private static readonly Lazy<string> _configFile = new(() => Path.Combine(_saveDirectory.Value, Path.Combine("config.json")));

    private static readonly DebugLog _log = new(nameof(SaveFolder));

    static SaveFolder()
    {
        DebugLog.Initialize(_logFile.Value);
    }

    private static PlayerProfile[] LoadProfiles()
    {
        if (!File.Exists(_savesFile.Value)) return PlayerProfile.MakeDefaults();

        try
        {
            var json = File.ReadAllText(_savesFile.Value);
            return JsonSerializer.Deserialize<PlayerProfile[]>(json) ?? PlayerProfile.MakeDefaults();
        }
        catch (JsonException e)
        {
            _log.Write($"Error parsing profiles: {e}");
            return PlayerProfile.MakeDefaults();
        }
        catch (FileNotFoundException e)
        {
            _log.Write($"Error loading profiles: {e}");
            return PlayerProfile.MakeDefaults();
        }
        catch (Exception e)
        {
            _log.Write($"Error loading profiles: {e}");
            return PlayerProfile.MakeDefaults();
        }
    }

    public static bool SaveProfiles()
    {
        var json = JsonSerializer.Serialize(Profiles, _jsonOptions);
        // TODO: Report errors to UI somehow.
        try
        {
            File.WriteAllText(_savesFile.Value, json);
        }
        catch (Exception e)
        {
            _log.Write($"Error saving profiles: {e}");
            throw;
        }
        return true;
    }

    private static GameConfiguration LoadGameConfiguration()
    {
        try
        {
            var json = File.ReadAllText(_configFile.Value);
            return JsonSerializer.Deserialize<GameConfiguration>(json) ?? GameConfiguration.MakeDefaults();
        }
        catch (JsonException e)
        {
            _log.Write($"Error parsing configuration: {e}");
            return GameConfiguration.MakeDefaults();
        }
        catch (FileNotFoundException e)
        {
            _log.Write($"Error loading configuration: {e}");
            return GameConfiguration.MakeDefaults();
        }
        catch (Exception e)
        {
            _log.Write($"Error loading configuration: {e}");
            return GameConfiguration.MakeDefaults();
        }
    }
}