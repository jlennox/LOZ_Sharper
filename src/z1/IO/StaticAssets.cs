using z1.Common.IO;

namespace z1.IO;

internal static class StaticAssets
{
    public static string GuiFont { get; } = Path.Combine(Directories.StaticAssets, Filenames.GuiFont);

    static StaticAssets()
    {
        // TODO: This is inconsistent with Assets and does not error handle at all.
        if (!Directory.Exists(Directories.StaticAssets)) Directory.CreateDirectory(Directories.StaticAssets);
    }
}