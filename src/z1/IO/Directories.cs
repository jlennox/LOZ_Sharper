using System.Reflection;

namespace z1.IO;

internal static class Directories
{
    private static readonly Lazy<string> _save = new(() =>
    {
        // TODO: Handle/report errors.
        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var path = Path.Combine(applicationData ?? Executable, "LOZ1");
        Directory.CreateDirectory(path);
        return path;
    });

    public static string Executable { get; } = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
    public static string Assets { get; } = Path.Combine(Save, "assets");
    public static string Save => _save.Value;
}