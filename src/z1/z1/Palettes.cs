using SkiaSharp;

namespace z1;

internal enum Palette
{
    Red = 6, Blue = 5, Player = 4, SeaPal = 7,
    BlueForeground = 9999,
    LevelForeground = 9999,
    WhiteBgPalette = 0,
    RedBgPalette = 1,
    BlueFgPalette = 5,
    RedFgPalette = 6,
    LevelFgPalette = 7,

    Mask = 0x7,

    FlashAttr = 0x80,
}

internal static class Palettes
{
    public const int PaletteCount = 8;
    public const int PaletteLength = 4;
    public const int ForegroundPalCount = 4;
    public const int BackgroundPalCount = 4;

    public static SKColor[][] Colors = GetPalettes();

    public static SKColor[] GetPalette(Palette palette) => Colors[(int)palette];

    public static SKColor[][] GetPalettes()
    {
        return ParseImage(Assets.Root.GetSKBitmap("palette.png"));
    }

    private static SKColor[][] ParseImage(SKBitmap bitmap, int blockSize = 12)
    {
        var palettes = new SKColor[16][];
        for (var i = 0; i < 16; ++i)
        {
            palettes[i] = new SKColor[4];
            for (var j = 0; j < 4; ++j)
            {
                var x = i * blockSize;
                var y = j * blockSize;
                var color = bitmap.GetPixel(x, y);
                palettes[i][j] = color;
            }
        }

        return palettes;
    }
}
