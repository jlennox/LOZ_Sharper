using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using z1.IO;

namespace z1.Randomizer;

// Known issues:
// * 9 isn't randomized?
// * Make 9 entrance always have the triforce block above it and no rooms to the left/right.

internal sealed class Randomizer
{
    private static readonly DebugLog _log = new(nameof(Randomizer));

    public static void Create(GameWorld overworld, RandomizerState state)
    {
        var timer = Stopwatch.StartNew();

        _log.Write(nameof(Create), $"Starting dungeon randomization {state.Seed}.");
        try
        {
            CreateCore(overworld, state);
        }
        catch (Exception e)
        {
            _log.Error($"Dungeon randomization failed: {e}");
            throw;
        }

        _log.Write(nameof(Create), $"Finished dungeon randomization in {timer.Elapsed}.");
    }

    public static void CreateCore(GameWorld overworld, RandomizerState state)
    {
        // Randomizing a dungeon is done in multiple passes. Some passes themselves having multiple passes.
        //
        // Requirements:
        // - Stay as random as possible.
        //   - There should not be any direction bias: Any algorithm that makes mutations while enumerating directions
        //     should use a randomized direction order. If they were always done in the same order then many situations
        //     would bias the first direction in the list.
        //   - There should not be any dungeon order bias: Dungeons earlier in the enumeration should not have an
        //     increased or decreased chance of any specific attribute, including rooms or items. The dungeon order is
        //     not randomized because that could only cover up defects, the algorithm should itself enforce this.
        //   - Unfortunately, staying as random as possible may result in an invalid layout.
        //     In this event, mutations should be random. It's possible that even progressing to the most permissive
        //     state it still remains unsolvable. In this case, the randomizer should backup and try again.
        //     - An example of this is adding doors until the dungeon is walkable. It's possible for a given floor
        //       layout for the entire dungeon to never be walkable regardless of the doors. Thusly, the randomizer
        //       must step back and change the room layout then place doors again. "T" rooms are notoriously bad.
        // Randomizer Stability:
        // - I'm not sure how I feel about this one. It matters in a product that is 1) mature, 2) used. This is
        //   unlikely to ever be either.
        // - _Some_ stability can be given by segmenting the rng's for different parts of the randomization. I don't
        //   know how granular that should get. But it would be nice if the algorithms changes, for example, only
        //   changed item layouts but not room layouts. It's obviously not real stability, but it's something.
        //
        // Notes:
        // - The retry attempt counts are largely arbitrary. I'm not sure it matters. My main concerns are preventing
        //   infinite loops, but more so, detecting failed states.

        var dungeons = GetAllDungeons(overworld).ToArray();
        state.Initialize(dungeons);

        // 1. First a shape for each dungeon is determined. Nothing else is set inside the dungeons at this point.
        var shapes = dungeons
            .Select(t => DungeonState.Create(t, state))
            .ToArray();

        // 2. Using the shared pool of all rooms, fit rooms that have requirements (are "special") first.
        // This includes item rooms, transport entrances, and entrance rooms.
        // These must be fit in their own separate pass to ensure that earlier dungeons don't deplete them. And if
        // they're avoided and preserved for later, then once they're "normalized" there will be a heavy bias of them
        // inside the later dungeons.
        foreach (var shape in shapes) shape.FitSpecialRooms(state);

        // TODO: Build another pass where all rooms that don't have ValidDoors == All are put into a list. Then all
        // shape parts where they could possibly fit without issue (ie, Princess room can't be on a bottom wall) are
        // put into a shuffled list, and they are fit at random. This will prevent down stream issues from happening.
        // Or just ensure they fit during normal room fitting?

        foreach (var shape in shapes)
        {
            var dungeon = shape.Dungeon;
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
        foreach (var shape in shapes) shape.NormalizeRooms();

        Lint(shapes, state);
        PrintSpoiler(shapes);
    }

    private static void Lint(IEnumerable<DungeonState> shapes, RandomizerState state)
    {
        // Make sure all rooms were only used once.
        var seenRooms = new HashSet<string>();

        // Make sure all items were only used once.
        var seenItems = new HashSet<ItemId>();

        foreach (var shape in shapes.OrderBy(t => t.Dungeon.Settings.LevelNumber))
        {
            var dungeon = shape.Dungeon;
            using var logger = _log.CreateScopedFunctionLog(dungeon.UniqueId);

            foreach (var room in shape.GetGameRooms())
            {
                if (!seenRooms.Add(room.UniqueId)) throw logger.Fatal($"Room {room.UniqueId} appears multiple times.");

                foreach (var item in room.InteractableBlockObjects
                    .Select(t => t.Interaction.Entrance?.Arguments?.ItemId)
                    .Where(DungeonStats.IsDungeonItem)
                    .Cast<ItemId>())
                {
                    if (!seenItems.Add(item))
                    {
                        throw logger.Fatal($"Item {item} appears multiple times.");
                    }
                }
            }

            foreach (var cell in shape.GetValidCells())
            {
                var room = cell.DemandGameRoom;

                // Ensure the Point stored on each cell is correct.
                var actualCell = shape[cell.Point];
                if (cell != actualCell) throw logger.Fatal($"Cell mismatch at {cell.Point}: expected {cell}, got {actualCell}.");

                // Ensure RequiredDoors and actual room doors line up.
                var actualDoors = room.UnderworldDoors
                    .Where(static t => t.Value is not (DoorType.Wall or DoorType.None))
                    .BitwiseOr(t => t.Key.ToEntrance());
                var missingDoors = cell.RequiredDoors & actualDoors;
                if (missingDoors != RoomEntrances.None)
                {
                    throw logger.Fatal($"Room {room.UniqueId} is missing required doors {missingDoors} for cell {cell}.");
                }
            }
        }

        // TODO: Arg, a lot of this is incorrect because they can end up on the overworld :)
        void LintState()
        {
            using var logger = _log.CreateScopedFunctionLog("RandomizerStateLint");

            // All items stored by the randomizer state must be placed.
            if (state.DungeonItems.Count > 0)
            {
                throw logger.Fatal($"Not all items were placed: " + string.Join(", ", state.DungeonItems));
            }

            // Double check that all items from AllDungeonItems are, infact, in the dungeons.
            foreach (var item in DungeonStats.AllDungeonItems)
            {
                if (!seenItems.Contains(item))
                {
                    throw _log.Fatal($"Item {item} was not placed into any dungeon.");
                }
            }
        }

        LintState();
    }

    private static void PrintSpoiler(IEnumerable<DungeonState> shapes)
    {
        foreach (var shape in shapes.OrderBy(t => t.Dungeon.Settings.LevelNumber))
        {
            var dungeon = shape.Dungeon;
            using var logger = _log.CreateScopedFunctionLog(dungeon.UniqueId);
            logger.Enter($"Dungeon spoilers for {dungeon.UniqueId}.");
            logger.Write($"✅ {shape.GetDebugDisplay()}");
            foreach (var item in shape.DungeonItems)
            {
                logger.Write($"✅ {item}");
            }
        }
    }

    // TODO: Move this off to something that modifies Game objects.
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
}

internal record OverworldState(GameWorld Overworld, OverworldState.Cell[,] Layout, ImmutableArray<DungeonState> Dungeons, Point EntranceLocation)
{
    internal readonly record struct Cell
    {
        public Point Point { get; init; }
        public GameRoom GameRoom { get; init; }
        public RoomType RoomType { get; init; }
        public string? LevelName { get; init; }

        public bool HasRoomItem => _roomItem != null;

        private readonly RoomItem? _roomItem;

        public Cell(Point point, GameRoom room)
        {
            Point = point;
            GameRoom = room;

            _roomItem = GameRoom.InteractableBlockObjects
                .Select(static t => t.Interaction.Item)
                .SingleOrDefault(static t => t != null);
        }

        public void SetItem(ItemId itemId)
        {
            var roomItem = _roomItem ?? throw new Exception("No item object to set item on.");
            roomItem.Item = itemId;
        }

        public override string ToString() => $"{GameRoom.UniqueId} (point: {Point.X},{Point.Y})";
    }

    internal enum RoomType { Normal, Cave, DungeonCave }

    private const int _maxWidth = 16;
    private const int _maxHeight = 8;

    private static readonly DebugLog _log = new(nameof(OverworldState), DebugLogDestination.File);

    public ref Cell this[Point i] => ref Layout[i.X, i.Y];
    private bool _hasFitItems = false;
    private bool _hasFitDungeonEntrances = false;

    private static IEnumerable<Point> EachPoint() => Point.FromRange(_maxWidth, _maxHeight);

    public static OverworldState Create(GameWorld overworld, ImmutableArray<DungeonState> dungeons, RandomizerState state)
    {
        static Cell[,] GetRoomGrid(GameWorld overworld)
        {
            static bool IsTopLeftRoom(GameRoom room)
            {
                var connections = room.Connections;
                return connections.Count == 2
                    && connections.ContainsKey(Direction.Right)
                    && connections.ContainsKey(Direction.Down);
            }

            var layout = new Cell[_maxWidth, _maxHeight];
            // Since grid style coordinates are not baked into the game engine, determine the top left room so we have a
            // known entry point (0, 0) and walk the connections from there.
            var topLeftRoom = overworld.Rooms.Single(IsTopLeftRoom);
            var visited = new HashSet<GameRoom>(_maxWidth * _maxHeight);
            var path = new Stack<(GameRoom Room, Point Point)> { (topLeftRoom, new Point(0, 0)) };
            while (path.TryPop(out var entry))
            {
                if (!visited.Add(entry.Room)) continue;
                layout[entry.Point.X, entry.Point.Y] = new Cell(entry.Point, entry.Room);

                foreach (var connection in entry.Room.Connections)
                {
                    var nextPoint = entry.Point + connection.Key.GetOffset();
                    path.Push((connection.Value, nextPoint));
                }
            }

            return layout;
        }

        var rng = state.OverworldMapRandom;
        var layout = GetRoomGrid(overworld);
        var entranceLocation = new Point(rng.Next(_maxWidth), rng.Next(_maxHeight));

        return new OverworldState(
            overworld,
            layout,
            dungeons,
            entranceLocation);
    }

    private void CanWalkTo(Point start, Point end)
    {
    }

    public void FitItems(RandomizerState state)
    {
        _hasFitItems = true;
    }

    public void FitDungeonEntrances(RandomizerState state)
    {
        if (!_hasFitItems) throw new Exception("Must fit items first.");
        _hasFitDungeonEntrances = true;
    }

    public void FitCaveEntrances(RandomizerState state)
    {
        if (!_hasFitDungeonEntrances) throw new Exception("Must fit dungeon entrances first.");
    }

    public void RandomizeStores(RandomizerState state)
    {
    }
}

internal readonly record struct DungeonItem(ItemId Item, Point Location, PathRequirements Requirements)
{
    public override string ToString() => $"{Item} at ({Location.X},{Location.Y}) requires {Requirements}.";
}

internal record DungeonState(GameWorld Dungeon, DungeonState.Cell[,] Layout, DungeonStats Stats, Point EntranceLocation)
{
    internal enum RoomType { None, Normal, Entrance, FloorDrop, ItemStaircase, TransportStaircase }
    internal readonly record struct Cell(
        RoomType Type,
        Point Point,
        ItemId Item = ItemId.None,
        GameRoom? GameRoom = null,
        RoomEntrances RequiredDoors = RoomEntrances.None)
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
            var roomDescription = GameRoom?.UniqueId ?? "No room";
            return $"{roomDescription} (point: {Point.X},{Point.Y}, type: {Type})";
        }
    }

    private const int _maxWidth = 8;
    private const int _maxHeight = 8;

    private static readonly DebugLog _log = new(nameof(DungeonState), DebugLogDestination.File);
    private static bool IsValidPoint(Point p) => p.X is >= 0 and < _maxWidth && p.Y is >= 0 and < _maxHeight;

    public ImmutableArray<DungeonItem> DungeonItems { get; private set; } = [];

    public ref Cell this[Point i] => ref Layout[i.X, i.Y];
    private bool _hasTransportsAttached;
    private bool _hasIdsUpdated;
    private bool _hasDoorsFit;

    private static IEnumerable<Point> EachPoint() => Point.FromRange(_maxWidth, _maxHeight);

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
                var cellPoint = normalCells.PopRandomly(rng);
                ref var cell = ref layout[cellPoint.X, cellPoint.Y];
                cell = cell with { Type = type };
            }
        }

        void SetRoomItemRandomly(RoomType type, ItemId item)
        {
            var emptyItemCells = EachPoint()
                .Select(t => layout[t.X, t.Y])
                .Where(t => t.Type == type)
                .Where(t => t.Item == ItemId.None)
                .ToArray();
            if (emptyItemCells.Length == 0) throw new Exception();

            var index = rng.Next(emptyItemCells.Length);
            var point = emptyItemCells[index].Point;
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
        using var logger = _log.CreateScopedFunctionLog(Dungeon.UniqueId);
        foreach (var point in EachPoint())
        {
            ref var cell = ref this[point];
            if (cell.GameRoom != null) continue; // Already set.
            if (cell.Type == RoomType.None) continue;
            if (!shouldFit(cell)) continue;

            var adjoiningRooms = RoomEntrances.None;
            if (IsValidPoint(point + new Point(-1, 0))) adjoiningRooms |= RoomEntrances.Left;
            if (IsValidPoint(point + new Point(1, 0))) adjoiningRooms |= RoomEntrances.Right;
            if (IsValidPoint(point + new Point(0, -1))) adjoiningRooms |= RoomEntrances.Top;
            if (IsValidPoint(point + new Point(0, 1))) adjoiningRooms |= RoomEntrances.Bottom;

            // Find a room that meets the criteria.
            for (var i = 0; i < state.RandomDungeonRoomList.Count && cell.GameRoom == null; i++)
            {
                var room = state.RandomDungeonRoomList[i];
                var requirements = RoomRequirements.Get(room);

                // Some rooms such as the Princess room contain no stairs and have limited places doors can be. If this
                // cell is invalid given those limitations then try the next room.
                if ((adjoiningRooms & requirements.ConnectableEntrances) == 0
                    && cell.Type != RoomType.TransportStaircase)
                {
                    logger.Error($"Unable to fit {room.UniqueId} to {cell}. adjoiningRooms: {adjoiningRooms}, ConnectableEntrances: {requirements.ConnectableEntrances}");
                    continue;
                }

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

            // TODO: This needs to throw a recoverable exception. If the Princess room ends up close to the end of the
            // list then it's very possible to be impossible to fit (doubly so because we pass the bottom row last).
            if (cell.GameRoom == null)
            {
                throw logger.Fatal("Exhausted rooms without being able to fit.");
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
        DungeonItems = [];

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
                    if (!RoomRequirements.PathRequirementsAllows(requirements, item))
                    {
                        logger.Write($"❌ Cannot fit {item} in {cell} due to requirements {requirements}.");
                        continue;
                    }

                    state.DungeonItems.RemoveAt(i);
                    logger.Write($"✅ Fit item {item} in {cell} with requirements {requirements}.");
                    DungeonItems = DungeonItems.Add(new DungeonItem(item, cell.Point, requirements));
                    return item;
                }

                throw new RecoverableRandomizerException($"Failed to fit {item} into {cell}.");
            }

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

    private readonly record struct TryWalkSearchPath(Point Point, PathRequirements Requirements, RoomEntrances EntryDirection);
    private readonly record struct TryWalkVisited(Point Point, PathRequirements Requirements, RoomEntrances EntryDirection);

    private bool CanWalkToRoom(Point start, Point destination, bool ignoreDoors = false)
    {
        return TryWalkToRoom(start, destination, ignoreDoors).Any();
    }

    private IEnumerable<PathRequirements> TryWalkToRoom(Point start, Point destination, bool ignoreDoors = false)
    {
        if (!_hasTransportsAttached) throw new Exception($"{nameof(TryWalkToRoom)} was called prior to attaching transports.");
        if (!ignoreDoors && !_hasDoorsFit) throw new Exception($"{nameof(TryWalkToRoom)} was called prior to fitting doors.");

        // If we've visited a map before with identical set of requirements and entry point, then there's no reason to repeat it.
        var visited = new HashSet<TryWalkVisited>();
        var paths = new PriorityQueue<TryWalkSearchPath, long>();
        paths.Enqueue(new TryWalkSearchPath(start, PathRequirements.None, RoomEntrances.Bottom), 0);
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
                    new TryWalkSearchPath(transportExit, transportRequirements, RoomEntrances.Stairs),
                    Distance(transportExit, destination, transportRequirements));
            }

            var roomRequirements = RoomRequirements.Get(room);

            foreach (var direction in RoomEntrances.EntranceOrder)
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

                if (!roomRequirements.ConnectableEntrances.HasFlag(direction))
                {
                    logger.Write($"❌ {room.Id} cannot go {direction} due to missing roomRequirements.ConnectableEntrances.");
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
                    new TryWalkSearchPath(next, requirements, direction.GetOpposite()),
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
            // Recreate this seed for each dungeon to help keep consistency with generator changes. I guess? The
            // room shuffling will still make it different each time.
            var rng = state.CreateDoorRandom(Dungeon.Settings.LevelNumber);
            logger.Enter($"Fitting doors attempt {attempt}.");
            try
            {

                FitDoorsCore(rng);
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

    private void FitDoorsCore(Random rng)
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
        var directions = RoomEntrances.EntranceOrder.ToArray();
        using var logger = _log.CreateScopedFunctionLog(Dungeon.UniqueId);

        IEnumerable<(Point Location, RoomEntrances Direction)> GetAdjoiningRooms(Point point)
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
                cell = cell with { RequiredDoors = RoomEntrances.None };
            }
        }

        void AddRequiredDoor(Point location, RoomEntrances direction)
        {
            ref var cell = ref this[location];
            cell = cell with { RequiredDoors = cell.RequiredDoors | direction };

            // This can OOB, but it should always be valid, or we've made an invalid map elsewhere.
            var adjoiningPoint = location + direction.GetOffset();
            ref var adjoining = ref this[adjoiningPoint];
            adjoining = adjoining with { RequiredDoors = adjoining.RequiredDoors | direction.GetOpposite() };
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

            var requirements = RoomRequirements.Get(cell.DemandGameRoom);
            if (requirements.ConnectableEntrances == RoomEntrances.None)
            {
                logger.Write(GetDebugDisplay());
                throw logger.Fatal($"Room {cell} has no connectable directions.");
            }

            var connectableCount = Popcnt.PopCount((uint)requirements.ConnectableEntrances);
            if (connectableCount == 1)
            {
                var oneDirection = requirements.ConnectableEntrances;
                logger.Write($"Room {cell} has only one connectable direction ({oneDirection}).");

                AddRequiredDoor(location, oneDirection);
                continue;
            }

            var canConnect = new List<(Point Location, RoomEntrances Direction)>();
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
                var requirements = RoomRequirements.Get(room);
                foreach (var adjoining in GetAdjoiningRooms(cell.Point))
                {
                    // Already has a door there.
                    if (cell.RequiredDoors.HasFlag(adjoining.Direction)) continue;
                    // Can't have a good there.
                    if (!requirements.ConnectableEntrances.HasFlag(adjoining.Direction)) continue;

                    AddRequiredDoor(cell.Point, adjoining.Direction);
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
            for (; attempts < 100; attempts++)
            {
                if (TryWalkToAllRooms())
                {
                    logger.Write($"✅ Made dungeon walkable in {attempts} attempts.");
                    return true;
                }

                if (!AddDoorAtRandom())
                {
                    return false;
                }

                logger.Write(GetDebugDisplay());
                logger.Write($"⬆️ Attempting dungeon walk again {attempts}...");
                ++attempts;
            }

            return false;
        }

        if (!RefitDoorsUntilWalkable())
        {
            logger.Write(GetDebugDisplay());
            throw new RecoverableRandomizerException("Unable to add more doors to make all rooms reachable.");
        }

        // TODO: Remove doors until either a random number is reached, or removing said door would make the level
        // unsolvable.

        // Pass over each room and set door types according to our requirements.
        var doorTypeGrid = EachValidPoint().ToDictionary(static t => t, static _ => new Dictionary<Direction, DoorType>());
        foreach (var cell in GetValidCells())
        {
            var doorTypeCell = doorTypeGrid[cell.Point];

            // This is a given. We only support Player coming in from the bottom.
            if (cell.Type == RoomType.Entrance)
            {
                doorTypeCell[Direction.Down] = DoorType.Open;
            }

            foreach (var direction in Direction.DoorDirectionOrder)
            {
                // This door was already set from an adjoining room.
                if (doorTypeCell.ContainsKey(direction)) continue;

                // Our algorithm has decided no door goes here.
                if (!cell.RequiredDoors.HasFlag(direction)) continue;

                var doorType = Stats.GetRandomDoorType(rng);
                doorTypeCell.Add(direction, doorType);

                // Also set the adjoining room's door because they must line up on both sides.
                // Can OOB here, but only if our map is invalid.
                var connected = doorTypeGrid[cell.Point + direction.GetOffset()];
                connected.Add(direction.GetOppositeDirection(), doorType);
            }
        }

        // Now update the actual room layouts with the doors and make further adjustments as needed.
        foreach (var cell in GetValidCells())
        {
            var room = cell.DemandGameRoom;
            var grid = doorTypeGrid[cell.Point];
            foreach (var direction in Direction.DoorDirectionOrder)
            {
                var type = grid.GetValueOrDefault(direction, DoorType.Wall);
                room.UnderworldDoors[direction] = type;
            }

            var anyShutters = grid.Any(static t => t.Value == DoorType.Shutter);
            var requirements = RoomRequirements.Get(room);
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
            if (!cell.RequiresStaircase) RemoveStaircase(cell.DemandGameRoom);
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