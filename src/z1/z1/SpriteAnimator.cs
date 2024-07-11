using SkiaSharp;

namespace z1;

internal readonly record struct SpriteFrame(byte X, byte Y, byte Flags);
internal readonly record struct SpriteAnimation(int Length, int Width, int Height, SpriteFrame[] Frames);

internal sealed class SpriteAnimator
{
    public readonly Game Game;
    public readonly Palette PaletteA;
    public readonly Palette PaletteB;
    public readonly SKBitmap[] Left;
    public readonly SKBitmap[] Right;
    public readonly SKBitmap[] Up;
    public readonly SKBitmap[] Down;

    public SpriteAnimation? Animation { get; set; }
    public int Time { get; set; }
    public int DurationFrames { get; set; }

    public SpriteAnimator(SpriteAnimation? animation = null)
    {
        Animation = animation;
    }

    // public void SetAnimation(Direction direction)
    // {
    //     Animation = direction switch {
    //         Direction.Left => new(Left.Length, Left[0].Width, Left[0].Height, Left),
    //         Direction.Right => new(Right.Length, Right[0].Width, Right[0].Height, Right),
    //         Direction.Up => new(Up.Length, Up[0].Width, Up[0].Height, Up),
    //         Direction.Down => new(Down.Length, Down[0].Width, Down[0].Height, Down),
    //         _ => throw new InvalidOperationException("Invalid direction")
    //     };
    // }

    public void SetDuration(int frames)
    {
        if (Time >= frames)
        {
            Time = 0;
        }

        DurationFrames = frames;
    }

    public void Advance()
    {
        Time = (Time + 1) % DurationFrames;
    }

    public void AdvanceFrame()
    {
        if (Animation != null && Animation.Value.Length > 0 && DurationFrames > 0)
        {
            int frameDuration = DurationFrames / Animation.Value.Length;
            Time = (Time + frameDuration) % DurationFrames;
        }
    }

    public void Draw(TileSheet sheetSlot, float x, float y, Palette palette, int flags = 0)
    {
        Draw((int)sheetSlot, (int)x, (int)y, (int)palette, flags);
    }

    public void Draw(TileSheet sheetSlot, int x, int y, Palette palette, int flags = 0)
    {
        if (Animation != null && Animation.Value.Length > 0 && DurationFrames > 0)
        {
            int index = (Animation.Value.Length * Time) / DurationFrames;

            DrawFrameInternal(sheetSlot, x, y, palette, index, flags);
        }
    }

    public void Draw(int sheetSlot, int x, int y, int palette, int flags)
    {
        Draw((TileSheet)sheetSlot, x, y, (Palette)palette, flags);
    }

    public void DrawFrame(TileSheet sheetSlot, int x, int y, Palette palette, int frame, int flags = 0)
    {
        if (Animation != null && Animation.Value.Length > frame)
        {
            DrawFrameInternal(sheetSlot, x, y, palette, frame, flags);
        }
    }

    public void DrawFrame(int sheetSlot, int x, int y, int palette, int frame, int flags = 0)
    {
        DrawFrame((TileSheet)sheetSlot, x, y, (Palette)palette, frame, flags);
    }

    // public SKBitmap? GetFrame()
    // {
    //     if (Animation != null && Animation.Value.Length > 0 && DurationFrames > 0)
    //     {
    //         var index = (Animation.Value.Length * Time) / DurationFrames;
    //         return Animation.Value.Frames[index];
    //     }
    //
    //     return null;
    // }

    public void DrawFrameInternal(TileSheet sheetSlot, int x, int y, Palette palette, int frame, int flags)
    {
        int index = frame;
        var anim = Animation.Value;
        Graphics.DrawSpriteTile(
            sheetSlot,
            anim.Frames[index].X,
            anim.Frames[index].Y,
            anim.Width,
            anim.Height,
            x,
            y,
            palette,
            anim.Frames[index].Flags | flags
        );
    }

    // public void DrawSpriteImage(TileSheet sheetSlot, int x, int y, Palette palette, int flags)
    // {
    //     Game.DrawBitmap(Left[0], x, y);
    //     var anim = Animation.Value;
    //     Graphics.DrawSpriteTile(
    //         sheetSlot,
    //         anim.Frames[0].X,
    //         anim.Frames[0].Y,
    //         anim.Width,
    //         anim.Height,
    //         x,
    //         y,
    //         palette,
    //         anim.Frames[0].Flags | flags
    //         );
    // }


    // private SpriteAnimator(Game game, Palette paletteA, Palette paletteB)
    // {
    //     PaletteA = paletteA;
    //     PaletteB = paletteB;
    // }
    //
    // public SpriteAnimator(Game game, Palette paletteA, Palette paletteB, SKBitmap[] left, SKBitmap[] down) : this(game, paletteA, paletteB)
    // {
    //     Left = left;
    //     Right = left.Mirror();
    //     Up = down.Flip();
    //     Down = down;
    // }
    //
    // public SpriteAnimator(Game game, Palette paletteA, Palette paletteB, SKBitmap[] left, SKBitmap[] down, SKBitmap[] up) : this(game, paletteA, paletteB)
    // {
    //     Left = left;
    //     Right = left.Mirror();
    //     Up = up;
    //     Down = down;
    // }
    //
    // public SpriteAnimator(Game game, Palette paletteA, Palette paletteB, SKBitmap[] left, SKBitmap[] right, SKBitmap[] down, SKBitmap[] up) : this(game, paletteA, paletteB)
    // {
    //     Left = left;
    //     Right = right;
    //     Up = up;
    //     Down = down;
    // }

    // public SKBitmap AsPaletteB(SKBitmap bitmap) => bitmap.ChangePalette(PaletteA, PaletteB);
}

internal class SpriteImage
{
    public readonly SpriteAnimation Animation;

    public SpriteImage(SpriteAnimation animation)
    {
        Animation = animation;
    }

    public void Draw(TileSheet sheetSlot, int x, int y, Palette palette, int flags = 0)
    {
        Graphics.DrawSpriteTile(
            sheetSlot,
            Animation.Frames[0].X,
            Animation.Frames[0].Y,
            Animation.Width,
            Animation.Height,
            x,
            y,
            palette,
            Animation.Frames[0].Flags | flags
        );
    }
};
