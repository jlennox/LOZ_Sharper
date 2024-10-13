using System.Reflection;

namespace z1.Common.IO;

public static class Filenames
{
    public const string GuiFont = "UbuntuMono-Regular.ttf";
    public const string WindowIcon = "icon.ico";

    public const string Font = "font.png";
    public const string FontAddendum = "font-addendum.png";

    private static readonly string _executableDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? throw new Exception());

    public static string FormatLevelWorld(int quest, int level) => $"Level{quest:D2}_{level:D2}.world";
    public static string FormatLevelAnyQuest(int level) => $"Level{{QuestId}}_{level:D2}.world";

    public static string GetRecordingFilename()
    {
        var filename = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".recording";
        return Path.Combine(_executableDirectory, filename);
    }

    public static IEnumerable<string> GetRecordingFiles()
    {
        return Directory.EnumerateFiles(_executableDirectory, "*.recording");
    }

    public static IEnumerable<string> GetNewestRecordingFiles(int limit)
    {
        return GetRecordingFiles()
            .OrderByDescending(File.GetCreationTime)
            .Take(limit);
    }
}
