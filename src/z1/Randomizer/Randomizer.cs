using System;
using System.Collections.Immutable;
using System.Text;

namespace z1.Randomizer;

// Randomized dungeon shape passes:
// 1. Create a shape with the right number of rooms, and mark special rooms (entrance, item staircase, floor drop).
// 2. Fit rooms with specific requirements first -- such as rooms that have items. This is to ensure rooms without
//    criteria do not pre-emptively drain them from the pool.
// 3. Fit the rest of the rooms.
// 4. Mark the requirements needed for each item room. IE, if the dungeon entrance requires a ladder to get to, and
//    the room item is a drop but requires a recorder, those 2 are items are marked as requirements.
// 5. Fit items to the dungeon rooms now that requirements are known.

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
    int Difficulty)
{
    public bool HasStaircase => Flags.HasFlag(RoomRequirementFlags.HasStaircase);
    public bool HasFloorDrop => Flags.HasFlag(RoomRequirementFlags.HasFloorDrop);
    public bool HasPushBlock => Flags.HasFlag(RoomRequirementFlags.HasPushBlock);
    public bool IsEntrance => Flags.HasFlag(RoomRequirementFlags.IsEntrance);
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

internal readonly record struct DungeonStats(int StaircaseItemCount, int FloorItemCount, Dictionary<DoorType, int> DoorTypeCounts)
{
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
}

internal readonly record struct DungeonShape(DungeonShape.Room[,] Layout, DungeonStats Stats)
{
    internal enum RoomType { None, Normal, Entrance, FloorDrop, ItemStaircase }
    internal readonly record struct Room(RoomType Type, ItemId Item = ItemId.None, GameRoom? GameRoom = null);

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

        return new DungeonShape(layout, stats);
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
            // if (cell.Type is not (RoomType.FloorDrop or RoomType.ItemStaircase)) continue;

            var requiredDirections = Direction.None;
            if (IsValidPoint(point + new Point(-1, 0))) requiredDirections |= Direction.Left;
            if (IsValidPoint(point + new Point(1, 0))) requiredDirections |= Direction.Right;
            if (IsValidPoint(point + new Point(0, -1))) requiredDirections |= Direction.Up;
            if (IsValidPoint(point + new Point(0, 1))) requiredDirections |= Direction.Down;

            // Find a room that meets the criteria.
            for (var i = 0; i < state.RandomDungeonRoomList.Count && cell.GameRoom == null; i++)
            {
                var room = state.RandomDungeonRoomList[i];
                var requirements = Randomizer.GetPathRequirements(room);

                // TODO: This isn't _really_ what is wanted. We want to ensure all paths are accessible, but we don't
                // super care that all possible paths in all rooms are connected. IE, it's possible that a tunnel
                // or alternative path on foot connect them.
                // This also may drain valid rooms and only leave a room with a single direction connection that is
                // not valid for the spot.
                if ((requiredDirections & requirements.ConnectableDirections) != requiredDirections) continue;

                if (fit(cell, requirements))
                {
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
            cell => cell.Type is RoomType.FloorDrop or RoomType.ItemStaircase,
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

    private static readonly DoorType[] _significantDoorTypes = [DoorType.Open, DoorType.Key, DoorType.Bombable, DoorType.FalseWall];

    public void RandomizeUnderworldDoors(RandomizerState state)
    {
        var rng = state.RoomRandom;

        // We're going to mark-sweep this. The mark phase is clearing out all of the doors.
        // This matters because we need connecting rooms to have the same door type. We'll
        // set them as we generate the main room, so once we get to them, we'll see they have a door there and skip.
        foreach (var room in GetRooms()) room.UnderworldDoors.Clear();

        var stats = Stats;
        var totalDoorCount = _significantDoorTypes.Sum(t => stats.DoorTypeCounts.GetValueOrDefault(t, 0));
        if (totalDoorCount == 0) throw new Exception("The dungeon has no doors.");

        var probabilities = new List<(int Probability, DoorType Type)>();
        var runningCount = 0;
        foreach (var type in _significantDoorTypes)
        {
            var count = stats.DoorTypeCounts.GetValueOrDefault(type, 0);
            runningCount += count;
            probabilities.Add((runningCount, type));
        }
        var orderedProbabilities = probabilities.OrderBy(t => t.Probability).ToArray();

        foreach (var room in GetRooms())
        {
            foreach (var direction in Direction.DoorDirectionOrder)
            {
                // It was set already from a connecting room.
                if (room.UnderworldDoors.ContainsKey(direction)) continue;

                if (!room.Connections.TryGetValue(direction, out var connected))
                {
                    room.UnderworldDoors[direction] = DoorType.Wall;
                    continue;
                }

                var doorRng = rng.Next(totalDoorCount);
                var doorType = orderedProbabilities.First(t => t.Probability >= doorRng).Type;
                room.UnderworldDoors[direction] = doorType;

                // Keys are always open on the other side. Or are they?
                var connectingDoorType = doorType == DoorType.Key ? DoorType.Open : doorType;
                connected.UnderworldDoors[direction.GetOppositeDirection()] = connectingDoorType;
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

    internal enum RandomRoomType { None, Normal, Entrance, FloorDrop, ItemStaircase }

    internal readonly record struct RandomizedRoom(int X, int Y, Direction Connections, RandomRoomType Type);

    private static readonly Dictionary<string, RoomRequirements> _requirementsCache = new();

    private static void OverworldRoom()
    {
    }

    private static IEnumerable<GameWorld> GetAllDungeons(GameWorld overworld)
    {
        foreach (var room in overworld.Rooms)
        {
            foreach (var interactable in room.InteractableBlockObjects)
            {
                var entrance = interactable.Interaction.Entrance;
                if (entrance == null) continue;
                if (entrance.DestinationType != GameWorldType.Underworld) continue;

                yield return overworld.World.GetWorld(GameWorldType.Underworld, entrance.Destination);
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

    public static void Randomize(GameWorld overworld, RandomizerState state)
    {
        foreach (var dungeon in GetAllDungeons(overworld))
        {
            state.RandomDungeonRoomList.AddRange(dungeon.Rooms);
        }

        Shuffle(state.RandomDungeonRoomList, state.RoomListRandom);

        var shapes = GetAllDungeons(overworld)
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
            shape.RandomizeUnderworldDoors(state);
            foreach (var room in shape.GetRooms()) room.GameWorld = randomizedDungeon;
            overworld.World.SetWorld(randomizedDungeon, dungeon.Name);
        }
    }

    public static void CreateDungeonShape(GameWorld world, RandomizerState state)
    {
        var shape = DungeonShape.Create(world, state);
    }

    public static RoomRequirements GetPathRequirements(GameRoom room)
    {
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
            // Stairways only mark that spot -- but I believe it's incorrect that they do?
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
        foreach (var obj in room.ObjectLayer.Objects)
        {
            switch (obj)
            {
                case TileBehaviorGameMapObject { TileBehavior: TileBehavior.Stairs or TileBehavior.SlowStairs }:
                case InteractableBlockObject { Interaction.Entrance: not null }:
                    flags |= RoomRequirementFlags.HasStaircase;
                    break;
                case InteractableBlockObject { Interaction.Item: not null }:
                    flags |= RoomRequirementFlags.HasFloorDrop;
                    break;
                case InteractableBlockObject { Interaction.Interaction: Interaction.Push }:
                    flags |= RoomRequirementFlags.HasPushBlock;
                    break;
            }
        }

        if (room == room.GameWorld.EntryRoom)
        {
            flags |= RoomRequirementFlags.IsEntrance;
        }

        var roomRequirements = new RoomRequirements(
            validDirections,
            paths.ToImmutableArray(),
            flags,
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
