using System;
using System.Collections.Immutable;

namespace z1.Randomizer;

// This is different enough from Direction that it deserves its own value.
// Notably, Stairs. Before Direction.None was re-used for stairs, but you can't
// test a bitwise value set for 0.
// This may want to be propagated into the actual game logic later?
[Flags]
internal enum RoomEntrances
{
    None = 0,
    Right = 1,
    Left = 2,
    Bottom = 4,
    Top = 8,
    Stairs = 16,
    Entry = 32, // In the overworld when you spawn in.
}

internal static class Extensions
{
    private static readonly ImmutableArray<RoomEntrances> _entranceOrder = [RoomEntrances.Right, RoomEntrances.Left, RoomEntrances.Bottom, RoomEntrances.Top];
    private static readonly ImmutableArray<RoomEntrances> _entranceOrderWithStairs = _entranceOrder.Add(RoomEntrances.Stairs);

    public static Point GetOffset(this RoomEntrances entrance) => entrance switch
    {
        RoomEntrances.Right => new Point(1, 0),
        RoomEntrances.Left => new Point(-1, 0),
        RoomEntrances.Bottom => new Point(0, 1),
        RoomEntrances.Top => new Point(0, -1),
        _ => throw new Exception(),
    };

    public static RoomEntrances GetOpposite(this RoomEntrances entrance) => entrance switch
    {
        RoomEntrances.Right => RoomEntrances.Left,
        RoomEntrances.Left => RoomEntrances.Right,
        RoomEntrances.Bottom => RoomEntrances.Top,
        RoomEntrances.Top => RoomEntrances.Bottom,
        _ => throw new Exception(),
    };

    public static RoomEntrances ToEntrance(this Direction direction) => direction switch
    {
        Direction.None => RoomEntrances.None,
        Direction.Right => RoomEntrances.Right,
        Direction.Left => RoomEntrances.Left,
        Direction.Down => RoomEntrances.Bottom,
        Direction.Up => RoomEntrances.Top,
        _ => throw new Exception(),
    };

    public static RoomEntrances ToEntrance(this Direction? direction) => direction switch
    {
        null => RoomEntrances.None,
        _ => ToEntrance(direction.Value),
    };

    public static Direction ToDirection(this RoomEntrances entrance) => entrance switch
    {
        RoomEntrances.None => Direction.None,
        RoomEntrances.Right => Direction.Right,
        RoomEntrances.Left => Direction.Left,
        RoomEntrances.Bottom => Direction.Down,
        RoomEntrances.Top => Direction.Up,
        _ => throw new Exception(),
    };

    extension(RoomEntrances)
    {
        public static ImmutableArray<RoomEntrances> EntranceOrder => _entranceOrder;
        public static ImmutableArray<RoomEntrances> EntranceOrderWithStairs => _entranceOrderWithStairs;
    }
}