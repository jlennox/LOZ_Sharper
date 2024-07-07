using SkiaSharp;

namespace z1;

internal readonly struct MonsterSprites
{
    public readonly Palette PaletteA;
    public readonly Palette PaletteB;
    public readonly SKBitmap[] Left;
    public readonly SKBitmap[] Right;
    public readonly SKBitmap[] Up;
    public readonly SKBitmap[] Down;

    private MonsterSprites(Palette paletteA, Palette paletteB)
    {
        PaletteA = paletteA;
        PaletteB = paletteB;
    }

    public MonsterSprites(Palette paletteA, Palette paletteB, SKBitmap[] left, SKBitmap[] down) : this(paletteA, paletteB)
    {
        Left = left;
        Right = left.Mirror();
        Up = down.Flip();
        Down = down;
    }

    public MonsterSprites(Palette paletteA, Palette paletteB, SKBitmap[] left, SKBitmap[] down, SKBitmap[] up) : this(paletteA, paletteB)
    {
        Left = left;
        Right = left.Mirror();
        Up = up;
        Down = down;
    }

    public MonsterSprites(Palette paletteA, Palette paletteB, SKBitmap[] left, SKBitmap[] right, SKBitmap[] down, SKBitmap[] up) : this(paletteA, paletteB)
    {
        Left = left;
        Right = right;
        Up = up;
        Down = down;
    }

    // public SKBitmap AsPaletteB(SKBitmap bitmap) => bitmap.ChangePalette(PaletteA, PaletteB);
}
