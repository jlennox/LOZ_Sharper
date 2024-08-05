using System.Reflection;

namespace z1.IO;

internal static class Directories
{
    public static string Executable => _executable.Value;
    public static string Assets => _assets.Value;
    public static string Save => _save.Value;

    private static readonly Lazy<string> _executable = new(() =>
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

    private static readonly Lazy<string> _assets = new(() => Path.Combine(Save, "assets"));

    private static readonly Lazy<string> _save = new(() =>
    {
        // TODO: Handle/report errors.
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LOZ1");
        Directory.CreateDirectory(path);
        return path;
    });
}