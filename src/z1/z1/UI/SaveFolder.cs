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
    private static readonly Lazy<string> _profileFile = new(() => Path.Combine(_saveDirectory.Value, Path.Combine("saves.json")));
    private static readonly Lazy<string> _configFile = new(() => Path.Combine(_saveDirectory.Value, Path.Combine("config.json")));

    private static readonly DebugLog _log = new(nameof(SaveFolder));

    static SaveFolder()
    {
        DebugLog.Initialize(_logFile.Value);
    }

    private static PlayerProfile[] LoadProfiles() => LoadOrDefault(_profileFile, PlayerProfile.MakeDefaults);
    public static bool SaveProfiles() => Save(_profileFile, Profiles);

    // JOE: TODO: The two save methods is confused and needs to be fixed. I'm not sure how preserve instances are with string.text.json yet,
    // but that needs to be determined and this needs to be made clearer. Either preserve the instances here or keep them on world, don't
    // do both, and I think I prefer the latter anyways.
    private static GameConfiguration LoadGameConfiguration() => LoadOrDefault(_configFile, GameConfiguration.MakeDefaults);
    public static bool SaveConfiguration() => Save(_configFile, Configuration);
    public static bool SaveConfiguration(GameConfiguration configuration) => Save(_configFile, configuration);

    private static T LoadOrDefault<T>(Lazy<string> file, Func<T> makeDefaults)
    {
        if (!File.Exists(file.Value)) return makeDefaults();

        try
        {
            var json = File.ReadAllText(file.Value);
            return JsonSerializer.Deserialize<T>(json) ?? makeDefaults();
        }
        catch (JsonException e)
        {
            _log.Write($"Error parsing {typeof(T).Name}: {e}");
            return makeDefaults();
        }
        catch (FileNotFoundException e)
        {
            _log.Write($"Error loading {typeof(T).Name}: {e}");
            return makeDefaults();
        }
        catch (Exception e)
        {
            _log.Write($"Error loading {typeof(T).Name}: {e}");
            return makeDefaults();
        }
    }

    private static bool Save<T>(Lazy<string> file, T obj)
    {
        var json = JsonSerializer.Serialize(obj, _jsonOptions);
        // TODO: Report errors to UI somehow.
        try
        {
            File.WriteAllText(file.Value, json);
        }
        catch (Exception e)
        {
            _log.Write($"Error saving {typeof(T).Name}: {e}");
            throw;
        }
        return true;
    }
}