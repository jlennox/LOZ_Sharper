using SkiaSharp;

namespace z1;

internal abstract class Actor
{
    public PointF Position { get; set; }
    public SizeF Size { get; set; }
    public Direction Dir;

    public RectangleF Rect => new RectangleF(Position, Size);

    public abstract void Tick(Game game);
    public abstract void Draw(Game game, SKCanvas canvas);
}
