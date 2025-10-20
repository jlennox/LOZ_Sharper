using System;
using z1.IO;

namespace z1.Randomizer;

internal readonly record struct DungeonStats
{
    private readonly record struct DoorProbability(int Probability, DoorType Type);

    private static readonly DoorType[] _significantDoorTypes = [DoorType.Open, DoorType.Key, DoorType.Bombable, DoorType.FalseWall, DoorType.Shutter];
    private static readonly DebugLog _log = new(nameof(DungeonStats));

    public int StaircaseItemCount { get; init; }
    public int FloorItemCount { get; init; }
    public Dictionary<DoorType, int> DoorTypeCounts { get; init; }
    public int TrainsportStairsCount { get; init; }

    private readonly DoorProbability[] _orderedDoorProbabilities;
    private readonly int _totalDoorCount;

    public DungeonStats(int staircaseItemCount, int floorItemCount, Dictionary<DoorType, int> doorTypeCounts, int trainsportStairsCount)
    {
        if (trainsportStairsCount % 2 != 0) throw new ArgumentOutOfRangeException(nameof(trainsportStairsCount), trainsportStairsCount, "Must be even.");

        StaircaseItemCount = staircaseItemCount;
        FloorItemCount = floorItemCount;
        DoorTypeCounts = doorTypeCounts;
        TrainsportStairsCount = trainsportStairsCount;

        // From the doors in the original dungeon, compute the probabilities of each one showing up.
        var probabilities = new List<DoorProbability>();
        var runningCount = 0;

        foreach (var type in _significantDoorTypes)
        {
            var count = doorTypeCounts.GetValueOrDefault(type, 0);
            runningCount += count;
            probabilities.Add(new DoorProbability(runningCount, type));
        }
        _orderedDoorProbabilities = probabilities.OrderBy(t => t.Probability).ToArray();

        _totalDoorCount = _significantDoorTypes.Sum(t => doorTypeCounts.GetValueOrDefault(t, 0));
        if (_totalDoorCount == 0) throw new Exception("The dungeon has no doors.");
    }

    public DoorType GetRandomDoorType(Random rng)
    {
        var doorRng = rng.Next(_totalDoorCount);
        return _orderedDoorProbabilities.First(t => t.Probability >= doorRng).Type;
    }

    // Perhaps dynamically create this list?
    private static bool IsDungeonItem(ItemId? item) => item
        is ItemId.WoodSword
        or ItemId.WhiteSword
        or ItemId.MagicSword
        or ItemId.Food
        or ItemId.Recorder
        or ItemId.BlueCandle
        or ItemId.RedCandle
        or ItemId.WoodArrow
        or ItemId.SilverArrow
        or ItemId.Bow
        or ItemId.MagicKey
        or ItemId.Raft
        or ItemId.Ladder
        or ItemId.PowerTriforce
        or ItemId.Rod
        or ItemId.Book
        or ItemId.BlueRing
        or ItemId.RedRing
        or ItemId.Bracelet
        or ItemId.Letter
        or ItemId.WoodBoomerang
        or ItemId.MagicBoomerang;

    private static bool HasItemStaircase(GameRoom room)
    {
        var itemId = room.InteractableBlockObjects
            .Select(static t => t.Interaction.Entrance?.Arguments?.ItemId)
            .FirstOrDefault(IsDungeonItem);

        if (itemId != null)
        {
            _log.Write(nameof(HasItemStaircase), $"Found {itemId} in room {room.UniqueId}.");
            return true;
        }

        return false;
    }

    private static bool HasFloorItem(GameRoom room)
    {
        var itemId = room.InteractableBlockObjects
            .Select(static t => t.Interaction.Item?.Item)
            .FirstOrDefault(IsDungeonItem);

        if (itemId != null)
        {
            _log.Write(nameof(HasFloorItem), $"Found {itemId} in room {room.UniqueId}.");
            return true;
        }

        return false;
    }

    public static DungeonStats Create(GameWorld world)
    {
        static bool HasTransportStairs(GameRoom room)
        {
            return room.InteractableBlockObjects.Any(static t => t.Interaction.Entrance?.Destination == CommonUnderworldRoomName.Transport);
        }

        _log.Write(nameof(Create), $"Creating for {world.UniqueId}.");

        var staircaseItemCount = world.Rooms.Count(HasItemStaircase);
        var floorItemCount = world.Rooms.Count(HasFloorItem);
        var doorTypeCounts = world.Rooms
            .SelectMany(static t => t.UnderworldDoors.Values)
            .GroupBy(static t => t)
            .ToDictionary(static g => g.Key, static g => g.Count());
        var transportStairsCount = world.Rooms.Count(HasTransportStairs);

        _log.Write(nameof(Create),
            $"{nameof(staircaseItemCount)}: {staircaseItemCount}, " +
            $"{nameof(floorItemCount)}: {floorItemCount}, " +
            $"{nameof(transportStairsCount)}: {transportStairsCount}");

        return new DungeonStats(staircaseItemCount, floorItemCount, doorTypeCounts, transportStairsCount);
    }

    public void Deconstruct(out int staircaseItemCount, out int floorItemCount, out Dictionary<DoorType, int> doorTypeCounts)
    {
        staircaseItemCount = StaircaseItemCount;
        floorItemCount = FloorItemCount;
        doorTypeCounts = DoorTypeCounts;
    }
}