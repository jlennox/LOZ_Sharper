using System.Diagnostics;

namespace z1;

// This is so we don't need to reference winforms.
// JOE: TODO: Errrr. We're still referencing it for the mean time because otherwise a console window pops up.

[DebuggerDisplay("{X},{Y}")]
internal struct Point
{
    public static readonly Point Empty = new(0, 0);

    public int X;
    public int Y;

    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }

    public static Point Add(Point pt, Size sz) => new(unchecked(pt.X + sz.Width), unchecked(pt.Y + sz.Height));
    public static Point operator +(Point pt, Size sz) => Add(pt, sz);
    public static Point Add(Point pt, Point sz) => new(unchecked(pt.X + sz.X), unchecked(pt.Y + sz.Y));
    public static Point operator +(Point pt, Point sz) => Add(pt, sz);
    public static bool operator ==(Point left, Point right) => left.X == right.X && left.Y == right.Y;
    public static bool operator !=(Point left, Point right) => !(left == right);

    public void Deconstruct(out int x, out int y)
    {
        x = X;
        y = Y;
    }

    public override bool Equals(object? obj) => obj is Point point && this == point;
    public bool Equals(Point other) => X == other.X && Y == other.Y;
    public override int GetHashCode() => HashCode.Combine(X, Y);
}

[DebuggerDisplay("{X},{Y}")]
internal struct PointF
{
    public static readonly PointF Empty = new(0, 0);

    public float X;
    public float Y;

    public PointF(float x, float y)
    {
        X = x;
        Y = y;
    }
}

[DebuggerDisplay("{X},{Y},{Width},{Height}")]
internal struct Rectangle
{
    public static readonly Rectangle Empty = new(0, 0, 0, 0);

    public int X;
    public int Y;
    public int Width;
    public int Height;

    public int Top => Y;
    public int Left => X;
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public Size Size => new(Width, Height);

    public Rectangle(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public Rectangle(Point point, Size size)
    {
        X = point.X;
        Y = point.Y;
        Width = size.Width;
        Height = size.Height;
    }

    public bool IntersectsWith(Rectangle rect)
    {
        return rect.Left < Right && Left < rect.Right && rect.Top < Bottom && Top < rect.Bottom;
    }
}

[DebuggerDisplay("{Width},{Height}")]
internal struct SizeF
{
    public float Width;
    public float Height;

    public SizeF(float width, float height)
    {
        Width = width;
        Height = height;
    }
}

[DebuggerDisplay("{Width},{Height}")]
internal struct Size
{
    public int Width;
    public int Height;

    public Size(int width, int height)
    {
        Width = width;
        Height = height;
    }
}