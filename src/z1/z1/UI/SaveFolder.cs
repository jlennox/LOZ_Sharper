using System.Diagnostics;
using System.Text.Json;

namespace z1.UI;

internal static class SaveFolder
{
    public const int MaxProfiles = 3;

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public static PlayerProfile[] Profiles => _profiles.Value;
    private static readonly Lazy<PlayerProfile[]> _profiles = new(LoadProfiles);

    private static readonly Lazy<string> _saveDirectory = new(() =>
    {
        // TODO: Handle/report errors.
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LOZ1");
        Directory.CreateDirectory(path);
        return path;
    });

    private static readonly Lazy<string> _savesFile = new(() => Path.Combine(_saveDirectory.Value, Path.Combine("saves.json")));

    private static PlayerProfile[] MakeDefaults() => Enumerable.Range(0, MaxProfiles).Select(_ => new PlayerProfile()).ToArray();

    private static PlayerProfile[] LoadProfiles()
    {
        if (!File.Exists(_savesFile.Value)) return MakeDefaults();

        try
        {
            var json = File.ReadAllText(_savesFile.Value);
            return JsonSerializer.Deserialize<PlayerProfile[]>(json) ?? MakeDefaults();
        }
        catch (JsonException e)
        {
            Debug.WriteLine($"Error parsing profiles: {e}");
            return MakeDefaults();
        }
        catch (FileNotFoundException e)
        {
            Debug.WriteLine($"Error loading profiles: {e}");
            return MakeDefaults();
        }
    }

    public static PlayerProfile ReadProfile(int index) => Profiles[index];

    public static bool Save()
    {
        var json = JsonSerializer.Serialize(Profiles, _jsonOptions);
        // TODO: Handle/report errors.
        File.WriteAllText(_savesFile.Value, json);
        return true;
    }
}