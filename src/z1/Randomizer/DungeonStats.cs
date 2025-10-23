using System;
using System.Collections.Immutable;
using z1.IO;

namespace z1.Randomizer;

internal readonly record struct DungeonStats
{
    private readonly record struct DoorProbability(int Probability, DoorType Type);

    public static readonly ImmutableArray<ItemId> AllDungeonItems = [
        // ItemId.WoodSword,
        // ItemId.WhiteSword,
        // ItemId.MagicSword,
        // ItemId.Food,
        ItemId.Recorder,
        ItemId.BlueCandle,
        ItemId.RedCandle,
        // ItemId.WoodArrow,
        ItemId.SilverArrow,
        ItemId.Bow,
        ItemId.MagicKey,
        ItemId.Raft,
        ItemId.Ladder,
        // ItemId.PowerTriforce,
        ItemId.Rod,
        ItemId.Book,
        // ItemId.BlueRing,
        ItemId.RedRing,
        ItemId.Bracelet,
        // ItemId.Letter,
        ItemId.WoodBoomerang,
        ItemId.MagicBoomerang
    ];

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
    public static bool IsDungeonItem(ItemId? item) => item != null && AllDungeonItems.Contains(item.Value);

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