﻿using System.Numerics;
using z1.Actors;

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

    public static int GetOrdinal(this Direction direction) => BitOperations.TrailingZeroCount((int)direction);

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

    private static readonly Direction[] _allDirs = { (Direction)8, (Direction)9, (Direction)1, (Direction)5, (Direction)4, (Direction)6, (Direction)2, (Direction)10 };

    public static int GetDirection8Ord(this Direction dir)
    {
        // JOE: TODO: Use index of
        for (var i = 0; i < _allDirs.Length; i++)
        {
            if (dir == _allDirs[i])
                return i;
        }
        return 0;
    }

    public static Direction GetDirection8(this int ord) => _allDirs[ord];
    public static Direction GetDirection8(this uint ord) => _allDirs[ord];

    public static Direction GetOppositeDir8(this Direction dir)
    {
        var ord = GetDirection8Ord(dir);
        ord = (ord + 4) % 8;
        return GetDirection8(ord);
    }

    public static byte GetByte(this Random random) => (byte)random.Next(256);
    public static Direction GetDirection8(this Random random) => Random.Shared.Next(8).GetDirection8();

    public static void Shuffle<T>(this T[] array)
    {
        int n = array.Length;
        for (var i = n - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            T temp = array[i];
            array[i] = array[j];
            array[j] = temp;
        }
    }

    public static bool IsBlueWalker(this ObjType type)
    {
        return type is ObjType.BlueFastOctorock or ObjType.BlueSlowOctorock or ObjType.BlueMoblin or ObjType.BlueLynel;
    }
}
