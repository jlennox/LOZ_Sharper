using SkiaSharp;

namespace z1;

internal enum Palette
{
    Red = 6, Blue = 2
}

internal static class Palettes
{
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
