using System;
using System.Collections.Immutable;

namespace z1.Randomizer;

[Flags]
internal enum RoomEntrances
{
    None = 0,
    Right = 1 << 0,
    Left = 1 << 1,
    Bottom = 1 << 2,
    Top = 1 << 3,
    Stairs = 1 << 4,
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

    extension(RoomEntrances)
    {
        public static ImmutableArray<RoomEntrances> EntranceOrder => _entranceOrder;
        public static ImmutableArray<RoomEntrances> EntranceOrderWithStairs => _entranceOrderWithStairs;
    }
}