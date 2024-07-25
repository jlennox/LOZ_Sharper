using System.Diagnostics;
using System.Text.Json;

namespace z1.UI;

internal static class SaveFolder
{
    public const int MaxProfiles = 3;

    public static PlayerProfile[] Profiles => _profiles.Value;

    private static readonly Lazy<string> _saveDirectory = new(() =>
    {
        // TODO: Handle/report errors.
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LOZ1");
        Directory.CreateDirectory(path);
        return path;
    });

    private static readonly Lazy<string> _savesFile = new(() => Path.Combine(_saveDirectory.Value, Path.Combine("saves.json")));

    private static PlayerProfile[] MakeDefault() => Enumerable.Range(0, MaxProfiles).Select(_ => new PlayerProfile()).ToArray();

    private static readonly Lazy<PlayerProfile[]> _profiles = new(LoadProfiles);

    private static PlayerProfile[] LoadProfiles()
    {
        if (!File.Exists(_savesFile.Value)) return MakeDefault();

        try
        {
            var json = File.ReadAllText(_savesFile.Value);
            return JsonSerializer.Deserialize<PlayerProfile[]>(json) ?? MakeDefault();
        }
        catch (JsonException e)
        {
            Debug.WriteLine($"Error parsing profiles: {e}");
            return MakeDefault();
        }
        catch (FileNotFoundException e)
        {
            Debug.WriteLine($"Error loading profiles: {e}");
            return MakeDefault();
        }
    }

    public static PlayerProfile ReadProfile(int index) => LoadProfiles()[index];

    public static bool Save()
    {
        var json = JsonSerializer.Serialize(Profiles, new JsonSerializerOptions { WriteIndented = true });
        // TODO: Handle/report errors.
        File.WriteAllText(_savesFile.Value, json);
        return true;
    }
}