using System.Runtime.InteropServices;

namespace z1;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct SpriteFrame(byte X, byte Y, byte Flags);
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SpriteAnimation
{
    public byte Length;
    public byte Width;
    public byte Height;
    public SpriteFrame FrameA;
    public SpriteFrame FrameB;

    public SpriteFrame GetFrame(int index) => index switch {
        0 => FrameA,
        1 => FrameB,
        _ => throw new IndexOutOfRangeException()
    };
}

internal sealed class SpriteAnimator
{
    public SpriteAnimation? Animation { get; set; }
    public int Time { get; set; }
    public int DurationFrames { get; set; }

    public SpriteAnimator(SpriteAnimation? animation = null)
    {
        Animation = animation;
    }

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

    public void DrawFrameInternal(TileSheet sheetSlot, int x, int y, Palette palette, int frame, int flags)
    {
        var anim = Animation ?? throw new Exception();
        Graphics.DrawSpriteTile(
            sheetSlot,
            anim.GetFrame(frame).X,
            anim.GetFrame(frame).Y,
            anim.Width,
            anim.Height,
            x,
            y,
            palette,
            anim.GetFrame(frame).Flags | flags
        );
    }
}

internal sealed class SpriteImage
{
    public SpriteAnimation Animation;

    public SpriteImage() { }
    public SpriteImage(SpriteAnimation animation)
    {
        Animation = animation;
    }

    public void Draw(TileSheet sheetSlot, int x, int y, Palette palette, int flags = 0)
    {
        Graphics.DrawSpriteTile(
            sheetSlot,
            Animation.FrameA.X,
            Animation.FrameA.Y,
            Animation.Width,
            Animation.Height,
            x,
            y,
            palette,
            Animation.FrameA.Flags | flags
        );
    }
};
