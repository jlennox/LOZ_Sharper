using System;
using System.Collections.Immutable;
using System.Text;
using OpenTK.Graphics.OpenGL;
using z1.IO;

namespace z1.Randomizer;

// Randomized dungeon shape passes:
// 1. Create a shape with the right number of rooms, and mark special rooms (entry, item staircase, floor drop).
// 2. Fit rooms with specific requirements first -- such as rooms that have items. This is to ensure rooms without
//    criteria do not pre-emptively drain them from the pool.
// 3. Fit the rest of the rooms.
// 4. Mark the requirements needed for each item room. IE, if the dungeon entrance requires a ladder to get to, and
//    the room item is a drop but requires a recorder, those 2 items are marked as requirements.
// 5. Fit items to the dungeon rooms now that requirements are known.

// Known issues:
// * Old men aren't seen as blocking north.
// * 9 isn't randomized?

// TODO:

internal readonly record struct DoorPair(Direction From, Direction To);

[Flags] // NIT: Drop "Flags", just pluralize it.
internal enum RoomRequirementFlags
{
    None,
    HasStaircase = 1 << 0,
    HasFloorDrop = 1 << 1,
    HasPushBlock = 1 << 2,
    IsEntrance = 1 << 3,
}

[Flags]
internal enum PathRequirements
{
    None,
    Ladder = 1 << 0,
    Recorder = 1 << 1,
    Arrow = 1 << 2,
    Food = 1 << 3,
}

internal readonly record struct RoomRequirements(
    Direction ConnectableDirections, // Does not mean they _are_ connected, just that there's nothing hard blocking it from being connected.
    IReadOnlyDictionary<DoorPair, PathRequirements> Paths,
    RoomRequirementFlags Flags,
    InteractableBlockObject? PushBlock,
    InteractableBlockObject? Staircase,
    int Difficulty)
{
    public bool HasStaircase => Flags.HasFlag(RoomRequirementFlags.HasStaircase);
    public bool HasFloorDrop => Flags.HasFlag(RoomRequirementFlags.HasFloorDrop);
    public bool HasPushBlock => Flags.HasFlag(RoomRequirementFlags.HasPushBlock);
    public bool IsEntrance => Flags.HasFlag(RoomRequirementFlags.IsEntrance);

    public bool HasShutterPushBlock => this is { HasPushBlock: true, PushBlock.Interaction.Effect: InteractionEffect.OpenShutterDoors };

    public static PathRequirements GetStairRequirements(GameRoom room)
    {
        var stairs = room.InteractableBlockObjects.FirstOrDefault(static t => t.Interaction.Entrance != null);
        if (stairs == null) return PathRequirements.None;
        if (stairs.Interaction.Interaction != Interaction.Revealed) return PathRequirements.None;

        var revealer = room.GetRevealer(stairs.Interaction);
        var requirements = PathRequirements.None;

        if (revealer.Interaction == Interaction.Push || revealer.RequiresAllEnemiesDefeated)
        {
            requirements |= GetRequirementItems(room.Monsters.Select(t => t.ObjType));
        }
        return requirements;
    }

    private static PathRequirements GetRequirementItem(ObjType type)
    {
        return type switch
        {
            ObjType.BlueGohma => PathRequirements.Arrow,
            ObjType.RedGohma => PathRequirements.Arrow,
            ObjType.Grumble => PathRequirements.Food,
            // ObjType.PondFairy -> ItemId.Recorder?
            ObjType.Digdogger1 => PathRequirements.Recorder,
            ObjType.Digdogger2 => PathRequirements.Recorder,
            _ => PathRequirements.None,
        };
    }

    private static PathRequirements GetRequirementItems(IEnumerable<ObjType> type)
    {
        var requirements = PathRequirements.None;
        foreach (var t in type) requirements |= GetRequirementItem(t);
        return requirements;
    }
}

internal sealed class RandomizerDungeonFlags
{
    public bool Rooms { get; set; } = true;
    public bool Shapes { get; set; } = true;
    public int ShapesSizeVariance { get; set; } = 2;
    public bool RandomizeMonsters { get; set; } = true;
    public bool AlwaysHaveCompass { get; set; } = true;
    public bool AlwaysHaveMap { get; set; } = true;
    public bool AllowFalseWalls { get; set; } = false;
}

internal sealed class RandomizerFlags
{
    public RandomizerDungeonFlags Dungeon { get; } = new();

    public void CheckIntegrity()
    {
        if (Dungeon is { Shapes: true, Rooms: false })
        {
            throw new InvalidOperationException("Cannot randomize dungeon shapes without randomizing rooms.");
        }
    }
}

// IDEA: Make an attribute that allows each property to specify its bit location, for the flag textualization.
internal sealed class RandomizerState
{
    public int Seed { get; }
    public RandomizerFlags Flags { get; }
    public Random RoomListRandom { get; }
    public Random RoomRandom { get; }

    public List<GameRoom> RandomDungeonRoomList { get; } = new();

    public List<ItemId> DungeonItems { get; } = [
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
        ItemId.MagicBoomerang];

    public RandomizerState(int seed, RandomizerFlags flags)
    {
        Seed = seed;
        Flags = flags;

        var seedRandom = new Random(seed);
        RoomListRandom = new Random(seedRandom.Next());
        RoomRandom = new Random(seedRandom.Next());

        DungeonItems.Shuffle(RoomRandom);
    }


    // All special rooms have been fit. Now strip all the interactions that make rooms "special" from them from the
    // remaining pool.
    public void RemoveSpecialnessFromRemainingRooms()
    {
        var toremove = new List<InteractableBase>();
        foreach (var room in RandomDungeonRoomList)
        {
            toremove.Clear();

            foreach (var obj in room.InteractableBlockObjects)
            {
                if (obj.Interaction.Entrance != null)
                {
                    toremove.Add(obj.Interaction);

                    if (obj.Interaction.Interaction == Interaction.Revealed)
                    {
                        toremove.Add(room.GetRevealer(obj.Interaction));
                    }
                }
            }

            room.InteractableBlockObjects = room.InteractableBlockObjects
                .Where(t => !toremove.Contains(t.Interaction))
                .ToImmutableArray();
        }
    }
}

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

internal record DungeonState(GameWorld Dungeon, DungeonState.Cell[,] Layout, DungeonStats Stats, Point EntranceLocation)
{
    internal enum RoomType { None, Normal, Entrance, FloorDrop, ItemStaircase, TransportStaircase }
    internal readonly record struct Cell(
        RoomType Type,
        Point Point,
        ItemId Item = ItemId.None,
        GameRoom? GameRoom = null,
        Direction RequiredDoors = Direction.None)
    {
        public bool IsSpecialRoom => Type is RoomType.FloorDrop or RoomType.ItemStaircase or RoomType.Entrance or RoomType.TransportStaircase;

        public override string ToString()
        {
            var room = GameRoom ?? throw new Exception("Room not fit.");
            return $"{room.Id} (point: {Point.X},{Point.Y}, type: {Type})";
        }
    }

    private const int _maxWidth = 8;
    private const int _maxHeight = 8;

    private static readonly DebugLog _log = new(nameof(DungeonState));
    private static bool IsValidPoint(Point p) => p.X is >= 0 and < _maxWidth && p.Y is >= 0 and < _maxHeight;

    private ref Cell this[Point i] => ref Layout[i.X, i.Y];
    private bool _hasTransportsAttached;
    private bool _hasIdsUpdated;
    private bool _hasDoorsFit;

    private static IEnumerable<Point> EachPoint()
    {
        for (var y = 0; y < _maxHeight; y++)
        {
            for (var x = 0; x < _maxWidth; x++)
            {
                yield return new Point(x, y);
            }
        }
    }

    public IEnumerable<GameRoom> GetGameRooms()
    {
        return EachPoint().Select(t => this[t].GameRoom).Where(static t => t != null)!;
    }

    public IEnumerable<Cell> GetValidCells()
    {
        foreach (var point in EachPoint())
        {
            var cell = this[point];
            if (cell.Type == RoomType.None) continue;
            yield return cell;
        }
    }

    public static DungeonState Create(GameWorld world, RandomizerState state)
    {
        var stats = DungeonStats.Create(world);
        var (staircaseItemCount, floorItemCount, _) = stats;
        var rng = state.RoomRandom;

        var hasCompass = state.Flags.Dungeon.AlwaysHaveCompass || rng.GetBool();
        var hasMap = state.Flags.Dungeon.AlwaysHaveMap || rng.GetBool();

        if (hasCompass) ++floorItemCount;
        if (hasMap) ++floorItemCount;

        var sizeVariance = state.Flags.Dungeon.ShapesSizeVariance;
        var newSize = world.Rooms.Length + rng.Next(-sizeVariance, sizeVariance);
        var layout = new Cell[_maxWidth, _maxHeight];
        var roomCount = 0;// JOE: TODO: Randomize this. At least make the X be random.
        var entrance = new Point(_maxWidth / 2, _maxHeight - 1);
        var roomType = RoomType.Entrance;
        var normalRooms = new List<Point>();
        var restartsStat = 0; // for debugging
        var iterationsStat = 0; // for debugging
        var checkDirections = Direction.DoorDirectionOrder;

        foreach (var point in EachPoint())
        {
            layout[point.X, point.Y] = new Cell(RoomType.None, point);
        }

        // It's possible for the random walk to not fill enough rooms, so we restart the walk until we do.
        while (roomCount < newSize)
        {
            ++restartsStat;

            _log.Write(nameof(Create),
                $"Placing rooms for dungeon {world.UniqueId} " +
                $"{nameof(newSize)} {newSize}, " +
                $"{nameof(restartsStat)} {restartsStat}, " +
                $"{nameof(iterationsStat)} {iterationsStat}, " +
                $"{nameof(roomCount)}: {roomCount}");

            var path = new Stack<Point> { entrance };
            while (path.TryPop(out var current) && roomCount < newSize)
            {
                ref var cell = ref layout[current.X, current.Y];
                if (cell.Type == RoomType.None)
                {
                    if (roomType == RoomType.Normal) normalRooms.Add(current);
                    cell = cell with { Type = roomType };
                    roomType = RoomType.Normal;
                    roomCount++;
                }

                // Must be randomized because it'll lean to index 0, since roomCount consumes them.
                checkDirections.Shuffle(rng);
                foreach (var dir in checkDirections)
                {
                    var next = current + dir.GetOffset();
                    if (!IsValidPoint(next)) continue;
                    if (rng.GetBool()) path.Push(next);
                }
                ++iterationsStat;
            }
        }

        void SetRoomTypeRandomly(int count, RoomType type)
        {
            for (var i = 0; i < count; i++)
            {
                if (normalRooms.Count == 0) throw new Exception();
                var index = rng.Next(normalRooms.Count);
                var roomPoint = normalRooms[index];
                ref var cell = ref layout[roomPoint.X, roomPoint.Y];
                cell = cell with { Type = type };
                normalRooms.RemoveAt(index);
            }
        }

        void SetRoomItemRandomly(RoomType type, ItemId item)
        {
            var rooms = EachPoint().Where(t => layout[t.X, t.Y].Type == type).ToArray();
            if (rooms.Length == 0) throw new Exception();

            var index = rng.Next(rooms.Length);
            var roomPoint = rooms[index];
            ref var cell = ref layout[roomPoint.X, roomPoint.Y];
            cell = cell with { Item = item };
            _log.Write(nameof(Create), $"SetRoomItemRandomly: {item} in room at {roomPoint}.");
        }

        SetRoomTypeRandomly(staircaseItemCount, RoomType.ItemStaircase);
        SetRoomTypeRandomly(floorItemCount, RoomType.FloorDrop);

        if (hasCompass) SetRoomItemRandomly(RoomType.FloorDrop, ItemId.Compass);
        if (hasMap) SetRoomItemRandomly(RoomType.FloorDrop, ItemId.Map);

        return new DungeonState(world, layout, stats, entrance);
    }

    private void FitRooms(
        RandomizerState state,
        Func<Cell, bool> shouldFit,
        Func<Cell, RoomRequirements, bool> fit)
    {
        foreach (var point in EachPoint())
        {
            ref var cell = ref this[point];
            if (cell.GameRoom != null) continue; // Already set.
            if (cell.Type == RoomType.None) continue;
            if (!shouldFit(cell)) continue;

            var requiredDirections = Direction.None;
            if (IsValidPoint(point + new Point(-1, 0))) requiredDirections |= Direction.Left;
            if (IsValidPoint(point + new Point(1, 0))) requiredDirections |= Direction.Right;
            if (IsValidPoint(point + new Point(0, -1))) requiredDirections |= Direction.Up;
            if (IsValidPoint(point + new Point(0, 1))) requiredDirections |= Direction.Down;

            // Find a room that meets the criteria.
            for (var i = 0; i < state.RandomDungeonRoomList.Count && cell.GameRoom == null; i++)
            {
                var room = state.RandomDungeonRoomList[i];
                var requirements = Randomizer.GetRoomRequirements(room);

                // TOFIX: This may drain valid rooms and only leave a room with a single direction connection that is
                // not valid for the spot.
                if ((requiredDirections & requirements.ConnectableDirections) != requiredDirections) continue;

                if (fit(cell, requirements))
                {
                    if (cell.Type != RoomType.Entrance)
                    {
                        room.Settings.Options &= ~RoomFlags.IsEntrance;
                    }
                    else
                    {
                        room.Settings.Options |= RoomFlags.IsEntrance;
                    }

                    cell = cell with { GameRoom = room };
                    state.RandomDungeonRoomList.RemoveAt(i);
                    break;
                }
            }

            if (cell.GameRoom == null)
            {
                throw new Exception("Exhausted rooms without being able to fit.");
            }
        }
    }

    // Room fitting happens in 2 passes. All special rooms in all dungeons should be fit first. This ensures their
    // needs are met before rooms that allow their criteria are used up by rooms that do not have criteria.
    public void FitSpecialRooms(RandomizerState state)
    {
        FitRooms(
            state,
            shouldFit: cell => cell.IsSpecialRoom,
            fit: (cell, requirements) => cell.Type switch
            {
                RoomType.FloorDrop => requirements.HasFloorDrop,
                RoomType.ItemStaircase => requirements.HasStaircase,
                RoomType.TransportStaircase => requirements.HasStaircase,
                RoomType.Entrance => requirements.IsEntrance,
                _ => false,
            });
    }

    public void FitNormalRooms(RandomizerState state)
    {
        FitRooms(
            state,
            // Change this to GameRoom != null? I dislike that because it's a bit... 'eh. It's too much of a catch all.
            // I'd rather it misbehave and exception out than silently behave in a way that's unexpected.
            shouldFit: cell => !cell.IsSpecialRoom,
            fit: (_, _) => true);
    }

    public void EnsureAllRoomsAreFit()
    {
        foreach (var cell in GetValidCells())
        {
            if (cell.GameRoom == null) throw new Exception($"Room at {cell} not fit.");
        }
    }

    public void FitItems(RandomizerState state)
    {
        ItemId FitItem(Cell cell)
        {
            if (state.DungeonItems.Count == 0)
            {
                _log.Error(nameof(FitItems), $"No more dungeon items to fit into {cell}.");
                throw new Exception("No more dungeon items to fit.");
            }

            for (var i = 0; i < state.DungeonItems.Count; i++)
            {
                var item = state.DungeonItems[i];
                foreach (var requirements in TryWalkToRoom(EntranceLocation, cell.Point))
                {
                    if (!Randomizer.PathRequirementsAllows(requirements, item))
                    {
                        _log.Write(nameof(FitItems), $"❌ Cannot fit {item} in {cell} due to requirements {requirements}.");
                        continue;
                    }

                    state.DungeonItems.RemoveAt(i);
                    _log.Write(nameof(FitItems), $"✅ Fit {item} in {cell} with requirements {requirements}.");
                    return item;
                }
            }

            var requirementsList = TryWalkToRoom(EntranceLocation, cell.Point).ToArray();
            _log.Error(nameof(FitItems), $"Failed to fit any items into {cell}");

            throw new Exception($"Failed to fit any items into {cell}");
        }

        foreach (var cell in GetValidCells())
        {
            if (cell.Type != RoomType.ItemStaircase) continue;

            _log.Write(nameof(FitItems), $"Fitting RoomType.ItemStaircase in {cell}.");
            var room = cell.GameRoom ?? throw new Exception();
            var staircase = room.InteractableBlockObjects.First(static t => t.Interaction.Entrance != null);

            staircase.Interaction.Entrance = Entrance.CreateItemCellar(FitItem(cell), cell.Point.ToPointXY());
        }

        foreach (var cell in GetValidCells())
        {
            if (cell.Type != RoomType.FloorDrop) continue;

            _log.Write(nameof(FitItems), $"Fitting RoomType.FloorDrop in {cell} ({cell.Item}).");
            var room = cell.GameRoom ?? throw new Exception();
            var floorItem = room.InteractableBlockObjects.First(static t => t.Interaction.Item?.Item != null);
            floorItem.Interaction.Item = new RoomItem
            {
                Item = cell.Item == ItemId.None ? FitItem(cell) : cell.Item,
                Options = ItemObjectOptions.IsRoomItem | ItemObjectOptions.MakeItemSound
            };
        }
    }

    public void UpdateRoomIds()
    {
        _hasIdsUpdated = true;

        foreach (var cell in GetValidCells())
        {
            var gameRoom = cell.GameRoom ?? throw new Exception("Room not fit.");
            gameRoom.Id = $"{Dungeon.UniqueId}/{cell.Point.X},{cell.Point.Y} ({cell.GameRoom.UniqueId})";
        }
    }

    public void AttachTransportHallways(RandomizerState state)
    {
        if (!_hasIdsUpdated) throw new Exception("Cannot attach transport hallways prior to updating IDs.");

        _hasTransportsAttached = true;
        if (Stats.TrainsportStairsCount == 0) return;

        var rng = state.RoomRandom;
        var cells = GetValidCells().Where(static t => t.Type == RoomType.TransportStaircase).Shuffle(rng).ToStack();
        cells.Shuffle(rng);

        if (cells.Count % 2 != 0) throw new Exception("Transport staircase count is not even.");

        while (cells.TryPop(out var cellA) && cells.TryPop(out var cellB))
        {
            var roomA = cellA.GameRoom ?? throw new Exception("Room not fit.");
            var roomB = cellB.GameRoom ?? throw new Exception("Room not fit.");

            var stairsA = roomA.InteractableBlockObjects.First(t => t.Interaction.Entrance != null);
            var stairsB = roomB.InteractableBlockObjects.First(t => t.Interaction.Entrance != null);

            stairsA.Interaction.Entrance = Entrance.CreateTransportRoom(cellA.Point.ToPointXY(), cellB.Point.ToPointXY(), true);
            stairsB.Interaction.Entrance = Entrance.CreateTransportRoom(cellA.Point.ToPointXY(), cellB.Point.ToPointXY(), false);
        }

        if (cells.Count > 0) throw new UnreachableCodeException();
    }

    private readonly record struct TryWalkSearchPath(Point Point, PathRequirements Requirements, Direction EntryDirection);
    private readonly record struct TryWalkVisited(Point Point, PathRequirements Requirements, Direction EntryDirection);

    // Can we walk to a specific room from the entrance?
    private bool CanWalkToRoom(Point start, Point destination, bool ignoreDoors = false)
    {
        foreach (var _ in TryWalkToRoom(start, destination, ignoreDoors)) return true;
        return false;
    }

    // Can we walk to a specific room from the entrance?
    private IEnumerable<PathRequirements> TryWalkToRoom(Point start, Point destination, bool ignoreDoors = false)
    {
        if (!_hasTransportsAttached) throw new Exception($"{nameof(TryWalkToRoom)} was called prior to attaching transports.");
        if (!ignoreDoors && !_hasDoorsFit) throw new Exception($"{nameof(TryWalkToRoom)} was called prior to fitting doors.");

        // If we've visited a map before with identical set of requirements and entry point, then there's no reason to repeat it.
        var visited = new HashSet<TryWalkVisited>();
        var paths = new Stack<TryWalkSearchPath> { new TryWalkSearchPath(start, PathRequirements.None, Direction.Down) };
        var seenRequirements = new HashSet<PathRequirements>();

        static IEnumerable<InteractableBlockObject> GetTransportBlocks(GameRoom? room)
        {
            ArgumentNullException.ThrowIfNull(room);

            foreach (var block in room.InteractableBlockObjects)
            {
                var entrance = block.Interaction.Entrance;
                if (entrance == null) continue;
                if (entrance.Destination != CommonUnderworldRoomName.Transport) continue;
                yield return block;
            }
        }

        bool TryGetTransportExit(GameRoom? room, out Point exit)
        {
            ArgumentNullException.ThrowIfNull(room);
            exit = default;

            foreach (var block in GetTransportBlocks(room))
            {
                var arguments = block.Interaction.Entrance?.Arguments ?? throw new Exception();
                var searchFor = arguments.ExitLeft == room.Id ? arguments.ExitRight : arguments.ExitLeft;

                foreach (var cell in GetValidCells())
                {
                    var cellRoom = cell.GameRoom ?? throw new Exception("Room not fit.");
                    if (cellRoom.Id != searchFor) continue;

                    exit = cell.Point;
                    return true;
                }
            }

            return false;
        }

        while (paths.TryPop(out var current))
        {
            if (current.Point == destination)
            {
                if (seenRequirements.Add(current.Requirements))
                {
                    yield return current.Requirements;
                }
                continue;
            }

            if (!visited.Add(new TryWalkVisited(current.Point, current.Requirements, current.EntryDirection))) continue;

            var cell = this[current.Point];
            if (cell.Type == RoomType.None) continue;
            var room = cell.GameRoom ?? throw new Exception("Room not fit.");


            // Add the other end of a transport staircase to toVisit.
            if (TryGetTransportExit(cell.GameRoom, out var exit))
            {
                var transportRequirements = current.Requirements | RoomRequirements.GetStairRequirements(room);
                paths.Push(new TryWalkSearchPath(exit, transportRequirements, Direction.None));
            }

            var roomRequirements = Randomizer.GetRoomRequirements(room);

            foreach (var direction in Direction.DoorDirectionOrder)
            {
                if (!ignoreDoors && !cell.RequiredDoors.HasFlag(direction)) continue;
                if (!roomRequirements.ConnectableDirections.HasFlag(direction)) continue;
                var next = current.Point + direction.GetOffset();
                if (!IsValidPoint(next)) continue;

                paths.Push(new TryWalkSearchPath(next, current.Requirements, direction.GetOppositeDirection()));
            }
        }
    }

    public void FitUnderworldDoors(RandomizerState state)
    {
        // Fitting doors is done in two passes.
        // 1. Walk from the entrance, in a random order, checking if you can get to the next rooms (CanWalkToRoom).
        //    If you can't, mark RequiredDoors as needing a door there.
        // 2. Then fit each door type into the dungeon.
        //
        // It's important that the order the adjoining rooms are checked is random, otherwise we'd keep favoring a
        // specific path.
        // It's also important that this be depth-first. Other-wise each adjoining room to the entrance would be seen
        // as unreachable and create a heavy bias and predictable layout.

        _hasDoorsFit = true;
        var rng = state.RoomRandom;
        var directions = Direction.DoorDirectionOrder.ToArray();

        IEnumerable<(Point Location, Direction Direction)> GetAdjoiningRooms(Point point)
        {
            directions.Shuffle(rng);
            foreach (var direction in directions)
            {
                var next = point + direction.GetOffset();
                if (!IsValidPoint(next)) continue;
                if (this[next].Type == RoomType.None) continue;
                yield return (next, direction);
            }
        }

        // 0. We're going to mark-sweep this. The mark phase is clearing out all of the doors.
        // This matters because we need connecting rooms to have the same door type. We'll
        // set them as we generate the main room, so once we get to them, we'll see they have a door there and skip.
        foreach (var room in GetGameRooms()) room.UnderworldDoors.Clear();

        // 1. Walk randomly from the entrance, with full back tracking, making sure each room is reachable.
        // If not, mark a door as needed.
        var visited = new HashSet<Point>();
        var toVisit = new ValueStack<Point>(EntranceLocation);
        while (toVisit.TryPop(out var location))
        {
            if (!visited.Add(location)) continue;
            ref var current = ref this[location];

            foreach (var (nextLocation, direction) in GetAdjoiningRooms(location))
            {
                // I _think_ pushing this prior to other checks is the correct move?
                toVisit.Push(nextLocation);

                // Likely added from a connecting room.
                if (current.RequiredDoors.HasFlag(direction)) continue;
                // We can already visit this room. Adding a door is not needed.
                // TODO: Figure out a probability here that isn't 50/50?
                if (CanWalkToRoom(EntranceLocation, nextLocation, true) && rng.GetBool()) continue;

                current = current with { RequiredDoors = current.RequiredDoors | direction };
                ref var adjoining = ref this[nextLocation];
                adjoining = adjoining with { RequiredDoors = adjoining.RequiredDoors | direction.GetOppositeDirection() };
            }

            if (current.RequiredDoors == Direction.None)
            {
                _log.Error(nameof(FitUnderworldDoors), $"Room at {location.X},{location.Y} has no required doors after walk.");
                throw new Exception("Room has no required doors after walk.");
            }
        }

        // 2. Walk the rooms and fit doors according to our probabilities.
        foreach (var point in EachPoint())
        {
            var cell = this[point];
            var room = cell.GameRoom;
            if (cell.Type == RoomType.None) continue;
            if (room == null) throw new Exception("Room not fit.");

            // This is a given. We only support Player coming in from the bottom.
            if (room.Settings.IsEntrance)
            {
                room.UnderworldDoors[Direction.Down] = DoorType.Open;
            }

            var anyShutters = false;
            foreach (var direction in Direction.DoorDirectionOrder)
            {
                // It was set already from an adjoining room.
                if (room.UnderworldDoors.ContainsKey(direction)) continue;

                if (!room.Connections.TryGetValue(direction, out var connected) ||
                    !cell.RequiredDoors.HasFlag(direction))
                {
                    room.UnderworldDoors[direction] = DoorType.Wall;
                    continue;
                }

                var doorType = Stats.GetRandomDoorType(rng);
                room.UnderworldDoors[direction] = doorType;

                connected.UnderworldDoors[direction.GetOppositeDirection()] = doorType;

                if (doorType == DoorType.Shutter) anyShutters = true;
            }

            var requirements = Randomizer.GetRoomRequirements(room);
            if (anyShutters)
            {
                // If we don't have an appropriate push block already, add a room cleared trigger for opening the shutters.
                if (!requirements.HasShutterPushBlock)
                {
                    // err, I hate this .add
                    room.RoomInteractions = room.RoomInteractions.Add(RoomInteraction.CreateOpenShutterDoors());
                }
            }
            else
            {
                if (requirements.HasShutterPushBlock)
                {
                    room.InteractableBlockObjects = room.InteractableBlockObjects.Remove(requirements.PushBlock!);
                }
            }
        }
    }

    public void UpdateRoomCoordinates()
    {
        const int assumedWidth = 256;
        const int assumedHeight = 176;

        foreach (var point in EachPoint())
        {
            var room = this[point].GameRoom;
            if (room == null) continue;

            room.WorldEntry.X = point.X * assumedWidth;
            room.WorldEntry.Y = point.Y * assumedHeight;
        }
    }

    public void PrintDebugShape()
    {
        _log.Write(nameof(PrintDebugShape), $"{Dungeon.UniqueId} layout:\n{GetDebugDisplay()}");
    }

    public string GetDebugDisplay()
    {
        var sb = new StringBuilder(_maxWidth * _maxHeight + _maxWidth + 100);

        sb.Append("   ");
        for (var i = 0; i < _maxWidth; i++) sb.Append(i);
        sb.AppendLine();

        for (var y = 0; y < _maxHeight; y++)
        {
            sb.Append(y);
            sb.Append(": ");
            for (var x = 0; x < _maxWidth; x++)
            {
                sb.Append(this[new Point(x, y)].Type switch
                {
                    RoomType.None => ' ',
                    RoomType.Normal => 'N',
                    RoomType.Entrance => 'E',
                    RoomType.FloorDrop => 'F',
                    RoomType.ItemStaircase => 'I',
                    RoomType.TransportStaircase => 'T',
                    _ => '?',
                });
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

internal sealed class Randomizer
{
    private readonly record struct DoorToDoorSearchPath(int X, int Y, bool RequiresLadder)
    {
        public DoorToDoorSearchPath(Point point, bool requiresLadder) : this(point.X, point.Y, requiresLadder) { }
        public bool SamePoint(DoorToDoorSearchPath other) => X == other.X && Y == other.Y;
        public int Distance(DoorToDoorSearchPath other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

        public override string ToString() => RequiresLadder ? $"({X}, {Y}, WET)" : $"({X}, {Y})";
    }

    private static readonly Dictionary<string, RoomRequirements> _requirementsCache = new();
    private static readonly DebugLog _log = new(nameof(Randomizer));

    private static void OverworldRoom()
    {
    }

    private static IEnumerable<GameWorld> GetAllDungeons(GameWorld overworld)
    {
        var seen = new HashSet<string>();
        foreach (var room in overworld.Rooms)
        {
            foreach (var interactable in room.InteractableBlockObjects)
            {
                var entrance = interactable.Interaction.Entrance;
                if (entrance == null) continue;
                if (entrance.DestinationType != GameWorldType.Underworld) continue;

                var dungeon = overworld.World.GetWorld(GameWorldType.Underworld, entrance.Destination);
                if (!seen.Add(dungeon.Name)) continue;

                yield return dungeon;
            }
        }
    }

    public static void Randomize(GameWorld overworld, RandomizerState state)
    {
        _log.Write(nameof(Randomize), $"Starting dungeon randomization {state.Seed}.");

        var dungeons = GetAllDungeons(overworld).ToArray();

        foreach (var dungeon in dungeons)
        {
            state.RandomDungeonRoomList.AddRange(dungeon.Rooms);

            // I think we now properly fix the dangling staircases problem.
            // foreach (var room in dungeon.Rooms)
            // {
            //     var requirements = GetRoomRequirements(room);
            //     if (requirements.HasStaircase)
            //     {
            //         // TOFIX: OK. So this is wrong. Unfit staircases should just be removed?
            //         var staircase = room.InteractableBlockObjects.First(static t => t.Interaction.Entrance != null);
            //         staircase.Interaction.Entrance = Entrance.CreateItemCellar(ItemId.FiveRupees, PointXY.Zero);
            //     }
            // }
        }

        state.RandomDungeonRoomList.Shuffle(state.RoomListRandom);

        var shapes = dungeons
            .Select(t => (t, DungeonState.Create(t, state)))
            .ToArray();

        // 1. Fit all special rooms must be fit.
        foreach (var (_, shape) in shapes) shape.FitSpecialRooms(state);

        foreach (var (dungeon, shape) in shapes)
        {
            state.RemoveSpecialnessFromRemainingRooms();
            // 2. Fit all normal rooms.
            shape.FitNormalRooms(state);
            // Exception if any rooms are still not fit.
            shape.EnsureAllRoomsAreFit();
            shape.UpdateRoomIds();
            shape.PrintDebugShape();
            // Attach the transport hallways to each other. This must be done before adding doors, because this can make
            // doors in specific locations unneeded.
            shape.AttachTransportHallways(state);
            shape.UpdateRoomCoordinates();
            // This must happen after recreating the gameworld because room connections are created inside of that constructor.
            // That might be a refactoring point sometime later.
            shape.FitUnderworldDoors(state);
            shape.FitItems(state);
            var randomizedDungeon = new GameWorld(dungeon.World, shape.GetGameRooms().ToArray(), dungeon.Settings, dungeon.Name);
            foreach (var room in shape.GetGameRooms()) room.GameWorld = randomizedDungeon;
            overworld.World.SetWorld(randomizedDungeon, dungeon.Name);
        }
    }

    public static RoomRequirements GetRoomRequirements(GameRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);

        var cacheId = room.OriginalUniqueId?.ToString() ?? room.UniqueId;
        if (_requirementsCache.TryGetValue(cacheId, out var cached)) return cached;

        // For the above world, there's the two 2Q caves that require ladder, the fairy pond, and likely others.
        if (!room.HasUnderworldDoors || room.UnderworldDoors.Count == 1) return default;

        var paths = new Dictionary<DoorPair, PathRequirements>();

        static DoorToDoorSearchPath GetEntranceLocation(Direction direction)
        {
            var (corner, _, _) = World.DoorCorner.Get(direction);
            return new DoorToDoorSearchPath(corner.X, corner.Y, false);
        }

        static Point GetLocationInfrontOfEntrance(Direction direction)
        {
            var location = GetEntranceLocation(direction);
            // We're doing 2 because we want the upper-left corner of the block in front of the door.
            // Stairways only mark that spot -- but I believe that's incorrect and they should make the 2x2 region?
            return direction switch
            {
                Direction.Right => new Point(location.X - 2, location.Y),
                Direction.Left => new Point(location.X + 2, location.Y),
                Direction.Up => new Point(location.X, location.Y + 2),
                Direction.Down => new Point(location.X, location.Y - 2),
                _ => throw new Exception(),
            };
        }

        // Technically... there can be multiple exit locations. The original game only used a static value for all
        // exits, but it would be nice to ultimately support this.
        static Point GetTransportExitPosition(GameRoom room)
        {
            static Point Core(GameRoom room)
            {
                foreach (var currentRoom in room.GameWorld.Rooms)
                {
                    foreach (var block in currentRoom.InteractableBlockObjects)
                    {
                        var entrance = block.Interaction.Entrance;
                        if (entrance == null) continue;
                        var arguments = entrance.Arguments;
                        if (arguments == null) continue;
                        if (arguments.ExitLeft == room.Id) return entrance.ExitPosition.ToPoint();
                        if (arguments.ExitRight == room.Id) return entrance.ExitPosition.ToPoint();
                    }
                }

                // This is the default in the OG game, they all transport here.
                return new Point(0x60, 0xA0);
            }

            var result = Core(room);
            return new Point(result.X / room.TileWidth, result.Y / room.TileHeight);
        }

        var validDirections = Direction.None;
        var invalidDirections = Direction.None;
        var hasOldMan = room.CaveSpec is { DoesControlsBlockingWall: true };
        if (hasOldMan) invalidDirections |= Direction.Up;

        foreach (var direction in Direction.DoorDirectionOrder)
        {
            if (invalidDirections.HasFlag(direction)) continue;
            var infront = GetLocationInfrontOfEntrance(direction);
            var behavior = room.RoomMap.Behavior(infront.X, infront.Y);
            if (behavior is TileBehavior.GenericWalkable or TileBehavior.Sand)
            {
                validDirections |= direction;
            }
        }

        var walkingSearch = new PriorityQueue<DoorToDoorSearchPath, int>();
        var visited = new HashSet<DoorToDoorSearchPath>();

        // Walk from each door to each other door, and record if a ladder was required.
        // Direction.None is used for transport entrances.
        foreach (var startingDirection in ((IEnumerable<Direction>)Direction.DoorDirectionOrder).Add(Direction.None))
        {
            var isTransportedInEntrance = startingDirection == Direction.None;
            if (!validDirections.HasFlag(startingDirection) && !isTransportedInEntrance) continue;

            var from = isTransportedInEntrance
                ? new DoorToDoorSearchPath(GetTransportExitPosition(room), false)
                : GetEntranceLocation(startingDirection);

            foreach (var endingDirection in Direction.DoorDirectionOrder)
            {
                if (endingDirection == startingDirection) continue;
                if (!validDirections.HasFlag(endingDirection)) continue;

                var to = GetEntranceLocation(endingDirection);

                walkingSearch.Clear();
                visited.Clear();
                walkingSearch.Enqueue(from, 0);

                var hasLadderedPath = false;
                var foundPathWithNoLadder = false;

                while (walkingSearch.TryDequeue(out var current, out _))
                {
                    if (!visited.Add(current)) continue;
                    if (current.SamePoint(to))
                    {
                        if (!current.RequiresLadder)
                        {
                            paths.Add(new DoorPair(startingDirection, endingDirection), PathRequirements.None);
                            foundPathWithNoLadder = true;
                            break;
                        }

                        hasLadderedPath = true;
                    }

                    static IEnumerable<DoorToDoorSearchPath> Neighbors(RoomTileMap map, DoorToDoorSearchPath point)
                    {
                        if (point.X > 0) yield return point with { X = point.X - 1 };
                        if (point.X < map.Width - 1) yield return point with { X = point.X + 1 };
                        if (point.Y > 0) yield return point with { Y = point.Y - 1 };
                        if (point.Y < map.Height - 1) yield return point with { Y = point.Y + 1 };
                    }

                    foreach (var neighbor in Neighbors(room.RoomMap, current))
                    {
                        if (visited.Contains(neighbor)) continue;

                        // TODO: This does need to determine if it's a double wide water, which means the ladder
                        // can't cross it.
                        var behavior = room.RoomMap.Behavior(neighbor.X, neighbor.Y);
                        if (behavior.CanWalk() || behavior == TileBehavior.Door)
                        {
                            walkingSearch.Enqueue(neighbor, to.Distance(neighbor));
                        }
                        else if (behavior == TileBehavior.Water)
                        {
                            walkingSearch.Enqueue(neighbor with { RequiresLadder = true }, to.Distance(neighbor));
                        }
                    }
                }

                if (!foundPathWithNoLadder && hasLadderedPath)
                {
                    paths.Add(new DoorPair(startingDirection, endingDirection), PathRequirements.Ladder);
                }
            }
        }

        // Compute the difficulty somehow, if we end up wanting it.
        // For example, Gleeok is difficult, but is worse a room with the square of water, which is yet worse when
        // there's spike traps in the corners.

        var flags = RoomRequirementFlags.None;
        InteractableBlockObject? pushBlock = null;
        InteractableBlockObject? staircase = null;
        foreach (var obj in room.ObjectLayer.Objects)
        {
            switch (obj)
            {
                case TileBehaviorGameMapObject { TileBehavior: TileBehavior.Stairs or TileBehavior.SlowStairs }:
                case InteractableBlockObject { Interaction.Entrance: not null }:
                    flags |= RoomRequirementFlags.HasStaircase;
                    staircase = obj as InteractableBlockObject;
                    break;
                case InteractableBlockObject { Interaction.Item: not null }:
                    flags |= RoomRequirementFlags.HasFloorDrop;
                    break;
                case InteractableBlockObject { Interaction.Interaction: Interaction.Push } blockObject:
                    flags |= RoomRequirementFlags.HasPushBlock;
                    pushBlock = blockObject;
                    break;
            }
        }

        if (room.Settings.IsEntrance)
        {
            flags |= RoomRequirementFlags.IsEntrance;
        }

        var roomRequirements = new RoomRequirements(
            validDirections,
            paths,
            flags,
            pushBlock,
            staircase,
            0);

        _requirementsCache[cacheId] = roomRequirements;
        return roomRequirements;
    }

    // Move from this class?
    public static bool PathRequirementsAllows(PathRequirements requirements, ItemId item) => item switch
    {
        ItemId.Ladder => !requirements.HasFlag(PathRequirements.Ladder),
        ItemId.Recorder => !requirements.HasFlag(PathRequirements.Recorder),
        ItemId.WoodArrow or ItemId.SilverArrow => !requirements.HasFlag(PathRequirements.Arrow),
        ItemId.Food => !requirements.HasFlag(PathRequirements.Food),
        _ => true,
    };
}