using System;

namespace z1.Randomizer;

internal readonly record struct DoorPair
{
    public RoomEntrances From { get; init; }
    public RoomEntrances To { get; init; }

    private DoorPair(RoomEntrances from, RoomEntrances to)
    {
        From = from;
        To = to;
    }

    public static DoorPair Create(RoomEntrances from, RoomEntrances to)
    {
        if (from == to) throw new Exception($"Cannot construct a DoorPair from two identical entrances {from}->{to}.");

        if (from == RoomEntrances.Stairs || to == RoomEntrances.Stairs)
        {
            // Stairs are directional, so we have to respect the order.
            return new DoorPair(from, to);
        }

        // This does make the assumption that if you can go from A to B, that you can go from B to A.
        return from < to ? new DoorPair(from, to) : new DoorPair(to, from);
    }

    // Returns each unique pair of directions.
    public static IEnumerable<DoorPair> GetAllPairs(GameRoom room)
    {
        var seen = new HashSet<DoorPair>();

        foreach (var start in RoomEntrances.EntranceOrderWithStairs)
        {
            foreach (var end in RoomEntrances.EntranceOrderWithStairs)
            {
                if (start == end) continue;
                var pair = Create(start, end);
                if (seen.Add(pair)) yield return pair;
            }
        }

        if (room.EntryPosition != null)
        {
            foreach (var end in RoomEntrances.EntranceOrderWithStairs)
            {
                yield return Create(RoomEntrances.Entry, end);
            }
        }

        if (room.HasFloorItem())
        {
            foreach (var end in RoomEntrances.EntranceOrderWithStairs)
            {
                yield return Create(RoomEntrances.Item, end);
            }
        }
    }

    public bool Contains(RoomEntrances entrance) => From == entrance || To == entrance;

    public override string ToString() => $"{From}→{To}";
}