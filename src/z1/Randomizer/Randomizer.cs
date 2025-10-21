using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.Intrinsics.X86;
using System.Text;
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
        var timer = Stopwatch.StartNew();

        _log.Write(nameof(Randomize), $"Starting dungeon randomization {state.Seed}.");
        try
        {
            RandomizeCore(overworld, state);
        }
        catch (Exception e)
        {
            _log.Error($"Dungeon randomization failed: {e}");
            throw;
        }

        _log.Write(nameof(Randomize), $"Finished dungeon randomization in {timer.Elapsed}.");
    }

    public static void RandomizeCore(GameWorld overworld, RandomizerState state)
    {
        // Randomizing a dungeon is done in multiple passes, some of those passes themselves having multiple passes.
        // Requirements:
        // - Stay as random as possible.
        //   - There should not be any direction bias: Any algorithm that makes mutations and enumerates directions,
        //     should use a randomized direction order. If they were always done in order, then in many situations
        //     it would bias the first direction in the list.
        //   - There should not be any dungeon order bias: Dungeons which are earlier in the enumeration should not
        //     have an increased or decreased chance of any specific attribute, including rooms or items. The dungeon
        //     order is not randomized because that could only cover up defects, the algorithm should itself enforce
        //     this.

        var dungeons = GetAllDungeons(overworld).ToArray();
        state.Initialize(dungeons);

        // 1. First a shape for each dungeon is determined. Nothing else is set inside the dungeons at this point.
        var shapes = dungeons
            .Select(t => (t, DungeonState.Create(t, state)))
            .ToArray();

        // 2. Next, using a shared pool of all rooms, fit rooms that have requirements (are "special") first.
        // This includes item rooms, transport entrances, and entrance rooms.
        // These must be fit in their own separate pass to ensure that earlier dungeons don't deplete them. And if
        // they're avoided and preserved for later, then once they're "normalized" there will be a heavy bias of them
        // inside the later dungeons.
        foreach (var (_, shape) in shapes) shape.FitSpecialRooms(state);

        foreach (var (dungeon, shape) in shapes)
        {
            using var logger = _log.CreateScopedFunctionLog(dungeon.UniqueId);
            logger.Enter($"Randomizing dungeon {dungeon.UniqueId}.");

            // 3. Fit all normal rooms.
            logger.Write($"{nameof(shape.FitNormalRooms)}...");
            shape.FitNormalRooms(state);

            logger.Write($"Shape:\n{shape.GetDebugDisplay()}");

            // A catch-all to ensure the above room fitting happened properly.
            shape.EnsureAllRoomsAreFit();
            shape.UpdateRoomIds();

            // Attach the transport hallways to each other. This must be done before adding doors, because this can make
            // doors in specific locations unneeded.
            logger.Write($"{nameof(shape.AttachTransportHallways)}...");
            shape.AttachTransportHallways(state);
            shape.UpdateRoomCoordinates();

            // Now that all rooms are fit and transports are connected, we the doors are fit.
            logger.Write($"{nameof(shape.FitDoors)}...");
            shape.FitDoors(state);

            // Because rooms come from all levels, doing this after intentionally re-normalizes the monster sets.
            logger.Write($"{nameof(shape.FitMonsters)}...");
            shape.FitMonsters(state);

            // And now that doors and monsters are fit, we know the item requirements needed to walk to each item room.
            logger.Write($"{nameof(shape.FitItems)}...");
            shape.FitItems(state);

            // Place them into the world over the original dungeons.
            var randomizedDungeon = new GameWorld(dungeon.World, shape.GetGameRooms().ToArray(), dungeon.Settings, dungeon.Name);
            foreach (var room in shape.GetGameRooms()) room.GameWorld = randomizedDungeon;
            overworld.World.SetWorld(randomizedDungeon, dungeon.Name);
        }

        // As a final pass, remove all now unused staircases and related handlers.
        foreach (var (_, shape) in shapes) shape.NormalizeRooms();

        // LINTER TODO:
        // Make sure each room only appears once.
        // All items are fit.
        // Ensure RequiredDoors and actual room doors line up.
        // Validate all cell.Point's are correct.
    }

    public static RoomRequirements GetRoomRequirements(GameRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);

        // This function operates on tile coordinate space but moves in block space.
        // Hit detection/etc are done in tile space, but
        // * We can drastically speed up this problem by moving twice the distance each time.
        // * When moving over water with the ladder, we can only move one blocks worth. So moving in block
        //   space already makes that check much easier.
        const int movementSize = 2;

        using var logger = _log.CreateScopedFunctionLog(room.UniqueId, level: LogLevel.Error);

        var cacheId = room.OriginalUniqueId?.ToString() ?? room.UniqueId;
        if (_requirementsCache.TryGetValue(cacheId, out var cached)) return cached;

        // For the above world, there's the two 2Q caves that require ladder, the fairy pond, and likely others.
        if (!room.HasUnderworldDoors || room.UnderworldDoors.Count == 1) return default;

        var paths = new Dictionary<DoorPair, PathRequirements>();

        static Point GetLocationInfrontOfEntrance(Direction direction)
        {
            const int blockToTileRatio = 2;
            return direction switch
            {
                Direction.Right => new Point(13 * blockToTileRatio, 5 * blockToTileRatio),
                Direction.Left => new Point(2 * blockToTileRatio, 5 * blockToTileRatio),
                Direction.Up => new Point(7 * blockToTileRatio, 2 * blockToTileRatio),
                Direction.Down => new Point(7 * blockToTileRatio, 8 * blockToTileRatio),
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
            logger.Write($"Behavior for door {direction} was {behavior}.");
            if (behavior is TileBehavior.GenericWalkable or TileBehavior.Sand)
            {
                validDirections |= direction;
            }
        }

        if (validDirections == Direction.None)
        {
            logger.Error("No valid directions found.");
            throw new Exception($"Room {room.UniqueId} has no valid directions?");
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
                : new DoorToDoorSearchPath(GetLocationInfrontOfEntrance(startingDirection), false);

            foreach (var endingDirection in Direction.DoorDirectionOrder)
            {
                if (endingDirection == startingDirection) continue;
                if (!validDirections.HasFlag(endingDirection)) continue;

                var doorPair = DoorPair.Create(startingDirection, endingDirection);
                if (paths.ContainsKey(doorPair)) continue;

                logger.Enter($"Searching path for {doorPair}...");

                if (room.UniqueId == "Level00_04/1,0") // && doorPair is { From: Direction.Right, To: Direction.Left })
                {
                }

                // This has to be infront of it, otherwise we bump into the wall in our path search.
                // We could alternatively check win conditions inside the next path enumeration code.
                var to = new DoorToDoorSearchPath(GetLocationInfrontOfEntrance(endingDirection), false);

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
                            paths.Add(doorPair, PathRequirements.None);
                            foundPathWithNoLadder = true;
                            logger.Write($"✅ Found path for {doorPair}.");
                            break;
                        }

                        hasLadderedPath = true;
                    }

                    static IEnumerable<DoorToDoorSearchPath> Neighbors(DoorToDoorSearchPath path)
                    {
                        yield return path with { X = path.X - movementSize };
                        yield return path with { X = path.X + movementSize };
                        yield return path with { Y = path.Y - movementSize };
                        yield return path with { Y = path.Y + movementSize };
                    }

                    var behavior = room.RoomMap.Behavior(current.X, current.Y);

                    foreach (var next in Neighbors(current))
                    {
                        if (!room.RoomMap.IsValid(next.X, next.Y)) continue;
                        if (visited.Contains(next)) continue;

                        var nextBehavior = room.RoomMap.Behavior(next.X, next.Y);
                        if (nextBehavior == TileBehavior.Water)
                        {
                            // The ladder can only cross a single water tile. Do not allow crossing multiple water tiles.
                            if (behavior == TileBehavior.Water)
                            {
                                logger.Write($"❌ Cannot move to {next} -- already in water.");
                                continue;
                            }

                            // If we've determined there's a path with a ladder, do not attempt additional ones.
                            if (hasLadderedPath)
                            {
                                logger.Write($"❌ Cannot move to {next} -- already have a laddered path.");
                                continue;
                            }

                            walkingSearch.Enqueue(next with { RequiresLadder = true }, to.Distance(next));
                            continue;
                        }

                        if (nextBehavior.CanWalk() || nextBehavior == TileBehavior.Door)
                        {
                            walkingSearch.Enqueue(next, to.Distance(next));
                            continue;
                        }

                        logger.Write($"❌ Cannot move to {next} -- behavior {nextBehavior}.");
                    }
                }

                if (!foundPathWithNoLadder && hasLadderedPath)
                {
                    paths.Add(doorPair, PathRequirements.Ladder);
                    logger.Write($"✅ Found path for {doorPair} requiring ladder.");
                }
            }
        }

        if (paths.Count == 0)
        {
            logger.Error("Did not connect to other rooms.");
            throw new Exception($"Room {room.UniqueId} can't connect to other rooms?");
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
            staircase);

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

internal readonly record struct DoorPair
{
    private DoorPair(Direction From, Direction To)
    {
        this.From = From;
        this.To = To;
    }

    public static DoorPair Create(Direction from, Direction to)
    {
        if (from == to) throw new Exception();

        // This does make the assumption that if you can go from A to B, that you can go from B to A.
        // I believe this to always be true.
        return from < to ? new DoorPair(from, to) : new DoorPair(to, from);
    }

    public override string ToString() => $"{From}→{To}";
    public Direction From { get; init; }
    public Direction To { get; init; }

    public void Deconstruct(out Direction from, out Direction to)
    {
        from = From;
        to = To;
    }
}

internal readonly record struct RoomRequirements(
    Direction ConnectableDirections, // Does not mean they _are_ connected, just that there's nothing hard blocking it from being connected.
    IReadOnlyDictionary<DoorPair, PathRequirements> Paths,
    RoomRequirementFlags Flags,
    InteractableBlockObject? PushBlock,
    InteractableBlockObject? Staircase)
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

    public static PathRequirements GetShutterRequirements(GameRoom room)
    {
        if (!room.UnderworldDoors.Any(static t => t.Value == DoorType.Shutter)) return PathRequirements.None;

        return GetRequirementItems(room.Monsters.Select(t => t.ObjType));
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
        return type.Aggregate(PathRequirements.None, static (current, t) => current | GetRequirementItem(t));
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
        public GameRoom DemandGameRoom => GameRoom ?? throw new Exception("Room not fit.");

        // If we fail during a fitting, rooms might end up being moved. Disallow the entrance room from being moved.
        public bool IsMovableRoom => Type is not RoomType.Entrance;
        public bool AllowsMonsters => Type is not RoomType.Entrance; // TODO: Nothing allowed in old man rooms.
        public bool IsFitItem => Item is not (ItemId.Compass or ItemId.Map or ItemId.TriforcePiece);
        public bool RequiresStaircase => Type is RoomType.ItemStaircase or RoomType.TransportStaircase;

        public override string ToString()
        {
            var room = GameRoom ?? throw new Exception("Room not fit.");
            return $"{room.Id} (point: {Point.X},{Point.Y}, type: {Type})";
        }
    }

    private const int _maxWidth = 8;
    private const int _maxHeight = 8;

    private static readonly DebugLog _log = new(nameof(DungeonState), DebugLogDestination.File);
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

    private IEnumerable<Point> EachValidPoint()
    {
        foreach (var point in EachPoint())
        {
            var cell = this[point];
            if (cell.Type == RoomType.None) continue;
            if (cell.Point != point) throw new Exception("Cell point mismatch.");
            yield return point;
        }
    }

    public IEnumerable<GameRoom> GetGameRooms()
    {
        return EachPoint().Select(t => this[t].GameRoom).Where(static t => t != null)!;
    }

    public IEnumerable<Cell> GetValidCells() => EachValidPoint().Select(t => this[t]);

    public static DungeonState Create(GameWorld world, RandomizerState state)
    {
        var stats = DungeonStats.Create(world);
        var (staircaseItemCount, floorItemCount, _) = stats;
        var rng = state.RoomRandom;
        using var logger = _log.CreateScopedFunctionLog(world.UniqueId);
        logger.Enter("Creating dungeon shape.");

        var hasCompass = state.Flags.Dungeon.AlwaysHaveCompass || rng.GetBool();
        var hasMap = state.Flags.Dungeon.AlwaysHaveMap || rng.GetBool();

        if (hasCompass) ++floorItemCount;
        if (hasMap) ++floorItemCount;
        ++floorItemCount; // Triforce.

        var sizeVariance = state.Flags.Dungeon.ShapesSizeVariance;
        var newSize = world.Rooms.Length + rng.Next(-sizeVariance, sizeVariance);
        var roomCount = 0;
        var entrance = new Point(rng.Next(0, _maxWidth), _maxHeight - 1);
        var roomType = RoomType.Entrance;
        var normalCells = new List<Point>();
        var restartsStat = 0;
        var iterationsStat = 0;
        var checkDirections = Direction.DoorDirectionOrder;

        var layout = new Cell[_maxWidth, _maxHeight];
        foreach (var point in EachPoint())
        {
            layout[point.X, point.Y] = new Cell(RoomType.None, point);
        }

        // It's possible for the random walk to not fill enough rooms, so we restart the walk until we do.
        while (roomCount < newSize)
        {
            ++restartsStat;

            logger.Write(
                $"Placing rooms" +
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
                    if (roomType == RoomType.Normal) normalCells.Add(current);
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
                if (normalCells.Count == 0) throw new Exception();
                var index = rng.Next(normalCells.Count);
                var cellPoint = normalCells[index];
                ref var cell = ref layout[cellPoint.X, cellPoint.Y];
                cell = cell with { Type = type };
                normalCells.RemoveAt(index);
            }
        }

        void SetRoomItemRandomly(RoomType type, ItemId item)
        {
            var cells = EachPoint()
                .Select(t => layout[t.X, t.Y])
                .Where(t => t.Type == type)
                .Where(t => t.Item == ItemId.None)
                .ToArray();
            if (cells.Length == 0) throw new Exception();

            var index = rng.Next(cells.Length);
            var randomCell = cells[index];
            var point = randomCell.Point;
            ref var cell = ref layout[point.X, point.Y];
            cell = cell with { Item = item };
            logger.Write($"{nameof(SetRoomItemRandomly)}: {item} in room at {point.X},{point.Y}.");
        }

        SetRoomTypeRandomly(staircaseItemCount, RoomType.ItemStaircase);
        SetRoomTypeRandomly(floorItemCount, RoomType.FloorDrop);

        if (hasCompass) SetRoomItemRandomly(RoomType.FloorDrop, ItemId.Compass);
        if (hasMap) SetRoomItemRandomly(RoomType.FloorDrop, ItemId.Map);
        SetRoomItemRandomly(RoomType.FloorDrop, ItemId.TriforcePiece);

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
            if (cell.GameRoom == null) throw new Exception($"Room at {cell.Point.X},{cell.Point.Y} not fit.");
        }
    }

    public void FitItems(RandomizerState state)
    {
        using var logger = _log.CreateScopedFunctionLog(Dungeon.UniqueId);
        for (var attempt = 0; attempt < 1000; ++attempt)
        {
            try
            {
                logger.Enter($"Fitting items attempt {attempt}.");

                FitItemsCore(state);
                return;
            }
            catch (RecoverableRandomizerException ex)
            {
                state.RerandomizeItemList();
                logger.Error($"Attempt {attempt} failed: {ex.Message}");
            }
        }

        throw logger.Fatal("Exceeded maximum item fitting attempts.");
    }

    private void FitItemsCore(RandomizerState state)
    {
        using var logger = _log.CreateScopedFunctionLog(Dungeon.UniqueId);

        ItemId FitItem(Cell cell)
        {
            if (state.DungeonItems.Count == 0)
            {
                throw logger.Fatal($"No more dungeon items to fit into {cell}.");
            }

            for (var i = 0; i < state.DungeonItems.Count; i++)
            {
                var item = state.DungeonItems[i];
                foreach (var requirements in TryWalkToRoom(EntranceLocation, cell.Point))
                {
                    if (!Randomizer.PathRequirementsAllows(requirements, item))
                    {
                        logger.Write($"❌ Cannot fit {item} in {cell} due to requirements {requirements}.");
                        continue;
                    }

                    state.DungeonItems.RemoveAt(i);
                    logger.Write($"✅ Fit {item} in {cell} with requirements {requirements}.");
                    return item;
                }

                throw new RecoverableRandomizerException($"Failed to fit {item} into {cell}");
            }

            // var requirementsList = TryWalkToRoom(EntranceLocation, cell.Point).ToArray();
            throw logger.Fatal($"Failed to fit any item into {cell}");
        }

        foreach (var cell in GetValidCells())
        {
            if (cell.Type != RoomType.ItemStaircase) continue;
            if (!cell.IsFitItem) continue;

            logger.Write($"Fitting RoomType.ItemStaircase in {cell}.");
            var room = cell.DemandGameRoom;
            var staircase = room.InteractableBlockObjects.FirstOrDefault(static t => t.Interaction.Entrance != null)
                ?? throw logger.Fatal($"No ItemStaircase found in room for cell {cell}.");

            var item = FitItem(cell);
            staircase.Interaction.Entrance = Entrance.CreateItemCellar(item, cell.Point.ToPointXY());
        }

        foreach (var cell in GetValidCells())
        {
            if (cell.Type != RoomType.FloorDrop) continue;
            if (!cell.IsFitItem) continue;

            logger.Write($"Fitting RoomType.FloorDrop in {cell} ({cell.Item}).");
            var room = cell.DemandGameRoom;
            var block = room.InteractableBlockObjects.FirstOrDefault(static t => t.Interaction.Item?.Item != null)
                ?? throw logger.Fatal($"No FloorDrop found in room for cell {cell}.");

            var item = cell.Item == ItemId.None ? FitItem(cell) : cell.Item;
            block.Interaction.Item = new RoomItem
            {
                Item = item,
                Options = ItemObjectOptions.IsRoomItem | ItemObjectOptions.MakeItemSound
            };
        }
    }

    public void UpdateRoomIds()
    {
        _hasIdsUpdated = true;

        foreach (var cell in GetValidCells())
        {
            var gameRoom = cell.DemandGameRoom;
            gameRoom.Id = $"{Dungeon.UniqueId}/{cell.Point.X},{cell.Point.Y} ({gameRoom.UniqueId})";
        }
    }

    public void AttachTransportHallways(RandomizerState state)
    {
        if (!_hasIdsUpdated) throw new Exception("Cannot attach transport hallways prior to updating IDs.");

        _hasTransportsAttached = true;
        if (Stats.TrainsportStairsCount == 0) return;

        using var logger = _log.CreateScopedFunctionLog(Dungeon.UniqueId);
        logger.Enter("Attaching transport staircases.");

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

            logger.Write($"Attaching {cellA} -> {cellB}");
        }

        if (cells.Count > 0) throw new UnreachableCodeException();
    }

    private readonly record struct TryWalkSearchPath(Point Point, PathRequirements Requirements, Direction EntryDirection);
    private readonly record struct TryWalkVisited(Point Point, PathRequirements Requirements, Direction EntryDirection);

    private bool CanWalkToRoom(Point start, Point destination, bool ignoreDoors = false)
    {
        foreach (var _ in TryWalkToRoom(start, destination, ignoreDoors)) return true;
        return false;
    }

    private IEnumerable<PathRequirements> TryWalkToRoom(Point start, Point destination, bool ignoreDoors = false)
    {
        if (!_hasTransportsAttached) throw new Exception($"{nameof(TryWalkToRoom)} was called prior to attaching transports.");
        if (!ignoreDoors && !_hasDoorsFit) throw new Exception($"{nameof(TryWalkToRoom)} was called prior to fitting doors.");

        // If we've visited a map before with identical set of requirements and entry point, then there's no reason to repeat it.
        var visited = new HashSet<TryWalkVisited>();
        var paths = new PriorityQueue<TryWalkSearchPath, long>();
        paths.Enqueue(new TryWalkSearchPath(start, PathRequirements.None, Direction.Down), 0);
        var seenRequirements = new HashSet<PathRequirements>();

        static long Distance(Point a, Point b, PathRequirements requirements)
        {
            // Drastically deprioritize paths with more requirements.
            var shift = (int)Popcnt.PopCount((uint)requirements);
            var distance = (long)(Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y));
            return distance << shift;
        }

        using var logger = _log.CreateScopedFunctionLog(Dungeon.UniqueId);

        logger.Enter($"Starting walk from {start.X},{start.Y} to {destination.X},{destination.Y} ({(ignoreDoors ? "ignoring doors" : "")}).");

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

        while (paths.TryDequeue(out var current, out _))
        {
            if (current.Point == destination)
            {
                // Only report back unique requirement sets.
                if (seenRequirements.Add(current.Requirements))
                {
                    logger.Write($"✅ Can walk to {destination.X},{destination.Y} with requirements {current.Requirements}.");
                    yield return current.Requirements;

                    // There _should be_ no need to continue on.
                    if (current.Requirements == PathRequirements.None) yield break;
                }
                continue;
            }

            if (!visited.Add(new TryWalkVisited(current.Point, current.Requirements, current.EntryDirection))) continue;

            var cell = this[current.Point];
            if (cell.Type == RoomType.None) continue;
            var room = cell.GameRoom ?? throw new Exception("Room not fit.");

            // Add the other end of a transport staircase to toVisit.
            if (TryGetTransportExit(cell.GameRoom, out var transportExit))
            {
                var transportRequirements = current.Requirements | RoomRequirements.GetStairRequirements(room);
                paths.Enqueue(
                    new TryWalkSearchPath(transportExit, transportRequirements, Direction.None),
                    Distance(transportExit, destination, transportRequirements));
            }

            var roomRequirements = Randomizer.GetRoomRequirements(room);

            foreach (var direction in Direction.DoorDirectionOrder)
            {
                var next = current.Point + direction.GetOffset();
                if (!IsValidPoint(next)) continue;
                if (current.EntryDirection == direction) continue;
                if (this[next].Type == RoomType.None) continue;

                if (!ignoreDoors && !cell.RequiredDoors.HasFlag(direction))
                {
                    logger.Write($"❌ {room.Id} cannot go {direction} due to missing cell.RequiredDoors.");
                    continue;
                }

                if (!roomRequirements.ConnectableDirections.HasFlag(direction))
                {
                    logger.Write($"❌ {room.Id} cannot go {direction} due to missing roomRequirements.ConnectableDirections.");
                    continue;
                }

                var doorPair = DoorPair.Create(current.EntryDirection, direction);
                if (!roomRequirements.Paths.TryGetValue(doorPair, out var pathRequirements))
                {
                    // This can happen in the case of T rooms. Southern door enters to a non-crossable water-room,
                    // but the other 3 walls can reach each other.
                    logger.Write($"❌ {room.Id} cannot move {doorPair} due to missing roomRequirements.Paths for {doorPair}. T room?");
                    continue;
                }

                var requirements = current.Requirements
                    | RoomRequirements.GetShutterRequirements(room)
                    | pathRequirements;

                logger.Write($"➡️ From {room.Id} going {direction}->{next.X},{next.Y} with requirements {requirements}.");
                paths.Enqueue(
                    new TryWalkSearchPath(next, requirements, direction.GetOppositeDirection()),
                    Distance(next, destination, requirements));
            }
        }
    }

    public void FitDoors(RandomizerState state)
    {
        void ShuffleAllRooms(Random rng)
        {
            var cells = new Stack<Cell>(GetValidCells().Where(static t => t.IsMovableRoom).Shuffle(rng));
            foreach (var point in EachValidPoint())
            {
                ref var cell = ref this[point];
                if (!cell.IsMovableRoom) continue;

                cell = cells.Pop() with { Point = point };
            }
        }

        // Arg. I have no idea what to do on this retry attempts.
        using var logger = _log.CreateScopedFunctionLog(Dungeon.UniqueId);
        for (var attempt = 0; attempt < 1_000; attempt++)
        {
            var rng = state.CreateDoorRandom(Dungeon.Settings.LevelNumber);
            logger.Enter($"Fitting doors attempt {attempt}.");
            try
            {

                FitDoorsCore(state, rng);
                return;
            }
            catch (RecoverableRandomizerException ex)
            {
                logger.Error($"Attempt {attempt} failed: {ex.Message}");
                ShuffleAllRooms(rng);
            }
        }

        throw logger.Fatal("Exhausted door fitting attempts...");
    }

    private void FitDoorsCore(RandomizerState state, Random rng)
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
        var directions = Direction.DoorDirectionOrder.ToArray();
        using var logger = _log.CreateScopedFunctionLog(Dungeon.UniqueId);

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

        void ClearDoorStates()
        {
            foreach (var room in GetGameRooms()) room.UnderworldDoors.Clear();

            foreach (var point in EachValidPoint())
            {
                ref var cell = ref this[point];
                cell = cell with { RequiredDoors = Direction.None };
            }
        }

        void AddRequiredDoor(Point location, Direction direction)
        {
            ref var cell = ref this[location];
            cell = cell with { RequiredDoors = cell.RequiredDoors | direction };

            // This can OOB, but it should always be valid, or we've made an invalid map elsewhere.
            var adjoiningPoint = location + direction.GetOffset();
            ref var adjoining = ref this[adjoiningPoint];
            adjoining = adjoining with { RequiredDoors = adjoining.RequiredDoors | direction.GetOppositeDirection() };
        }

        // We're going to mark-sweep this. The mark phase is clearing out all the doors on GameRooms.
        // We might also need to try and FitDoors again since the door fitting phase is one of the largest validation
        // passes of the dungeon so far.
        // This matters because we need connecting rooms to have the same door type. We'll
        // set them as we generate the main room, so once we get to them, we'll see they have a door there and skip.
        ClearDoorStates();

        // Walk randomly from the entrance to each room and place doors randomly. This does not yet create
        // a valid dungeon layout.
        var visited = new HashSet<Point>();
        var toVisit = new ValueStack<Point>(EntranceLocation);
        while (toVisit.TryPop(out var location))
        {
            if (!visited.Add(location)) continue;
            ref var cell = ref this[location];
            var loopCounter = 0;

            var requirements = Randomizer.GetRoomRequirements(cell.DemandGameRoom);
            if (requirements.ConnectableDirections == Direction.None)
            {
                logger.Write(GetDebugDisplay());
                throw logger.Fatal($"Room {cell} has no connectable directions.");
            }

            var connectableCount = Popcnt.PopCount((uint)requirements.ConnectableDirections);
            if (connectableCount == 1)
            {
                var oneDirection = requirements.ConnectableDirections;
                logger.Write($"Room {cell} has only one connectable direction ({oneDirection}).");

                AddRequiredDoor(location, oneDirection);
                continue;
            }

            var canConnect = new List<(Point Location, Direction Direction)>();
            foreach (var (nextLocation, direction) in GetAdjoiningRooms(location))
            {
                // I _think_ pushing this prior to other checks is the correct move?
                toVisit.Push(nextLocation);

                // Likely added from a connecting room.
                if (cell.RequiredDoors.HasFlag(direction)) continue;

                var canWalkToRoom = CanWalkToRoom(EntranceLocation, nextLocation, true);
                if (canWalkToRoom)
                {
                    canConnect.Add((nextLocation, direction));
                }
            }

            if (canConnect.Count == 0)
            {
                logger.Write($"Room {cell} had no connectable connections...");
                continue;
            }

            // Fit doors until we have added at least one.
            var addedDoorCount = 0;
            do
            {
                logger.Write($"Checking {cell} Type:{cell.Type}, RequiredDoors:{cell.RequiredDoors}, loopCounter:{loopCounter}, addedDoorCount:{addedDoorCount}");

                foreach (var (_, direction) in canConnect)
                {
                    // TODO: Figure out a probability here that isn't 50/50?
                    if (rng.GetBool())
                    {
                        logger.Write($"Could walk {direction}, but randomized no door.");
                        continue;
                    }

                    // cell = cell with { RequiredDoors = cell.RequiredDoors | direction };
                    // ref var adjoining = ref this[nextLocation];
                    // adjoining = adjoining with { RequiredDoors = adjoining.RequiredDoors | direction.GetOppositeDirection() };
                    AddRequiredDoor(location, direction);
                    logger.Write($"Added {direction}.");
                    ++addedDoorCount;
                }
                ++loopCounter;
            } while (cell.Type != RoomType.TransportStaircase
                && loopCounter < 100
                && addedDoorCount == 0);

            if (cell.Type != RoomType.TransportStaircase && addedDoorCount == 0)
            {
                logger.Write(GetDebugDisplay());
                throw logger.Fatal($"Room {cell.DemandGameRoom} has no required doors after walk.");
            }
        }

        bool AddDoorAtRandom()
        {
            var points = EachValidPoint().Shuffle(rng);
            foreach (var point in points)
            {
                ref var cell = ref this[point];
                var room = cell.DemandGameRoom;
                var requirements = Randomizer.GetRoomRequirements(room);
                foreach (var adjoining in GetAdjoiningRooms(cell.Point))
                {
                    if ($"Randomly added door {adjoining.Direction} to {cell}." == "Randomly added door Right to Level00_08/3,6 (Level00_05/5,2) (point: 3,6, type: Normal).")
                    {
                    }
                    // Already has a door there.
                    if (cell.RequiredDoors.HasFlag(adjoining.Direction)) continue;
                    // Can't have a good there.
                    if (!requirements.ConnectableDirections.HasFlag(adjoining.Direction)) continue;

                    if ($"Randomly added door {adjoining.Direction} to {cell}." == "Randomly added door Right to Level00_08/3,6 (Level00_05/5,2) (point: 3,6, type: Normal).")
                    {
                    }

                    ref var cellx = ref this[point];
                    AddRequiredDoor(cell.Point, adjoining.Direction);
                    ref var cellxx = ref this[point];
                    logger.Write($"Randomly added door {adjoining.Direction} to {cell}.");
                    return true;
                }
            }

            return false;
        }

        bool TryWalkToAllRooms()
        {
            foreach (var cell in GetValidCells())
            {
                if (!CanWalkToRoom(EntranceLocation, cell.Point))
                {
                    logger.Write($"❌ Room at {cell} is not reachable from entrance.");
                    return false;
                }
            }
            return true;
        }

        // Our above algorithm doesn't ensure the level is correct. It added doors randomly. Lets go room by room to
        // check that we can reach them. If we can't, lets add one random door to the level until they're all reachable.
        // I'm not in-love with this solution. Something that works on the potential path between the unconnected room
        // and the entrance would likely result in less restart loops, and perhaps a better (more interesting) dungeon
        // layout? But I don't feel it would be "random" enough -- bias in the path selection would lead to bias in the
        // dungeon layout.
        bool RefitDoorsUntilWalkable()
        {
            var attempts = 0;
            for (; attempts < 1_0000000000; attempts++)
            {
                if (TryWalkToAllRooms()) break;

                if (!AddDoorAtRandom())
                {
                    return false;
                }

                logger.Write(GetDebugDisplay());
                logger.Write($"⬆️ Attempting dungeon walk again {attempts}...");
                ++attempts;
            }

            logger.Write($"✅ Made dungeon walkable in {attempts} attempts.");
            return true;
        }

        if (!RefitDoorsUntilWalkable())
        {
            logger.Write(GetDebugDisplay());
            throw new RecoverableRandomizerException("Unable to add more doors to make all rooms reachable.");
        }

        // 2. Walk the rooms and fit doors according to our probabilities.
        foreach (var point in EachPoint())
        {
            var cell = this[point];
            if (cell.Type == RoomType.None) continue;
            var room = cell.DemandGameRoom;

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

    public void FitMonsters(RandomizerState state)
    {
        foreach (var cell in GetValidCells())
        {
            if (!cell.AllowsMonsters) continue;

            var room = cell.DemandGameRoom;
            room.Monsters = state.GetRoomMonsters(room);
        }
    }

    public void NormalizeRooms()
    {
        var toremove = new List<InteractableBase>();

        void RemoveStaircase(GameRoom room)
        {
            toremove.Clear();

            // Always strip entrances. We've used all the entrances we need by this point.
            // There shouldn't be any left, but you know.
            room.Settings.Options &= ~RoomFlags.IsEntrance;

            foreach (var obj in room.InteractableBlockObjects)
            {
                if (obj.Interaction.Entrance == null) continue;

                // Strip all stairs and interactions that reveal those stairs.
                toremove.Add(obj.Interaction);

                if (obj.Interaction.Interaction == Interaction.Revealed)
                {
                    toremove.Add(room.GetRevealer(obj.Interaction));
                }
            }

            if (toremove.Count > 0)
            {
                room.InteractableBlockObjects = room.InteractableBlockObjects
                    .Where(t => !toremove.Contains(t.Interaction))
                    .ToImmutableArray();
            }
        }

        foreach (var cell in GetValidCells())
        {
            // if (!cell.RequiresStaircase) RemoveStaircase(cell.DemandGameRoom);
        }
    }

    // Update the world entry coordinates. This is used by GameWorld to determine the grid.
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
        static ReadOnlySpan<char> HexChars() => "0123456789ABCDEF";

        const int displayGridCharCount = _maxWidth * _maxHeight + _maxWidth;
        var sb = new StringBuilder(displayGridCharCount * 2 + 100);

        sb.AppendLine($"Map of {Dungeon.UniqueId}");
        sb.Append("   ");
        for (var i = 0; i < _maxWidth; i++) sb.Append(i);
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

            sb.Append("   ");

            for (var x = 0; x < _maxWidth; x++)
            {
                sb.Append(HexChars()[(int)this[new Point(x, y)].RequiredDoors]);
            }

            sb.AppendLine();
        }
        return sb.ToString();
    }
}