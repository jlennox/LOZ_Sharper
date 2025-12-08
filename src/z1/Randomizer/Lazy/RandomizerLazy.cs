using System;
using System.Collections.Immutable;
using System.Diagnostics;
using z1.IO;

namespace z1.Randomizer.Lazy;

// The "Lazy" randomizer is a secondary approach to randomization. It attempts to create the world
// through "constraints." The outter-most constraint is "Player, walk to Princess." The world is created as it goes
// to fetch each one of those items. "Princess is in Level 9, get level 9. It does not exist, create it, etc."

// Hmmm... Abstract out Overworld/Dungeon# as a "GameLocation" I think? Then have a point inside for that...?

// Might be a bit confusing -- but a floor drop is any room that has a "drop," a floor item room is any room that has
// an actual item (ie, boomerang).
internal enum GameRoomType { Weird, FloorDrop, Stairs }

internal readonly record struct DungeonCell(GameRoom? Room, Point Point, bool HasRoom, RoomEntrances Entrances);

internal readonly record struct DungeonRequirements
{
    public int StaircaseItemCount { get; init; }
    public int FloorItemCount { get; init; }
    public int TrainsportStairsCount { get; init; }
}

internal sealed class GameEntityPool
{
    public required ImmutableArray<GameWorld> Dungeons { get; init; }
    public required List<GameRoom> DungeonRooms { get; init; }
    public required List<ItemId> DungeonItems { get; init; }

    public static GameEntityPool Get(ContextSource ctxSource, WorldStore worldStore)
    {
        using var ctx = ctxSource.Create();
        if (ctx.TryGetCached<GameEntityPool>(out var cached)) return cached;
        var rng = ctx.CreateRng();

        var dungeons = Enumerable.Range(1, 9)
            .Select(t => worldStore.GetDungeon(0, t))
            .Shuffle(rng)
            .ToImmutableArray();

        var dungeonRooms = dungeons
            .SelectMany(static t => t.Rooms)
            .Shuffle(rng)
            .ToList();

        var dungeonItems = DungeonStats
            .AllDungeonItems
            .Shuffle(rng)
            .ToList();

        var pool = new GameEntityPool
        {
            Dungeons = dungeons,
            DungeonRooms = dungeonRooms,
            DungeonItems = dungeonItems
        };
        return ctx.SetCache(pool);
    }

    public GameRoom TakeNextDungeonEntranceRoom()
    {
        var entrances = Dungeons
            .Select(static t => t.EntranceRoom)
            .ToArray();

        foreach (var room in DungeonRooms)
        {
            if (!entrances.Contains(room)) continue;

            DungeonRooms.Remove(room);
            return room;
        }

        throw new Exception("No additional entrance rooms were located.");
    }
}

internal sealed class OverworldGameObject : GameMapObject
{
    public GameWorld Overworld { get; set; }

    public static OverworldGameObject Get(ContextSource ctxSource)
    {
        using var ctx = ctxSource.Create(typeof(OverworldGameObject));
        if (ctx.TryGetCached<OverworldGameObject>(out var cached)) return cached;
        var rng = ctx.CreateRng();

        var overworldObject = new OverworldGameObject();
        return ctx.SetCache(overworldObject);
    }

    public Point GetRoomLocation(GameRoom room)
    {
        throw new NotImplementedException();
    }

    public override bool CanWalkTo(Point startRoom, Point startPoint, Point endRoom, Point endPoint)
    {
        return false;
    }
}

internal abstract class GameMapObject
{
    public abstract bool CanWalkTo(Point startRoom, Point startPoint, Point endRoom, Point endPoint);
}

internal sealed class DungeonGameObject : GameMapObject
{
    private const int _maxWidth = World.UnderworldMapWidth;
    private const int _maxHeight = World.UnderworldMapHeight;

    private static readonly DebugLog _log = new(nameof(DungeonGameObject));

    public int Number { get; init; }
    public DungeonCell[,] Layout { get; init; }
    public DungeonRequirements Requirements { get; init; }
    public List<DungeonItemGameObject> Items { get; } = new();

    private readonly IContext _ctx;
    private readonly OverworldGameObject _overworld;
    private readonly GameEntityPool _pool;
    private readonly ImmutableArray<GameRoom> _floorItemRooms;
    private readonly ImmutableArray<GameRoom> _staircaseRooms;

    private ref DungeonCell this[Point i] => ref Layout[i.X, i.Y];

    private DungeonGameObject(
        IContext ctx,
        int number,
        DungeonCell[,] layout,
        OverworldGameObject overworld,
        DungeonRequirements requirements,
        GameEntityPool pool)
    {
        _ctx = ctx;
        _overworld = overworld;
        Number = number;
        Layout = layout;
        Requirements = requirements;
        _pool = pool;
        _floorItemRooms = pool.DungeonRooms
            .PopManyExactly(static t => t.IsRoomType(GameRoomType.FloorDrop), requirements.FloorItemCount)
            .ToImmutableArray();
        var requiredStaircaseRooms = requirements.TrainsportStairsCount + requirements.StaircaseItemCount;
        _staircaseRooms = pool.DungeonRooms
            .PopManyExactly(static t => t.IsRoomType(GameRoomType.Stairs), requiredStaircaseRooms)
            .ToImmutableArray();
    }

    private static IEnumerable<Point> EachPoint() => Point.FromRange(_maxWidth, _maxHeight);
    private static bool IsValidPoint(Point point) => point.X is >= 0 and < _maxWidth && point.Y is >= 0 and < _maxHeight;
    private bool IsRoomCell(Point point) => IsValidPoint(point) && this[point].HasRoom;
    public IEnumerable<DungeonCell> EachRoomCell() => EachPoint().Where(IsRoomCell).Select(t => this[t]);

    public static void InitializeDungeons(ContextSource ctxSource, WorldStore worldStore)
    {
        var pool = GameEntityPool.Get(ctxSource, worldStore);

        var dungeons = Enumerable.Range(1, 9)
            .Select(t => Create(ctxSource, t, worldStore))
            .ToArray();

        var weirdRooms = pool.DungeonRooms
            .Where(static t => t.IsRoomType(GameRoomType.Weird))
            .ToArray();
    }

    public static DungeonGameObject Get(ContextSource ctxSource, int dungeonNumber)
    {
        using var ctx = ctxSource.Create(typeof(DungeonGameObject), dungeonNumber);
        if (ctx.TryGetCached<DungeonGameObject>(out var cached)) return cached;
        throw new Exception("Dungeon not initialized.");
    }

    public static DungeonGameObject Create(ContextSource ctxSource, int dungeonNumber, WorldStore worldStore)
    {
        using var ctx = ctxSource.Create(typeof(DungeonGameObject), dungeonNumber);
        if (ctx.TryGetCached<DungeonGameObject>(out var cached)) return cached;
        var rng = ctx.CreateRng();

        var world = worldStore.GetDungeon(0, dungeonNumber);
        var stats = DungeonStats.Create(world);
        using var logger = _log.CreateNamedScopedFunctionLog(world.UniqueId);
        logger.Enter("Creating dungeon shape.");

        var requiredFloorDropRooms = stats.FloorItemCount;

        ++requiredFloorDropRooms; // Compass
        ++requiredFloorDropRooms; // Map
        ++requiredFloorDropRooms; // Triforce.
        ++requiredFloorDropRooms; // Heart.

        DungeonCell[,] CreateShape()
        {
            var checkDirections = Direction.DoorDirectionOrder.ToArray();
            var layout = new DungeonCell[_maxWidth, _maxHeight];
            var starting = new Point(rng.Next(0, _maxWidth), _maxHeight - 1);
            var sizeVariance = 0; // TODO
            var newSize = world.Rooms.Length + rng.Next(-sizeVariance, sizeVariance);
            var roomCount = 0;
            var restartsStat = 0;

            // Initialize each cell
            foreach (var point in EachPoint())
            {
                layout.FromPoint(point) = new DungeonCell { Point = point };
            }

            // It's possible for the random walk to not fill enough rooms, so we restart the walk until we do.
            while (roomCount < newSize)
            {
                if (++restartsStat > 1000) throw logger.Fatal($"Unable to generate shape for dungeon {world}. roomCount: {roomCount}, newSize: {newSize}.");

                var path = new Stack<Point> { starting };
                while (path.TryPop(out var current) && roomCount < newSize)
                {
                    ref var cell = ref layout[current.X, current.Y];
                    cell = cell with { HasRoom = true };
                    ++roomCount;

                    // Must be randomized the directions because it'll lean to index 0, since roomCount consumes them.
                    checkDirections.Shuffle(rng);
                    foreach (var dir in checkDirections)
                    {
                        var next = current + dir.GetOffset();
                        if (!IsValidPoint(next)) continue;
                        if (rng.GetBool()) path.Push(next);
                    }
                }
            }

            bool HasRoom(Point point) => IsValidPoint(point) && layout.FromPoint(point).HasRoom;

            // Compute the directional entrances each cell has.
            foreach (var point in EachPoint())
            {
                var entrances = RoomEntrances.None;
                if (HasRoom(point + new Point(-1, 0))) entrances |= RoomEntrances.Left;
                if (HasRoom(point + new Point(1, 0))) entrances |= RoomEntrances.Right;
                if (HasRoom(point + new Point(0, -1))) entrances |= RoomEntrances.Top;
                if (HasRoom(point + new Point(0, 1))) entrances |= RoomEntrances.Bottom;
                layout.FromPoint(point) = new DungeonCell { Entrances = entrances };
            }

            return layout;
        }

        // TODO: Actually assign the floor drop and item drop rooms here.
        // That's because figuring out what rooms, allocating them in the constructor, and then assigning them later
        // feels wonky and weird. Deferring that makes it more complex, not lazier.

        var layout = CreateShape();
        var requirements = new DungeonRequirements
        {
            FloorItemCount = requiredFloorDropRooms,
            StaircaseItemCount = stats.StaircaseItemCount,
            TrainsportStairsCount = stats.TrainsportStairsCount,
        };
        var pool = GameEntityPool.Get(ctxSource, worldStore);
        var overworld = OverworldGameObject.Get(ctxSource);
        var dungeonObect = new DungeonGameObject(ctx, world.Settings.LevelNumber, layout, overworld, requirements, pool);
        return ctx.SetCache(dungeonObect);
    }

    public DungeonPointGameObject GetInteriorEntranceLocation()
    {
        using var ctx = _ctx.Create();
        if (ctx.TryGetCached<DungeonPointGameObject>(out var cached)) return cached;
        var rng = ctx.CreateRng();

        var possibleEntrancePoints = Point
            .FromRange(_maxWidth, _maxHeight - 1)
            .Where(IsRoomCell)
            .ToArray();

        var entrancePoint = possibleEntrancePoints.GetRandomly(rng);
        var entranceRoom = _pool.TakeNextDungeonEntranceRoom();
        var entryPosition = entranceRoom.EntryPosition?.ToPoint() ?? throw new Exception();
        ref var cell = ref this[entrancePoint];
        cell = cell with { Room = entranceRoom };

        var gameObject = new DungeonPointGameObject(Number, entrancePoint, entryPosition);
        return ctx.SetCache(gameObject);
    }

    public OverworldPointGameObject GetExteriorEntranceLocation()
    {
        using var ctx = _ctx.Create();
        if (ctx.TryGetCached<OverworldPointGameObject>(out var cached)) return cached;

        // TODO: This feels too over the top.
        foreach (var room in _overworld.Overworld.Rooms)
        {
            var entranceObject = room.InteractableBlockObjects
                .FirstOrDefault(t => DoesEntranceTarget(t.Interaction.Entrance));
            if (entranceObject == null) continue;

            var roomPoint = _overworld.GetRoomLocation(room);
            var entranceLocation = new Point(entranceObject.X, entranceObject.Y);

            var gameObject = new OverworldPointGameObject(roomPoint, entranceLocation);
            return ctx.SetCache(gameObject);
        }

        throw new Exception();
    }

    public OverworldPointGameObject GetExteriorExitLocation()
    {
        using var ctx = _ctx.Create();
        if (ctx.TryGetCached<OverworldPointGameObject>(out var cached)) return cached;

        // TODO: This feels too over the top.
        foreach (var room in _overworld.Overworld.Rooms)
        {
            var entranceObject = room.InteractableBlockObjects
                .Select(static t => t.Interaction.Entrance)
                .FirstOrDefault(DoesEntranceTarget);
            if (entranceObject == null) continue;

            var roomPoint = _overworld.GetRoomLocation(room);
            var exitLocation = entranceObject.ExitPosition?.ToPoint() ?? throw new Exception();

            var gameObject = new OverworldPointGameObject(roomPoint, exitLocation);
            return ctx.SetCache(gameObject);
        }

        throw new Exception();
    }

    private bool DoesEntranceTarget(Entrance? entrance)
    {
        if (entrance == null) return false;
        if (entrance.DestinationType != GameWorldType.Underworld) return false;

        return entrance.Destination == $"{0:##}_{Number:##}";
    }

    public override bool CanWalkTo(Point startRoom, Point startPoint, Point endRoom, Point endPoint)
    {
        return false;
    }
}

internal sealed class RandomizerLazy
{
    private static readonly DebugLog _log = new(nameof(RandomizerLazy), DebugLogDestination.File);
    private readonly ContextSource _ctx;

    private readonly GameWorld _originalOverworld;

    public RandomizerLazy(int seed, WorldStore worldStore)
    {
        _ctx = new ContextSource(seed);
        _originalOverworld = worldStore.GetOverworld(0);

        DungeonGameObject.InitializeDungeons(_ctx, worldStore);
    }

    public static WorldStore Create(RandomizerState state)
    {
        var timer = Stopwatch.StartNew();

        _log.Write(nameof(Create), $"Starting dungeon randomization {state.Seed}.");
        try
        {
            return CreateCore(state);
        }
        catch (Exception e)
        {
            _log.Fatal($"Dungeon randomization failed: {e}");
            throw;
        }
        finally
        {
            _log.Write(nameof(Create), $"Finished dungeon randomization in {timer.Elapsed}.");
        }
    }

    private GameObject GetPrincess()
    {
        var dungeon = DungeonGameObject.Get(_ctx, 9);
        // var princessRoom = dungeon.EachRoomCell().ToArray().GetRandomly();

        throw new NotImplementedException();
    }

    private GameObject GetDungeon(int number)
    {
        using var context = _ctx.Create(number);
        if (context.TryGetCached<GameObject>(out var cached)) return cached;

        throw new NotImplementedException();
    }

    private GameObject GetPlayerStart()
    {
        using var context = _ctx.Create();
        if (context.TryGetCached<GameObject>(out var cached)) return cached;

        var rng = context.CreateRng();

        var roomX = rng.Next(0, World.WorldWidth);
        var roomY = rng.Next(0, World.WorldHeight);

        var entrance = _originalOverworld.EntranceRoom;
        var entryLocation = entrance.EntryPosition ?? throw new Exception();

        var playerStart = new OverworldPointGameObject(
            new Point(roomX, roomY),
            new Point(entryLocation.X, entryLocation.Y));
        return context.SetCache(playerStart);
    }

    private bool CanReach(GameObject start, GameObject end)
    {
        throw new NotImplementedException();
    }

    public static WorldStore CreateCore(RandomizerState state)
    {
        var worldStorage = new AssetWorldStore();
        var rando = new RandomizerLazy(state.Seed, worldStorage);
        rando.Resolve();
        throw new NotImplementedException();
    }

    public void Resolve()
    {
        var princess = GetPrincess();
        var player = GetPlayerStart();
        if (!CanReach(princess, player)) throw new Exception("Invalid game exception");
    }
}
