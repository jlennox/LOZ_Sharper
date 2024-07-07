using SkiaSharp;

namespace z1;

internal class Sprites
{
    public static readonly SKColor TransparentMask = new(0x74, 0x74, 0x74);

    private static readonly SKColor _border = new(0, 0x80, 0);

    public static readonly SKBitmap BadguysOverworld = Assets.Root.GetSKBitmap("NES - The Legend of Zelda - Overworld Enemies.png");
    public static readonly SKBitmap Link = Assets.Root.GetSKBitmap("NES - The Legend of Zelda - Link.png");

    public static SKBitmap FromSheet(SKBitmap sheet, int x, int y)
    {
        for (; x > 0; x--) if (sheet.GetPixel(x, y) == _border) break;
        for (; y > 0; y--) if (sheet.GetPixel(x + 8, y) == _border) break;
        if (x == 0 || y == 0) throw new Exception();
        return sheet.Extract(x + 1, y + 1, 16, 16);
    }
}
