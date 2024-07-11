using SkiaSharp;
using static z1.Sprites;

namespace z1;

internal static class Sprites
{
    public static readonly SKColor TransparentMask = new(0x74, 0x74, 0x74);
    public static readonly SKBitmap BadguysOverworld = Assets.Root.GetSKBitmap("NES - The Legend of Zelda - Overworld Enemies.png");
    public static readonly SKBitmap Link = Assets.Root.GetSKBitmap("NES - The Legend of Zelda - Link.png");
    public static readonly SKBitmap Overworld = Assets.Root.GetSKBitmap("NES - The Legend of Zelda - Overworld Tileset.png");

    private static readonly SKColor _border = new(0, 0x80, 0);

    public static SKBitmap FromSheet(SKBitmap sheet, int x, int y, int width = 16, int height = 16)
    {
        for (; x > 0; x--) if (sheet.GetPixel(x, y) == _border) break;
        for (; y > 0; y--) if (sheet.GetPixel(x + 8, y) == _border) break;
        return sheet.Extract(x + 1, y + 1, width, height);
    }
}

internal static class OverworldTiles
{
    private static SKBitmap FromIndex(int y, int index) => FromSheet(Overworld, index * 17 + 8, y);

    public static readonly SKBitmap Empty = FromIndex(158, 0);
    public static readonly SKBitmap RockBR = FromIndex(158, 1);
    public static readonly SKBitmap RockB = FromIndex(158, 2);
    public static readonly SKBitmap RockBL = FromIndex(158, 3);
    public static readonly SKBitmap Sand = FromIndex(158 + 17, 0);
    public static readonly SKBitmap RockTR = FromIndex(158 + 17, 1);
    public static readonly SKBitmap RockSolid = FromIndex(158 + 17, 2);
    public static readonly SKBitmap RockTL = FromIndex(158 + 17, 3);
    public static readonly SKBitmap Stone = FromIndex(158 + 34, 0);
    public static readonly SKBitmap Brush = FromIndex(158 + 34, 1);
    public static readonly SKBitmap Armos = FromIndex(158 + 34, 2);
    public static readonly SKBitmap Grave = FromIndex(158 + 34, 3);
}