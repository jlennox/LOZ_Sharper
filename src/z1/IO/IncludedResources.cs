namespace z1.IO;

internal class IncludedResources
{
    public static string? GetFont()
    {
        var path = Path.Combine(Directories.Executable, "UbuntuMono-Regular.ttf");
        return File.Exists(path) ? path : null;
    }
}
