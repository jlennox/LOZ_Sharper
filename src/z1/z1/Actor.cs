using SkiaSharp;

namespace z1;

internal abstract class Actor
{
    private static readonly Game _game = new();
    public Point Position { get; set; }
    public SizeF Size { get; set; }
    public Direction Dir = Direction.Left;
    public Game Game = _game; // TODO

    public RectangleF Rect => new RectangleF(Position, Size);

    public abstract void Tick(Game game);
    public abstract void Draw(Game game, SKCanvas canvas);

    public virtual bool IsPlayer => false;
}
