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
        if (from == to) throw new Exception();

        // This does make the assumption that if you can go from A to B, that you can go from B to A.
        // I believe this to always be true.
        return from < to ? new DoorPair(from, to) : new DoorPair(to, from);
    }

    // Returns each unique pair of directions.
    public static IEnumerable<DoorPair> GetAllPairs(RoomEntrances directionMask, bool includeStairs)
    {
        var seen = new HashSet<DoorPair>();
        var directions = RoomEntrances.EntranceOrder.Where(d => directionMask.HasFlag(d)).ToArray();

        foreach (var start in directions)
        {
            foreach (var end in directions)
            {
                var pair = Create(start, end);
                if (seen.Add(pair)) yield return pair;
            }
        }

        if (includeStairs)
        {
            foreach (var start in directions) yield return Create(start, RoomEntrances.Stairs);
        }
    }

    public override string ToString() => $"{From}→{To}";

    public void Deconstruct(out RoomEntrances from, out RoomEntrances to)
    {
        from = From;
        to = To;
    }
}