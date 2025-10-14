using System;
using System.Collections.Immutable;
using System.Text;

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

internal readonly record struct RoomPathRequirement(
    Direction StartingDoor,
    Direction ExitDoor,
    ImmutableArray<ItemId> Requirements);

[Flags]
internal enum RoomRequirementFlags
{
    None,
    HasStaircase = 1 << 0,
    HasFloorDrop = 1 << 1,
    HasPushBlock = 1 << 2,
    IsEntrance = 1 << 3,
}

internal readonly record struct RoomRequirements(
    Direction ConnectableDirections, // Does not mean they _are_ connected, just that there's nothing hard blocking it from being connected.
    ImmutableArray<RoomPathRequirement> Paths,
    RoomRequirementFlags Flags,
    InteractableBlockObject? PushBlock,
    InteractableBlockObject? Staircase,
    int Difficulty)
{
    public bool HasStaircase => Flags.HasFlag(RoomRequirementFlags.HasStaircase);
    public bool HasFloorDrop => Flags.HasFlag(RoomRequirementFlags.HasFloorDrop);
    public bool HasPushBlock => Flags.HasFlag(RoomRequirementFlags.HasPushBlock);
    public bool IsEntrance => Flags.HasFlag(RoomRequirementFlags.IsEntrance);

    public bool ConnectsLeft => ConnectableDirections.HasFlag(Direction.Left);
    public bool ConnectsRight => ConnectableDirections.HasFlag(Direction.Right);
    public bool ConnectsUp => ConnectableDirections.HasFlag(Direction.Up);
    public bool ConnectsDown => ConnectableDirections.HasFlag(Direction.Down);

    public bool HasShutterPushBlock => this is { HasPushBlock: true, PushBlock.Interaction.Effect: InteractionEffect.OpenShutterDoors };
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
    public RandomizerFlags Flags { get; }
    public Random RoomListRandom { get; }
    public Random RoomRandom { get; }

    public List<GameRoom> RandomDungeonRoomList { get; } = new();

    public RandomizerState(int seed, RandomizerFlags flags)
    {
        Flags = flags;

        var seedRandom = new Random(seed);
        RoomListRandom = new Random(seedRandom.Next());
        RoomRandom = new Random(seedRandom.Next());
    }
}

internal readonly record struct DungeonStats
{
    private readonly record struct DoorProbability(int Probability, DoorType Type);

    private static readonly DoorType[] _significantDoorTypes = [DoorType.Open, DoorType.Key, DoorType.Bombable, DoorType.FalseWall, DoorType.Shutter];

    public int StaircaseItemCount { get; init; }
    public int FloorItemCount { get; init; }
    public Dictionary<DoorType, int> DoorTypeCounts { get; init; }

    private readonly DoorProbability[] _orderedDoorProbabilities;
    private readonly int _totalDoorCount;

    public DungeonStats(int staircaseItemCount, int floorItemCount, Dictionary<DoorType, int> doorTypeCounts)
    {
        StaircaseItemCount = staircaseItemCount;
        FloorItemCount = floorItemCount;
        DoorTypeCounts = doorTypeCounts;

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
        return room.InteractableBlockObjects.Any(static t => IsDungeonItem(t.Interaction.Entrance?.Arguments?.ItemId));
    }

    private static bool HasFloorItem(GameRoom room)
    {
        return room.InteractableBlockObjects.Any(static t => IsDungeonItem(t.Interaction.Item?.Item));
    }

    public static DungeonStats Get(GameWorld world)
    {
        var staircaseItemCount = world.Rooms.Count(HasItemStaircase);
        var floorItemCount = world.Rooms.Count(HasFloorItem);
        var doorTypeCounts = world.Rooms.SelectMany(t => t.UnderworldDoors.Values)
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());

        return new DungeonStats(staircaseItemCount, floorItemCount, doorTypeCounts);
    }

    public void Deconstruct(out int staircaseItemCount, out int floorItemCount, out Dictionary<DoorType, int> doorTypeCounts)
    {
        staircaseItemCount = StaircaseItemCount;
        floorItemCount = FloorItemCount;
        doorTypeCounts = DoorTypeCounts;
    }
}

internal record DungeonShape(DungeonShape.Room[,] Layout, DungeonStats Stats, Point EntranceLocation)
{
    internal enum RoomType { None, Normal, Entrance, FloorDrop, ItemStaircase }
    internal readonly record struct Room(
        RoomType Type,
        ItemId Item = ItemId.None,
        GameRoom? GameRoom = null,
        Direction RequiredDoors = Direction.None);

    private const int _maxWidth = 8;
    private const int _maxHeight = 8;

    private ref Room this[Point i] => ref Layout[i.X, i.Y];

    private static bool IsValidPoint(Point p) => p.X is >= 0 and < _maxWidth && p.Y is >= 0 and < _maxHeight;

    private static IEnumerable<Point> EachPoint()
    {
        for (var x = 0; x < _maxWidth; x++)
        {
            for (var y = 0; y < _maxHeight; y++)
            {
                yield return new Point(x, y);
            }
        }
    }

    public static DungeonShape Create(GameWorld world, RandomizerState state)
    {
        var stats = DungeonStats.Get(world);
        var (staircaseItemCount, floorItemCount, _) = stats;
        var rng = state.RoomRandom;

        var hasCompass = state.Flags.Dungeon.AlwaysHaveCompass || rng.GetBool();
        var hasMap = state.Flags.Dungeon.AlwaysHaveMap || rng.GetBool();

        if (hasCompass) ++floorItemCount;
        if (hasMap) ++floorItemCount;

        var sizeVariance = state.Flags.Dungeon.ShapesSizeVariance;
        var newSize = world.Rooms.Length + rng.Next(-sizeVariance, sizeVariance);
        var layout = new Room[_maxWidth, _maxHeight];
        var roomCount = 0;
        var entrance = new Point(_maxWidth / 2, _maxHeight - 1);
        var roomType = RoomType.Entrance;
        var normalRooms = new List<Point>();
        var restartsStat = 0; // for debugging
        var iterationsStat = 0; // for debugging
        while (roomCount < newSize)
        {
            var path = new Stack<Point>();
            path.Push(entrance);
            ++restartsStat;

            while (path.Count > 0 && roomCount < newSize)
            {
                var current = path.Pop();
                ref var cell = ref layout[current.X, current.Y];
                if (cell.Type == RoomType.None)
                {
                    if (roomType == RoomType.Normal) normalRooms.Add(current);
                    cell = cell with { Type = roomType };
                    roomType = RoomType.Normal;
                    roomCount++;
                }

                if (rng.GetBool() && current.X > 0) path.Push(new Point(current.X - 1, current.Y));
                if (rng.GetBool() && current.X < _maxWidth - 1) path.Push(new Point(current.X + 1, current.Y));
                if (rng.GetBool() && current.Y > 0) path.Push(new Point(current.X, current.Y - 1));
                if (rng.GetBool() && current.Y < _maxHeight - 1) path.Push(new Point(current.X, current.Y + 1));
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
        }

        SetRoomTypeRandomly(staircaseItemCount, RoomType.ItemStaircase);
        SetRoomTypeRandomly(floorItemCount, RoomType.FloorDrop);

        if (hasCompass) SetRoomItemRandomly(RoomType.FloorDrop, ItemId.Compass);
        if (hasMap) SetRoomItemRandomly(RoomType.FloorDrop, ItemId.Map);

        return new DungeonShape(layout, stats, entrance);
    }

    public IEnumerable<GameRoom> GetRooms()
    {
        var layout = Layout;
        return EachPoint().Select(t => layout[t.X, t.Y].GameRoom).Where(t => t != null)!;
    }

    private void FitRooms(RandomizerState state, Func<Room, bool> shouldFit, Func<Room, RoomRequirements, bool> fit)
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

                // TODO: This isn't _really_ what is wanted. We want to ensure all paths are accessible, but we don't
                // super care that all possible paths in all rooms are connected. IE, it's possible that a tunnel
                // or alternative path on foot connects them.
                // This also may drain valid rooms and only leave a room with a single direction connection that is
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
        FitRooms(state,
            cell => cell.Type is RoomType.FloorDrop or RoomType.ItemStaircase or RoomType.Entrance,
            (cell, requirements) =>
                (cell.Type is RoomType.FloorDrop && requirements.HasFloorDrop)
                || (cell.Type is RoomType.ItemStaircase && requirements.HasStaircase)
                || (cell.Type is RoomType.Entrance && requirements.IsEntrance));
    }

    public void FitRooms(RandomizerState state)
    {
        FitRooms(
            state,
            cell => cell.Type is not (RoomType.FloorDrop or RoomType.ItemStaircase or RoomType.Entrance),
            (_, _) => true);
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
        // It's also important that this be depth-first. Other-wise each adjoining room would be seen as unreachable,
        // and we'd once again get a heavy bias and predictable layout.

        // Can we walk to a specific room from the entrance?
        // TODO: This needs to support stairways.
        bool CanWalkToRoom(Point point)
        {
            var canwalkVisited = new HashSet<Point>();
            var canwalkToVisit = new ValueStack<Point>(EntranceLocation);

            while (canwalkToVisit.TryPop(out var current))
            {
                if (point == current) return true;
                if (!canwalkVisited.Add(current)) continue;

                var cell = this[current];
                var requirements = Randomizer.GetRoomRequirements(cell.GameRoom!);
                if (cell.Type == RoomType.None) continue;

                foreach (var direction in Direction.DoorDirectionOrder)
                {
                    if (!cell.RequiredDoors.HasFlag(direction)) continue;
                    if (!requirements.ConnectableDirections.HasFlag(direction)) continue;
                    var next = current + direction.GetOffset();
                    if (!IsValidPoint(next)) continue;

                    canwalkToVisit.Push(next);
                }
            }

            return false;
        }

        var rng = state.RoomRandom;
        var directions = Direction.DoorDirectionOrder.ToArray();

        IEnumerable<(Point Location, Direction Direction)> GetAdjoiningRooms(Point point)
        {
            Randomizer.Shuffle(directions, rng);
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
        foreach (var room in GetRooms()) room.UnderworldDoors.Clear();

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
                if (CanWalkToRoom(nextLocation) && rng.GetBool()) continue;

                current = current with { RequiredDoors = current.RequiredDoors | direction };
                ref var adjoining = ref this[nextLocation];
                adjoining = adjoining with { RequiredDoors = adjoining.RequiredDoors | direction.GetOppositeDirection() };
            }
        }

        // 2. Walk the rooms and fit doors.
        foreach (var point in EachPoint())
        {
            ref var cell = ref this[point];
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
                // It was set already from a connecting room.
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

    public string GetDebugDisplay()
    {
        var sb = new StringBuilder(_maxWidth * _maxHeight + _maxWidth);
        for (var y = 0; y < _maxHeight; y++)
        {
            for (var x = 0; x < _maxWidth; x++)
            {
                sb.Append(Layout[x, y].Type switch
                {
                    RoomType.None => ' ',
                    RoomType.Normal => 'N',
                    RoomType.Entrance => 'E',
                    RoomType.FloorDrop => 'F',
                    RoomType.ItemStaircase => 'I',
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
    private readonly record struct PathSearch(int X, int Y, bool RequiresLadder)
    {
        public bool SamePoint(PathSearch other) => X == other.X && Y == other.Y;
        public int Distance(PathSearch other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

        public override string ToString() => RequiresLadder ? $"({X}, {Y}, WET)" : $"({X}, {Y})";
    }

    private static readonly Dictionary<string, RoomRequirements> _requirementsCache = new();

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

    public static void Shuffle<T>(List<T> array, Random rng)
    {
        for (var i = array.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }

    public static void Shuffle<T>(T[] array, Random rng)
    {
        for (var i = array.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }

    public static void Randomize(GameWorld overworld, RandomizerState state)
    {
        var dungeons = GetAllDungeons(overworld).ToArray();

        foreach (var dungeon in dungeons)
        {
            state.RandomDungeonRoomList.AddRange(dungeon.Rooms);
        }

        Shuffle(state.RandomDungeonRoomList, state.RoomListRandom);

        var shapes = dungeons
            .Select(t => (t, DungeonShape.Create(t, state)))
            .ToArray();

        foreach (var (_, shape) in shapes)
        {
            shape.FitSpecialRooms(state);
        }

        foreach (var (dungeon, shape) in shapes)
        {
            shape.FitRooms(state);
            shape.UpdateRoomCoordinates();
            var randomizedDungeon = new GameWorld(dungeon.World, shape.GetRooms().ToArray(), dungeon.Settings, dungeon.Name);
            // This must become after recreate the gameworld because room connections are created inside of that constructor.
            // That might be a refactoring point sometime later.
            shape.FitUnderworldDoors(state);
            foreach (var room in shape.GetRooms()) room.GameWorld = randomizedDungeon;
            overworld.World.SetWorld(randomizedDungeon, dungeon.Name);
        }
    }

    public static void CreateDungeonShape(GameWorld world, RandomizerState state)
    {
        var shape = DungeonShape.Create(world, state);
    }

    public static RoomRequirements GetRoomRequirements(GameRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);

        var cacheId = room.OriginalUniqueId?.ToString() ?? room.UniqueId;
        if (_requirementsCache.TryGetValue(cacheId, out var cached)) return cached;

        // For the above world, there's the two 2Q caves that require ladder, the fairy pond, and likely others.
        if (!room.HasUnderworldDoors || room.UnderworldDoors.Count == 1) return default;

        var paths = new List<RoomPathRequirement>();

        static PathSearch GetEntranceLocation(Direction direction)
        {
            var (corner, _, _) = World.DoorCorner.Get(direction);
            return new PathSearch(corner.X, corner.Y, false);
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

        Span<Direction> validDoors = stackalloc Direction[Direction.DoorDirectionOrder.Length];
        var validDirections = Direction.None;
        var invalidDirections = Direction.None;
        var hasOldMan = room.CaveSpec is { DoesControlsBlockingWall: true };
        if (hasOldMan) invalidDirections |= Direction.Up;
        // var behaviorDebug = RoomMap.DebugDisplay();

        for (var i = 0; i < Direction.DoorDirectionOrder.Length; i++)
        {
            var direction = Direction.DoorDirectionOrder[i];
            if (invalidDirections.HasFlag(direction)) continue;
            var infront = GetLocationInfrontOfEntrance(direction);
            var behavior = room.RoomMap.Behavior(infront.X, infront.Y);
            if (behavior is TileBehavior.GenericWalkable or TileBehavior.Sand)
            {
                validDoors[i] = direction;
                validDirections |= direction;
            }
        }

        var walkingSearch = new PriorityQueue<PathSearch, int>();
        var visited = new HashSet<PathSearch>();

        // Walk from each door to each other door, and record if a ladder was required.
        foreach (var startingDirection in validDoors)
        {
            if (startingDirection == Direction.None) continue;

            foreach (var endingDirection in validDoors)
            {
                if (endingDirection == startingDirection) continue;
                if (endingDirection == Direction.None) continue;

                var from = GetEntranceLocation(startingDirection);
                var to = GetEntranceLocation(endingDirection);

                walkingSearch.Clear();
                visited.Clear();
                walkingSearch.Enqueue(from, 0);

                RoomPathRequirement? ladderedPath = null;
                var foundPathWithNoLadder = false;

                while (walkingSearch.Count > 0)
                {
                    var current = walkingSearch.Dequeue();
                    if (!visited.Add(current)) continue;
                    if (current.SamePoint(to))
                    {
                        ImmutableArray<ItemId> requirements = current.RequiresLadder ? [ItemId.Ladder] : [];
                        var path = new RoomPathRequirement(startingDirection, endingDirection, requirements);
                        if (!current.RequiresLadder)
                        {
                            paths.Add(path);
                            foundPathWithNoLadder = true;
                            break;
                        }

                        ladderedPath = path;
                    }

                    static IEnumerable<PathSearch> Neighbors(RoomTileMap map, PathSearch point)
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

                if (!foundPathWithNoLadder && ladderedPath != null)
                {
                    paths.Add(ladderedPath.Value);
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
            paths.ToImmutableArray(),
            flags,
            pushBlock,
            staircase,
            0);

        _requirementsCache[cacheId] = roomRequirements;
        return roomRequirements;
    }

    private static bool HasRequirementItem(ObjType type, out ItemId itemId)
    {
        itemId = type switch
        {
            ObjType.BlueGohma => ItemId.WoodArrow,
            ObjType.RedGohma => ItemId.WoodArrow,
            ObjType.Grumble => ItemId.Food,
            // ObjType.PondFairy -> ItemId.Recorder?
            ObjType.Digdogger1 => ItemId.Recorder,
            ObjType.Digdogger2 => ItemId.Recorder,
            _ => ItemId.None,
        };
        return itemId != ItemId.None;
    }
}
