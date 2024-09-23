using System.Collections.Immutable;
using System.Diagnostics;
using Silk.NET.Input;
using Silk.NET.Windowing;
using z1.Common.Data;
using z1.Render;

namespace z1;

internal static class Extensions
{
    public static bool IsHorizontal(this Direction direction, Direction mask = Direction.FullMask)
    {
        return (direction & mask) is Direction.Left or Direction.Right;
    }

    public static bool IsVertical(this Direction direction, Direction mask = Direction.FullMask)
    {
        return (direction & mask) is Direction.Up or Direction.Down;
    }

    /// <summary>Does X or Y increase when walking in this direction?</summary>
    public static bool IsGrowing(this Direction direction) => direction is Direction.Right or Direction.Down;

    public static int GetOrdinal(this Direction direction)
    {
        for (var i = 0; i < 4; i++)
        {
            if (((int)direction & 1) != 0)
            {
                return i;
            }
            direction = (Direction)((int)direction >> 1);
        }

        return 0;
    }

    public static Direction GetOppositeDirection(this Direction direction)
    {
        return direction switch {
            Direction.Left => Direction.Right,
            Direction.Right => Direction.Left,
            Direction.Up => Direction.Down,
            Direction.Down => Direction.Up,
            _ => Direction.None
        };
    }

    public static Direction GetOrdDirection(this int ord)
    {
        // ORIGINAL: the original game goes in the opposite order.
        return (Direction)(1 << ord);
    }

    public static Direction GetNextDirection8(this Direction dir)
    {
        var index = dir.GetDirection8Ord();
        index = (index + 1) % 8;
        return index.GetDirection8();
    }

    public static Direction GetPrevDirection8(this Direction dir)
    {
        var index = (uint)dir.GetDirection8Ord();
        index = (index - 1) % 8;
        return index.GetDirection8();
    }

    private static ReadOnlySpan<Direction> _allDirs() => [
        Direction.Up, Direction.Up | Direction.Right, Direction.Right,
        Direction.Right | Direction.Down, Direction.Down,
        Direction.Down | Direction.Left, Direction.Left, Direction.Left | Direction.Up];

    public static int GetDirection8Ord(this Direction dir)
    {
        // JOE: TODO: Use index of
        for (var i = 0; i < _allDirs().Length; i++)
        {
            if (dir == _allDirs()[i]) return i;
        }
        return 0;
    }

    public static Direction GetDirection8(this int ord) => _allDirs()[ord];
    public static Direction GetDirection8(this uint ord) => _allDirs()[(int)ord];

    public static Direction GetOppositeDir8(this Direction dir)
    {
        var ord = GetDirection8Ord(dir);
        ord = (ord + 4) % 8;
        return GetDirection8(ord);
    }

    public static byte GetByte(this Random random) => (byte)random.Next(256);
    public static Direction GetDirection8(this Random random) => random.Next(8).GetDirection8();

    public static T GetRandom<T>(this Random random, T[] array) => array[random.Next(array.Length)];
    public static T GetRandom<T>(this Random random, ImmutableArray<T> array) => array[random.Next(array.Length)];
    public static T GetRandom<T>(this Random random, T a, T b) => random.Next(2) == 0 ? a : b;

    public static bool IsBlueWalker(this ObjType type)
    {
        return type is ObjType.BlueFastOctorock or ObjType.BlueSlowOctorock or ObjType.BlueMoblin or ObjType.BlueLynel;
    }

    public static bool HasReachedPoint(this Point link, int targetX, int targetY, Direction direction)
    {
        return direction switch {
            Direction.Left => link.X <= targetX && link.Y == targetY,
            Direction.Right => link.X >= targetX && link.Y == targetY,
            Direction.Up => link.Y <= targetY && link.X == targetX,
            Direction.Down => link.Y >= targetY && link.X == targetX,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Invalid direction."),
        };
    }

    public static PointF Rotate(this PointF point, float angle)
    {
        var sine = Math.Sin(angle);
        var cosine = Math.Cos(angle);

        return new PointF(
            (float)(point.X * cosine - point.Y * sine),
            (float)(point.X * sine + point.Y * cosine));
    }

    public static int GetSector16(this PointF point)
    {
        var x = point.X;
        var y = point.Y;
        var sector = 0;

        if (y < 0)
        {
            sector += 8;
            y = -y;
            x = -x;
        }

        if (x < 0)
        {
            sector += 4;
            var temp = x;
            x = y;
            y = -temp;
        }

        if (x < y)
        {
            sector += 2;
            var temp = y - x;
            x += y;
            y = temp;
            // Because we're only finding out the sector, only the angle matters, not the point along it.
            // So, we can skip multiplying x and y by 1/(2^.5)
        }

        var rotated = Rotate(new PointF(x, y), Global.NEG_PI_OVER_8);
        y = rotated.Y;

        if (y > 0) sector++;

        sector %= 16;
        return sector;
    }

    public static char GetKeyCharacter(this Key key)
    {
        if ((int)key < 32 || (int)key > 126)
        {
            return '\0';
        }
        return (char)key;
    }

    public static bool IEquals(this string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    public static bool IStartsWith(this string? a, string? b) => a?.StartsWith(b ?? "", StringComparison.OrdinalIgnoreCase) ?? false;
    public static bool IContains(this string? a, string? b) => a?.Contains(b ?? "", StringComparison.OrdinalIgnoreCase) ?? false;

    public static System.Drawing.Rectangle GetRect(this IWindow window)
    {
        return new System.Drawing.Rectangle(window.Position.X, window.Position.Y, window.Size.X, window.Size.Y);
    }

    public static void TryDispose<T>(this T? disposable) where T : IDisposable
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    public static Point ToPoint(this PointXY? point) => point == null ? default : new(point.X, point.Y);

    public static DrawingFlags GetDrawingFlags(this TiledTile tile)
    {
        var a = tile.IsFlippedX ? DrawingFlags.FlipX : 0;
        var b = tile.IsFlippedY ? DrawingFlags.FlipY : 0;
        return a | b;
    }
}
