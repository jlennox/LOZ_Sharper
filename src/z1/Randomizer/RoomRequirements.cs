using System;
using System.Text;
using z1.IO;

namespace z1.Randomizer;

[Flags] // NIT: Drop "Flags", just pluralize it.
internal enum RoomRequirementFlags
{
    None,
    HasStaircase = 1 << 0,
    HasFloorDrop = 1 << 1,
    HasPushBlock = 1 << 2,
    IsEntrance = 1 << 3,
}

// _Technically,_ these things can also have "level" requirements. IE, Ganon requires Arrow level 2.
[Flags]
internal enum PathRequirements
{
    None,
    Ladder = 1 << 0,
    Recorder = 1 << 1,
    Arrow = 1 << 2,
    Food = 1 << 3,
    Raft = 1 << 4,
    Bracelet = 1 << 5,
}

internal readonly record struct RoomRequirements(
    RoomEntrances ConnectableEntrances, // Does not mean they _are_ connected, just that there's nothing hard blocking it from being connected.
    IReadOnlyDictionary<DoorPair, PathRequirements> Paths,
    RoomRequirementFlags Flags,
    InteractableBlockObject? PushBlock,
    InteractableBlockObject? Staircase)
{
    private delegate bool TryGetLocationInfrontOfEntrance(
        GameRoom room,
        RoomEntrances entrance,
        out Point[] point,
        out PathRequirements requirements);

    private readonly record struct RoomPaths(Dictionary<DoorPair, PathRequirements> Paths, RoomEntrances ValidEntrances);

    private readonly record struct DoorToDoorSearchPath(int X, int Y, PathRequirements Requirements)
    {
        public DoorToDoorSearchPath(Point point, PathRequirements requirements) : this(point.X, point.Y, requirements) { }

        public bool RequiresLadder => Requirements.HasFlag(PathRequirements.Ladder);

        public DoorToDoorSearchPath WithRequirement(PathRequirements requirement) => this with { Requirements = Requirements | requirement };
        public bool SamePoint(DoorToDoorSearchPath other) => X == other.X && Y == other.Y;
        public int Distance(DoorToDoorSearchPath other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
        public override string ToString() => RequiresLadder ? $"({X}, {Y}, WET)" : $"({X}, {Y})";
    }

    private static readonly DebugLog _log = new(nameof(RoomArguments));
    private static readonly Dictionary<int, RoomPaths> _doorCache = new();
    private static readonly Dictionary<string, RoomRequirements> _requirementsCache = new();

    public bool HasStaircase => Flags.HasFlag(RoomRequirementFlags.HasStaircase);
    public bool HasFloorDrop => Flags.HasFlag(RoomRequirementFlags.HasFloorDrop);
    public bool HasPushBlock => Flags.HasFlag(RoomRequirementFlags.HasPushBlock);
    public bool IsEntrance => Flags.HasFlag(RoomRequirementFlags.IsEntrance);

    public bool HasShutterPushBlock => this is { HasPushBlock: true, PushBlock.Interaction.Effect: InteractionEffect.OpenShutterDoors };

    private static RoomPaths GetRoomPaths(GameRoom room)
    {
         return room.HasUnderworldDoors
            ? GetUnderworldRoomPaths(room)
            : GetOverworldRoomPaths(room);
    }

    private static RoomPaths GetOverworldRoomPaths(GameRoom room)
    {
        if (room.HasUnderworldDoors) throw new Exception();

        static RoomEntrances GetRaftEntrance(GameRoom room, out Point point)
        {
            var raftSpot = room.InteractableBlockObjects
                .SingleOrDefault(static t => t.Interaction.Raft != null);
            var direction = raftSpot?.Interaction.Raft?.Direction;
            if (raftSpot == null)
            {
                point = default;
                return RoomEntrances.None;
            }

            point = new Point(raftSpot.X / room.TileWidth, raftSpot.Y / room.TileHeight);
            return direction.ToEntrance();
        }

        static bool TryGetLocationInfrontOfEntrance(
            GameRoom room,
            RoomEntrances entrance,
            out Point[] points,
            out PathRequirements requirements)
        {
            bool CanWalkOnPoint(Point point)
            {
                var behavior = room.RoomMap.Behavior(point.X, point.Y);
                return behavior.CanWalk();
            }

            Point PointFromObjectSpace(int x, int y)
            {
                var tileX = x / room.TileWidth;
                var tileY = y / room.TileHeight;
                // Sometimes we'll be starting on a half tile, so be sure to round them out.
                return new Point(tileX - (tileX % 2), tileY - (tileY % 2));
            }

            var raftEntrance = GetRaftEntrance(room, out var raftPoint);
            if (entrance == raftEntrance)
            {
                requirements = PathRequirements.Raft;
                points = [raftPoint];
                return true;
            }

            requirements = PathRequirements.None;

            switch (entrance)
            {
                case RoomEntrances.Stairs:
                {
                    var caveEntrance = room.InteractableBlockObjects.FirstOrDefault(static t => t.Interaction.Entrance != null);
                    if (caveEntrance != null)
                    {
                        points = [PointFromObjectSpace(caveEntrance.X, caveEntrance.Y)];
                        return true;
                    }

                    points = [];
                    return false;
                }
                case RoomEntrances.Entry:
                    var entry = room.EntryPosition;
                    if (entry != null)
                    {
                        points = [PointFromObjectSpace(entry.X, entry.Y)];
                        return true;
                    }

                    points = [];
                    return false;
            }

            IEnumerable<Point> XRange(int y)
            {
                for (var x = 0; x < room.Width; x += ZPoint.TilesPerBlock) yield return new Point(x, y);
            }

            IEnumerable<Point> YRange(int x)
            {
                for (var y = 0; y < room.Height; y += ZPoint.TilesPerBlock) yield return new Point(x, y);
            }

            var range = entrance switch
            {
                RoomEntrances.Right => YRange(room.Width - ZPoint.TilesPerBlock),
                RoomEntrances.Left => YRange(0),
                RoomEntrances.Top => XRange(0),
                RoomEntrances.Bottom => XRange(room.Height - ZPoint.TilesPerBlock),
                _ => throw new Exception(),
            };

            // We want to find the first in each series that is uninterrupted.
            // _ = walkable, X = non-walkable.
            // __X__
            // Should return (0,0), and (3,0). Because these are used for walking sources/destinations, it can be
            // assumed that in the above example, if you can walk from (0,0), then (0,1) is equivalent because they are
            // connected.
            var foundPoints = new List<Point>();
            var lastFound = false;
            foreach (var point in range)
            {
                if (CanWalkOnPoint(point))
                {
                    if (!lastFound) foundPoints.Add(point);
                    lastFound = true;
                    continue;
                }

                lastFound = false;
            }

            points = foundPoints.ToArray();
            return foundPoints.Count > 0;
        }

        return GetRoomPathsCore(room, TryGetLocationInfrontOfEntrance);
    }

    private static RoomPaths GetUnderworldRoomPaths(GameRoom room)
    {
        if (!room.HasUnderworldDoors) throw new Exception();

        // The location "infront" of the door is used. This is where the player needs to walk to to be able to get to
        // the door. Our map data may not have "doors" actually assigned to this room, so we can't search for them
        // directly.
        static bool TryGetLocationInfrontOfEntrance(
            GameRoom room,
            RoomEntrances entrance,
            out Point[] points,
            out PathRequirements requirements)
        {
            requirements = PathRequirements.None;
            // All cave people block upward movement, except grumbles which can be removed.
            var hasOldMan = room.CaveSpec != null && room.CaveSpec.PersonType != PersonType.Grumble;
            if (hasOldMan && entrance == RoomEntrances.Top)
            {
                points = default;
                return false;
            }

            if (entrance == RoomEntrances.Stairs)
            {
                var found = TryGetTransportExitPosition(room, out var stairsPoint);
                points = found ? [stairsPoint] : [];
                return found;
            }

            var point = entrance switch
            {
                RoomEntrances.Right => new Point(13 * ZPoint.TilesPerBlock, 5 * ZPoint.TilesPerBlock),
                RoomEntrances.Left => new Point(2 * ZPoint.TilesPerBlock, 5 * ZPoint.TilesPerBlock),
                RoomEntrances.Top => new Point(7 * ZPoint.TilesPerBlock, 2 * ZPoint.TilesPerBlock),
                RoomEntrances.Bottom => new Point(7 * ZPoint.TilesPerBlock, 8 * ZPoint.TilesPerBlock),
                _ => throw new Exception(),
            };

            var behavior = room.RoomMap.Behavior(point.X, point.Y);
            // Do not use CanWalk because we need to detect Stairs as invalid entrance locations.
            var isWalkable = behavior is TileBehavior.GenericWalkable or TileBehavior.Sand;
            points = isWalkable ? [point] : [];
            return isWalkable;
        }

        // Technically... there can be multiple exit locations. The original game only used a static value for all
        // exits, but it would be nice to ultimately support this.
        static bool TryGetTransportExitPosition(GameRoom room, out Point point)
        {
            // Scan each room in this world for an entrance that exits into this passed in room.
            foreach (var currentRoom in room.GameWorld.Rooms)
            {
                foreach (var block in currentRoom.InteractableBlockObjects)
                {
                    var entrance = block.Interaction.Entrance;
                    if (entrance == null) continue;
                    var arguments = entrance.Arguments;
                    if (arguments == null) continue;
                    if (arguments.ExitLeft == room.Id || arguments.ExitRight == room.Id)
                    {
                        var exitPosition = entrance.ExitPosition ?? throw new Exception();
                        point = new Point(exitPosition.X / room.TileWidth, exitPosition.Y / room.TileHeight);
                        return true;
                    }
                }
            }

            point = default;
            return false;
        }

        return GetRoomPathsCore(room, TryGetLocationInfrontOfEntrance);
    }

    private enum PathTileBehavior { Solid, Walkable, Water, Bracelet }

    private readonly record struct PathBehaviorMap
    {
        // Contains Tile space information but only stores Block space data.
        // Meaning, the top left of each block contains the behavior, but the other 3 tiles in the block are ignored.
        public int Width { get; init; }
        public int Height { get; init; }
        public PathTileBehavior[,] Behaviors { get; init; }

        public PathTileBehavior this[Point point] => Behaviors[point.X, point.Y];
        public PathTileBehavior this[int x, int y] => Behaviors[x, y];

        private PathBehaviorMap(int width, int height, PathTileBehavior[,] behaviors)
        {
            Width = width;
            Height = height;
            Behaviors = behaviors;
        }

        public bool IsValid(Point point) => point.X >= 0 && point.X < Width && point.Y >= 0 && point.Y < Height;

        public static PathBehaviorMap Create(GameRoom room)
        {
            var width = room.RoomMap.Width;
            var height = room.RoomMap.Height;
            var behaviorMap = new PathTileBehavior[width, height];
            for (var y = 0; y < room.RoomMap.Height; y += ZPoint.TilesPerBlock)
            {
                for (var x = 0; x < room.RoomMap.Width; x += ZPoint.TilesPerBlock)
                {
                    var behavior = room.RoomMap.Behavior(x, y);
                    if (behavior == TileBehavior.Water)
                    {
                        behaviorMap[x, y] = PathTileBehavior.Water;
                        continue;
                    }

                    if (behavior.CanWalk() || behavior == TileBehavior.Door)
                    {
                        behaviorMap[x, y] = PathTileBehavior.Walkable;
                        continue;
                    }

                    var worldX = x * ZPoint.TileSize;
                    var worldY = y * ZPoint.TileSize;
                    var bounds = new Rectangle(worldX, worldY, ZPoint.TileSize, ZPoint.TileSize);
                    var objs = room.InteractableBlockObjects.SingleOrDefault(t => t.GetBounds().IntersectsWith(bounds));
                    if (objs?.Interaction is
                        {
                            Interaction: Interaction.Push or Interaction.PushVertical,
                            ItemRequirement.Slot: ItemSlot.Bracelet
                        })
                    {
                        behaviorMap[x, y] = PathTileBehavior.Bracelet;
                        continue;
                    }

                    // Mark these as walkable because some will be hidden under bombable walls, etc, and wont be
                    // in the normal behavior map.
                    if (objs?.Interaction is { Entrance: not null })
                    {
                        behaviorMap[x, y] = PathTileBehavior.Walkable;
                        continue;
                    }

                    behaviorMap[x, y] = PathTileBehavior.Solid;
                }
            }

            return new PathBehaviorMap(width, height, behaviorMap);
        }

        public string ToDebugString()
        {
            var sb = new StringBuilder();

            for (var y = 0; y < Height; y += ZPoint.TilesPerBlock)
            {
                for (var x = 0; x < Width; x += ZPoint.TilesPerBlock)
                {
                    sb.Append(this[x, y] switch
                    {
                        PathTileBehavior.Solid => 'X',
                        PathTileBehavior.Walkable => '.',
                        PathTileBehavior.Water => '~',
                        PathTileBehavior.Bracelet => 'B',
                        _ => '?',
                    });
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }
    }

    private static RoomPaths GetRoomPathsCore(GameRoom room, TryGetLocationInfrontOfEntrance tryGetLocationInfrontOfEntrance)
    {
        // The original unique room ID was a reference to the specific room layout. Monsters/doors/etc was contained
        // else where and referenced by "the original room ID" into other tables, thusly, we can save a lot of
        // recomputation by this caching.
        var cacheId = room.OriginalUniqueId;
        if (cacheId.HasValue && _doorCache.TryGetValue(cacheId.Value, out var cached)) return cached;

        // This function operates on tile coordinate space but moves in block space (thusly, movementSize = 2).
        // Hit detection/etc are done in tile space, but
        // - We can drastically speed up checks by moving twice the distance each time.
        // - The player can only move over a single block of water when using the ladder. So moving in block space
        //   already makes that check much easier.
        const int movementSize = ZPoint.TilesPerBlock;

        using var logger = _log.CreateScopedFunctionLog(room.UniqueId);

        var paths = new Dictionary<DoorPair, PathRequirements>();
        var walkingSearch = new PriorityQueue<DoorToDoorSearchPath, int>();
        var visited = new HashSet<DoorToDoorSearchPath>();
        var validEntrances = RoomEntrances.None;

        var behaviorMap = PathBehaviorMap.Create(room);
        var asdasdsd = behaviorMap.ToDebugString();

        // Walk from each door to each other door, and record if a ladder was required.
        // Direction.None is used for transport entrances.
        foreach (var doorPair in DoorPair.GetAllPairs(room))
        {
            var fromIsValid = tryGetLocationInfrontOfEntrance(room, doorPair.From, out var fromLocations, out var fromRequirements);
            var toIsValid = tryGetLocationInfrontOfEntrance(room, doorPair.To, out var toLocations, out var toRequirements);

            if (fromIsValid) validEntrances |= doorPair.From;
            if (toIsValid) validEntrances |= doorPair.To;

            if (!fromIsValid)
            {
                logger.Write($"❌ Skipping path for {doorPair} -- no valid location for entrance {doorPair.From}.");
                continue;
            }

            if (!toIsValid)
            {
                logger.Write($"❌ Skipping path for {doorPair} -- no valid location for entrance {doorPair.To}.");
                continue;
            }

            var entranceLocations = fromLocations.SelectMany(_ => toLocations, static (fromLoc, toLoc) => (fromLoc, toLoc));
            foreach (var (fromLocation, toLocation) in entranceLocations)
            {
                // Since our directions are treated ambidextrously, both requirements go on the "from" path. IE, if you can
                // leave this room with the raft, then you must also need the raft to enter it.
                var from = new DoorToDoorSearchPath(fromLocation, fromRequirements | toRequirements);
                var to = new DoorToDoorSearchPath(toLocation, PathRequirements.None);

                void Enqueue(DoorToDoorSearchPath path) => walkingSearch.Enqueue(path, to.Distance(path));

                logger.Enter($"Searching path for {doorPair} ({fromLocation.X},{fromLocation.Y})->({toLocation.X},{toLocation.Y})...");

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
                        // TODO: The way this works with `RequiresLadder` instead of generic requirements isn't the best
                        // now that other requirements than ladder exists. I believe it's still _logically_ correct
                        // given the map design.
                        if (!current.RequiresLadder)
                        {
                            paths.Add(doorPair, current.Requirements);
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

                    foreach (var next in Neighbors(current))
                    {
                        if (!room.RoomMap.IsValid(next.X, next.Y)) continue;
                        if (visited.Contains(next)) continue;

                        var nextBehavior = behaviorMap[next.X, next.Y];
                        switch (nextBehavior)
                        {
                            case PathTileBehavior.Water:
                            {
                                var behavior = behaviorMap[current.X, current.Y];

                                // The ladder can only cross a single water tile. Do not allow crossing multiple water tiles.
                                if (behavior == PathTileBehavior.Water)
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

                                Enqueue(next.WithRequirement(PathRequirements.Ladder));
                                continue;
                            }
                            case PathTileBehavior.Bracelet:
                                Enqueue(next.WithRequirement(PathRequirements.Bracelet));
                                continue;
                            case PathTileBehavior.Walkable:
                                Enqueue(next);
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

                if (foundPathWithNoLadder) break;
            }
        }

        // The princess room is unique in it's the only room with a single possible doorway. We'd normally consider this
        // an error, which is important to ensure the algorithm is correct.
        bool IsPrincessRoom()
        {
            // Not exact but good enough.
            return validEntrances == RoomEntrances.Bottom &&
                room.InteractableBlockObjects.Any(static t => t.Interaction.Item?.Item == ItemId.TriforcePiece);
        }

        if (paths.Count == 0 && !IsPrincessRoom())
        {
            throw logger.Fatal($"Room {room.UniqueId} can't connect to other rooms?");
        }

        var entry = new RoomPaths(paths, validEntrances);
        if (cacheId.HasValue) _doorCache[cacheId.Value] = entry;
        return entry;
    }

    public static RoomRequirements Get(GameRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);

        using var logger = _log.CreateScopedFunctionLog(room.UniqueId, level: LogLevel.Error);

        var cacheId = room.UniqueId;
        if (_requirementsCache.TryGetValue(cacheId, out var cached)) return cached;

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

        var paths = GetRoomPaths(room);
        var roomRequirements = new RoomRequirements(
            paths.ValidEntrances,
            paths.Paths,
            flags,
            pushBlock,
            staircase);

        _requirementsCache[cacheId] = roomRequirements;
        return roomRequirements;
    }

    public static bool PathRequirementsAllows(PathRequirements requirements, ItemId item) => item switch
    {
        ItemId.Ladder => !requirements.HasFlag(PathRequirements.Ladder),
        ItemId.Recorder => !requirements.HasFlag(PathRequirements.Recorder),
        ItemId.WoodArrow or ItemId.SilverArrow => !requirements.HasFlag(PathRequirements.Arrow),
        ItemId.Food => !requirements.HasFlag(PathRequirements.Food),
        _ => true,
    };

    public static PathRequirements GetStairRequirements(GameRoom room)
    {
        var stairs = room.InteractableBlockObjects.FirstOrDefault(static t => t.Interaction.Entrance != null);
        if (stairs == null) return PathRequirements.None;
        if (stairs.Interaction.Interaction != Interaction.Revealed) return PathRequirements.None;

        var revealer = room.GetRevealer(stairs.Interaction);
        var requirements = PathRequirements.None;

        if (revealer.Interaction == Interaction.Push || revealer.RequiresAllEnemiesDefeated)
        {
            requirements |= GetRequirementItems(room.Monsters);
        }
        return requirements;
    }

    public static PathRequirements GetShutterRequirements(GameRoom room)
    {
        if (!room.UnderworldDoors.Any(static t => t.Value == DoorType.Shutter)) return PathRequirements.None;

        return GetRequirementItems(room.Monsters);
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

    private static PathRequirements GetRequirementItems(IEnumerable<MonsterEntry> type)
    {
        return type.BitwiseOr(static t => GetRequirementItem(t.ObjType));
    }
}