using System;
using System.Collections.Immutable;

namespace z1.Randomizer;

[Flags]
internal enum RoomEntrances
{
    None = 0,
    Right = 1 << 0,
    Left = 1 << 1,
    Down = 1 << 2,
    Up = 1 << 3,
    Stairs = 1 << 4,
}

internal static class Extensions
{
    private static readonly ImmutableArray<RoomEntrances> _entranceOrder = [RoomEntrances.Right, RoomEntrances.Left, RoomEntrances.Down, RoomEntrances.Up];
    private static readonly ImmutableArray<RoomEntrances> _entranceOrderWithStairway = _entranceOrder.Add(RoomEntrances.Stairs);

    public static Point GetOffset(this RoomEntrances entrance) => entrance switch
    {
        RoomEntrances.Right => new Point(1, 0),
        RoomEntrances.Left => new Point(-1, 0),
        RoomEntrances.Down => new Point(0, 1),
        RoomEntrances.Up => new Point(0, -1),
        _ => throw new Exception(),
    };

    public static RoomEntrances GetOpposite(this RoomEntrances entrance) => entrance switch
    {
        RoomEntrances.Right => RoomEntrances.Left,
        RoomEntrances.Left => RoomEntrances.Right,
        RoomEntrances.Down => RoomEntrances.Up,
        RoomEntrances.Up => RoomEntrances.Down,
        _ => throw new Exception(),
    };

    public static RoomEntrances ToEntrance(this Direction direction) => direction switch
    {
        Direction.Right => RoomEntrances.Right,
        Direction.Left => RoomEntrances.Left,
        Direction.Down => RoomEntrances.Down,
        Direction.Up => RoomEntrances.Up,
        _ => throw new Exception(),
    };

    extension(RoomEntrances)
    {
        public static ImmutableArray<RoomEntrances> EntranceOrder => _entranceOrder;
        public static ImmutableArray<RoomEntrances> EntranceOrderWithStairway => _entranceOrderWithStairway;
    }
}