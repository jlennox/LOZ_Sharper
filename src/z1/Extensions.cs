using System.Collections.Immutable;
using System.Diagnostics;
using Silk.NET.Input;
using Silk.NET.Windowing;
using z1.Render;

namespace z1;

internal static class DirectionExtensions
{
    extension(Direction direction)
    {
        public bool IsHorizontal(Direction mask = Direction.FullMask)
        {
            return (direction & mask) is Direction.Left or Direction.Right;
        }

        public bool IsVertical(Direction mask = Direction.FullMask)
        {
            return (direction & mask) is Direction.Up or Direction.Down;
        }

        /// <summary>Does X or Y increase when walking in this direction?</summary>
        public bool IsGrowing() => direction is Direction.Right or Direction.Down;

        public int GetOrdinal()
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

        public Direction GetOppositeDirection()
        {
            return direction switch
            {
                Direction.Left => Direction.Right,
                Direction.Right => Direction.Left,
                Direction.Up => Direction.Down,
                Direction.Down => Direction.Up,
                _ => Direction.None
            };
        }

        public Direction GetNextDirection8()
        {
            var index = direction.GetDirection8Ord();
            index = (index + 1) % 8;
            return index.GetDirection8();
        }

        public Direction GetPrevDirection8()
        {
            var index = (uint)direction.GetDirection8Ord();
            index = (index - 1) % 8;
            return index.GetDirection8();
        }

        public int GetDirection8Ord()
        {
            // JOE: TODO: Use index of
            for (var i = 0; i < _allDirs().Length; i++)
            {
                if (direction == _allDirs()[i]) return i;
            }
            return 0;
        }

        public Point GetOffset()
        {
            if (direction.HasFlag(Direction.Right)) return new Point(1, 0);
            if (direction.HasFlag(Direction.Left)) return new Point(-1, 0);
            if (direction.HasFlag(Direction.Down)) return new Point(0, 1);
            if (direction.HasFlag(Direction.Up)) return new Point(0, -1);
            return new Point(0, 0);
        }

        public Direction GetOppositeDir8()
        {
            var ord = GetDirection8Ord(direction);
            ord = (ord + 4) % 8;
            return GetDirection8(ord);
        }
    }

    // ORIGINAL: the original game goes in the opposite order.
    public static Direction GetOrdDirection(this int ord) => (Direction)(1 << ord);

    private static ReadOnlySpan<Direction> _allDirs() => [
        Direction.Up, Direction.Up | Direction.Right, Direction.Right,
        Direction.Right | Direction.Down, Direction.Down,
        Direction.Down | Direction.Left, Direction.Left, Direction.Left | Direction.Up];

    public static Direction GetDirection8(this int ord) => _allDirs()[ord];
    public static Direction GetDirection8(this uint ord) => _allDirs()[(int)ord];
}

internal static class Extensions
{
    extension(Random random)
    {
        public byte GetByte() => (byte)random.Next(256);
        public Direction GetDirection8() => random.Next(8).GetDirection8();
        public T GetRandom<T>(T[] array) => array[random.Next(array.Length)];
        public T GetRandom<T>(ImmutableArray<T> array) => array[random.Next(array.Length)];
        public T GetRandom<T>(T a, T b) => random.Next(2) == 0 ? a : b;
        public bool GetBool() => (random.Next() & 1) == 1;
    }

    public static bool IsBlueWalker(this ObjType type)
    {
        return type is ObjType.BlueFastOctorock or ObjType.BlueSlowOctorock or ObjType.BlueMoblin or ObjType.BlueLynel;
    }

    public static char GetKeyCharacter(this Key key)
    {
        if ((int)key < 32 || (int)key > 126)
        {
            return '\0';
        }
        return (char)key;
    }

    public static Rectangle GetRect(this IWindow window)
    {
        return new Rectangle(window.Position.X, window.Position.Y, window.Size.X, window.Size.Y);
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

    public static DrawingFlags GetDrawingFlags(this TiledTile tile)
    {
        var a = tile.IsFlippedX ? DrawingFlags.FlipX : 0;
        var b = tile.IsFlippedY ? DrawingFlags.FlipY : 0;
        return a | b;
    }

    public static BlockType GetBlockType(this TileType tile)
    {
        return tile switch
        {
            TileType.Ground => BlockType.Ground,
            TileType.Rock => BlockType.Rock,
            TileType.Headstone => BlockType.Headstone,
            TileType.Block => BlockType.Block,
            _ => throw new Exception($"TileType {tile} has no associated BlockType.")
        };
    }

    public static TileType GetTileType(this BlockType block)
    {
        return block switch
        {
            BlockType.Ground => TileType.Ground,
            BlockType.Rock => TileType.Rock,
            BlockType.Headstone => TileType.Headstone,
            BlockType.Block => TileType.Block,
            _ => throw new Exception($"BlockType {block} has no associated TileType.")
        };
    }

    public static bool IsLockedType(this DoorType type) => type is DoorType.Bombable or DoorType.Key or DoorType.Key2;

    public static bool CollidesWall(this TileBehavior behavior) => behavior is TileBehavior.Wall or TileBehavior.Doorway or TileBehavior.Door;
    public static bool CollidesTile(this TileBehavior behavior) => behavior >= TileBehavior.FirstSolid;
    public static bool CanWalk(this TileBehavior behavior) => behavior is < TileBehavior.FirstSolid or TileBehavior.Doorway or TileBehavior.Cave;
}

internal static class PointExtensions
{
    extension(Point point)
    {
        public static IEnumerable<Point> FromRange(int width, int height)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    yield return new Point(x, y);
                }
            }
        }

        public bool HasReachedPoint(int targetX, int targetY, Direction direction)
        {
            return direction switch
            {
                Direction.Left => point.X <= targetX && point.Y == targetY,
                Direction.Right => point.X >= targetX && point.Y == targetY,
                Direction.Up => point.Y <= targetY && point.X == targetX,
                Direction.Down => point.Y >= targetY && point.X == targetX,
                _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Invalid direction."),
            };
        }

        public PointXY ToPointXY() => new(point.X, point.Y);
    }

    extension(PointF point)
    {
        public PointF Rotate(float angle)
        {
            var sine = Math.Sin(angle);
            var cosine = Math.Cos(angle);

            return new PointF(
                (float)(point.X * cosine - point.Y * sine),
                (float)(point.X * sine + point.Y * cosine));
        }

        public int GetSector16()
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

            var rotated = Rotate(new PointF(x, y), Pi.NegPiOver8);
            y = rotated.Y;

            if (y > 0) sector++;

            sector %= 16;
            return sector;
        }
    }

    public static Point ToPoint(this PointXY? point) => point == null ? default : new Point(point.X, point.Y);
    public static Point ToPoint(this EntryPosition entry) => new(entry.X, entry.Y);
    public static ref T FromPoint<T>(this T[,] layout, Point point) => ref layout[point.X, point.Y];
}