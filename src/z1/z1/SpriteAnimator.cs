using System.Runtime.InteropServices;

namespace z1;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly record struct SpriteFrame(byte X, byte Y, byte Flags)
{
    public DrawingFlags DrawingFlags => (DrawingFlags)Flags;
}

// JOE: Arg. I hate this, but I couldn't think of a cleaner way to go about it.
internal interface ILoadVariableLengthData<T>
{
    T LoadVariableData(ReadOnlySpan<byte> buf);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SpriteAnimationStruct : ILoadVariableLengthData<SpriteAnimation>
{
    public byte Length;
    public byte Width;
    public byte Height;

    public SpriteAnimation LoadVariableData(ReadOnlySpan<byte> buf)
    {
        var frames = MemoryMarshal.Cast<byte, SpriteFrame>(buf)[..Length].ToArray();
        return new SpriteAnimation(this, frames);
    }
}

internal sealed class SpriteAnimation
{
    public SpriteFrame[] Frames { get; }
    public SpriteFrame FrameA => Frames[0];

    public byte Length => _animStruct.Length;
    public byte Width => _animStruct.Width;
    public byte Height => _animStruct.Height;

    private readonly SpriteAnimationStruct _animStruct;

    public SpriteAnimation(SpriteAnimationStruct animStruct, SpriteFrame[] frames)
    {
        Frames = frames;
        _animStruct = animStruct;
    }
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
        if (Animation != null && Animation.Length > 0 && DurationFrames > 0)
        {
            var frameDuration = DurationFrames / Animation.Length;
            Time = (Time + frameDuration) % DurationFrames;
        }
    }

    public void Draw(TileSheet sheetSlot, float x, float y, Palette palette, DrawingFlags flags = DrawingFlags.None)
    {
        Draw(sheetSlot, (int)x, (int)y, palette, flags);
    }

    public void Draw(TileSheet sheetSlot, int x, int y, Palette palette, DrawingFlags flags = DrawingFlags.None)
    {
        if (Animation != null && Animation.Length > 0 && DurationFrames > 0)
        {
            var index = (Animation.Length * Time) / DurationFrames;
            DrawFrameInternal(sheetSlot, x, y, palette, index, flags);
        }
    }

    public void DrawFrame(TileSheet sheetSlot, int x, int y, Palette palette, int frame, DrawingFlags flags = DrawingFlags.None)
    {
        if (Animation != null && Animation.Length > frame)
        {
            DrawFrameInternal(sheetSlot, x, y, palette, frame, flags);
        }
    }

    public void DrawFrameInternal(TileSheet sheetSlot, int x, int y, Palette palette, int frame, DrawingFlags flags)
    {
        var anim = Animation ?? throw new Exception();
        Graphics.DrawSpriteTile(
            sheetSlot,
            anim.Frames[frame].X,
            anim.Frames[frame].Y,
            anim.Width,
            anim.Height,
            x,
            y,
            palette,
            anim.Frames[frame].DrawingFlags | flags
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

    public void Draw(TileSheet sheetSlot, int x, int y, Palette palette, DrawingFlags flags = DrawingFlags.None)
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
            Animation.FrameA.DrawingFlags | flags
        );
    }
}
