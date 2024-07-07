using SkiaSharp;

namespace z1;

internal enum Direction { None = 0, Right = 1, Left = 2, Down = 4, Up = 8, Mask = 0x0F }

internal sealed class Game
{
    const float AspectRatio = 16 / 9;

    public static readonly SKColor[][] Palettes = z1.Palettes.GetPalettes();

    public Keys KeyCode { get; private set; } = Keys.None;

    public Link Link = new();
    public Actor ChaseTarget { get; set; }

    private SKSurface? _surface;
    public readonly List<Actor> _actors = new();

    public SKSurface Surface => _surface ?? throw new InvalidOperationException("Surface not set");

    public Game()
    {
        ChaseTarget = Link;
        _actors.Add(Link);
    }

    public void SetKeys(Keys keys) => KeyCode = keys;
    public void UnsetKeys() => KeyCode = Keys.None;

    public void UpdateActors()
    {
        var surface = Surface;
        surface.Canvas.Clear();
        Link.Move(this);
        foreach (var actor in _actors) actor.Tick(this);
        foreach (var actor in _actors) actor.Draw(this, _surface!.Canvas);
    }

    public void UpdateScreenSize(SKSurface surface, SKImageInfo info)
    {
        const int NesResX = 256;
        const int NesResY = 240;

        _surface = surface;

        var scale = Math.Min((float)info.Width / NesResX, (float)info.Height / NesResY);
        var offsetX = (info.Width - scale * NesResX) / 2;
        var offsetY = (info.Height - scale * NesResY) / 2;

        surface.Canvas.Translate(offsetX, offsetY);
        surface.Canvas.Scale(scale, scale);
    }

    public void DrawBitmap(SKBitmap bitmap, RectangleF destRect)
    {
        var surface = _surface ?? throw new InvalidOperationException("Surface not set");
        var canvas = surface.Canvas;
        canvas.DrawBitmap(bitmap, destRect.X, destRect.Y);
    }
}
