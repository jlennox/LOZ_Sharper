using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using z1.UI;

namespace z1.IO;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GameConfiguration))]
[JsonSerializable(typeof(PlayerProfiles))]
internal partial class JsonTypeInfos : JsonSerializerContext;

internal interface IInitializable
{
    void Initialize();
}

internal static class SaveFolder
{
    public const int MaxProfiles = 3;

    public static PlayerProfiles Profiles => _profiles.Value;
    private static readonly Lazy<PlayerProfiles> _profiles = new(LoadProfiles);

    public static GameConfiguration Configuration => _config.Value;
    private static readonly Lazy<GameConfiguration> _config = new(LoadGameConfiguration);

    private static readonly Lazy<string> _profileFile = new(() => Path.Combine(Directories.Save, Path.Combine("saves.json")));
    private static readonly Lazy<string> _configFile = new(() => Path.Combine(Directories.Save, Path.Combine("config.json")));

    private static readonly DebugLog _log = new(nameof(SaveFolder));

    private static JsonTypeInfo<PlayerProfiles> ProfilesTypeInfo => JsonTypeInfos.Default.PlayerProfiles;
    private static PlayerProfiles LoadProfiles() => LoadOrDefault(_profileFile, PlayerProfiles.MakeDefault, ProfilesTypeInfo);
    public static bool SaveProfiles() => Save(_profileFile, Profiles, ProfilesTypeInfo);

    // JOE: TODO: The two save methods is confused and needs to be fixed. I'm not sure how preserve instances are with string.text.json yet,
    // but that needs to be determined and this needs to be made clearer. Either preserve the instances here or keep them on world, don't
    // do both, and I think I prefer the latter anyways.
    private static JsonTypeInfo<GameConfiguration> GameConfigTypeInfo => JsonTypeInfos.Default.GameConfiguration;
    private static GameConfiguration LoadGameConfiguration() => LoadOrDefault(_configFile, GameConfiguration.MakeDefaults, GameConfigTypeInfo);
    public static bool SaveConfiguration() => Save(_configFile, Configuration, GameConfigTypeInfo);
    public static bool SaveConfiguration(GameConfiguration configuration) => Save(_configFile, configuration, GameConfigTypeInfo);

    private static T LoadOrDefault<T>(Lazy<string> file, Func<T> makeDefaults, JsonTypeInfo<T> typeinfo)
    {
        if (!File.Exists(file.Value)) return makeDefaults();

        try
        {
            var json = File.ReadAllText(file.Value);
            var result = JsonSerializer.Deserialize<T>(json, typeinfo) ?? makeDefaults();
            if (result is IInitializable init) init.Initialize();
            return result;
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

    private static bool Save<T>(Lazy<string> file, T obj, JsonTypeInfo<T> typeinfo)
    {
        var json = JsonSerializer.Serialize(obj, typeinfo);

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