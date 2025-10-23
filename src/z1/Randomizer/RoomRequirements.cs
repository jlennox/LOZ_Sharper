using System;
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

internal readonly record struct RoomRequirements(
    RoomEntrances ConnectableEntrances, // Does not mean they _are_ connected, just that there's nothing hard blocking it from being connected.
    IReadOnlyDictionary<DoorPair, PathRequirements> Paths,
    RoomRequirementFlags Flags,
    InteractableBlockObject? PushBlock,
    InteractableBlockObject? Staircase)
{
    private readonly record struct RoomPaths(Dictionary<DoorPair, PathRequirements> Paths, RoomEntrances ValidEntrances);

    private readonly record struct DoorToDoorSearchPath(int X, int Y, bool RequiresLadder)
    {
        public DoorToDoorSearchPath(Point point, bool requiresLadder) : this(point.X, point.Y, requiresLadder) { }
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

        static bool TryGetLocationInfrontOfEntrance(GameRoom room, RoomEntrances entrance, out Point point)
        {
            const int blockToTileRatio = 2;

            bool TryPoint(int x, int y, out Point point)
            {
                var behavior = room.RoomMap.Behavior(x, y);
                if (behavior.CanWalk())
                {
                    point = new Point(x, y);
                    return true;
                }

                point = default;
                return false;
            }

            if (entrance is RoomEntrances.Left or RoomEntrances.Right)
            {
                var x = entrance == RoomEntrances.Right ? room.Width - blockToTileRatio : 0;

                for (var y = 0; y < room.Height; y += blockToTileRatio)
                {
                    if (TryPoint(x, y, out point)) return true;
                }
            }
            if (entrance is RoomEntrances.Up or RoomEntrances.Down)
            {
                var y = entrance == RoomEntrances.Down ? room.Height - blockToTileRatio : 0;
                for (var x = 0; x < room.Width; x += blockToTileRatio)
                {
                    if (TryPoint(x, y, out point)) return true;
                }
            }
            else if (entrance == RoomEntrances.Stairs)
            {
                var caveEntrance = room.InteractableBlockObjects.FirstOrDefault(static t => t.Interaction.Entrance != null);
                if (caveEntrance != null)
                {
                    point = new Point(caveEntrance.X, caveEntrance.Y);
                    return true;
                }
            }

            point = default;
            return false;
        }

        static Point GetLocationInfrontOfEntrance(GameRoom room, RoomEntrances direction)
        {
            if (TryGetLocationInfrontOfEntrance(room, direction, out var point)) return point;
            throw new Exception($"Could not find location infront of entrance for entrance {direction} in room {room.UniqueId}.");
        }

        // I hate the amount of rework being done here, even if it's not that expensive. We use
        // TryGetLocationInfrontOfEntrance now, then GetLocationInfrontOfEntrance is used later.
        var validEntrances = RoomEntrances.EntranceOrderWithStairway.Where(t => TryGetLocationInfrontOfEntrance(room, t, out _)).BitwiseOr();

        throw new NotSupportedException();
    }

    private static RoomPaths GetUnderworldRoomPaths(GameRoom room)
    {
        if (!room.HasUnderworldDoors) throw new Exception();

        static RoomEntrances GetValidEntrances(GameRoom room)
        {
            var validEntrances = RoomEntrances.None;
            var invalidEntrances = RoomEntrances.None;
            // All cave people block upward movement, except grumbles can be removed.
            var hasOldMan = room.CaveSpec != null && room.CaveSpec.PersonType != PersonType.Grumble;
            if (hasOldMan) invalidEntrances |= RoomEntrances.Up;

            foreach (var direction in RoomEntrances.EntranceOrder)
            {
                if (invalidEntrances.HasFlag(direction)) continue;
                var infront = GetLocationInfrontOfEntrance(room, direction);
                var behavior = room.RoomMap.Behavior(infront.X, infront.Y);
                _log.Write($"Behavior for door {direction} was {behavior}.");
                // Do not use CanWalk because we need to detect Stairs/etc.
                if (behavior is TileBehavior.GenericWalkable or TileBehavior.Sand)
                {
                    validEntrances |= direction;
                }
            }

            return validEntrances;
        }

        // The location "infront" of the door is used. This is where the player needs to walk to to be able to get to
        // the door. Our map data may not have "doors" actually assigned to this room, so we can't search for them
        // directly.
        static Point GetLocationInfrontOfEntrance(GameRoom room, RoomEntrances entrance)
        {
            const int blockToTileRatio = 2;
            return entrance switch
            {
                RoomEntrances.None => GetTransportExitPosition(room),
                RoomEntrances.Right => new Point(13 * blockToTileRatio, 5 * blockToTileRatio),
                RoomEntrances.Left => new Point(2 * blockToTileRatio, 5 * blockToTileRatio),
                RoomEntrances.Up => new Point(7 * blockToTileRatio, 2 * blockToTileRatio),
                RoomEntrances.Down => new Point(7 * blockToTileRatio, 8 * blockToTileRatio),
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

        return GetRoomPathsCore(room, GetValidEntrances(room), GetLocationInfrontOfEntrance);
    }

    private delegate Point GetLocationInfrontOfEntrance(GameRoom room, RoomEntrances entrance);

    private static RoomPaths GetRoomPathsCore(
        GameRoom room,
        RoomEntrances validEntrances,
        GetLocationInfrontOfEntrance getLocationInfrontOfEntrance)
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
        const int movementSize = 2;

        using var logger = _log.CreateScopedFunctionLog(room.UniqueId);

        if (validEntrances == RoomEntrances.None) throw logger.Fatal($"Room {room.UniqueId} has no valid directions.");

        var paths = new Dictionary<DoorPair, PathRequirements>();
        var walkingSearch = new PriorityQueue<DoorToDoorSearchPath, int>();
        var visited = new HashSet<DoorToDoorSearchPath>();

        // Walk from each door to each other door, and record if a ladder was required.
        // Direction.None is used for transport entrances.
        foreach (var doorPair in DoorPair.GetAllPairs(validEntrances, true))
        {
            var from = new DoorToDoorSearchPath(getLocationInfrontOfEntrance(room, doorPair.From), false);
            var to = new DoorToDoorSearchPath(getLocationInfrontOfEntrance(room, doorPair.To), false);

            logger.Enter($"Searching path for {doorPair}...");

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

        // The princess room is unique in it's the only room with a single possible doorway. We'd normally consider this
        // an error, which is important to ensure the algorithm is correct.
        bool IsPrincessRoom()
        {
            // Not exact but good enough.
            return validEntrances == RoomEntrances.Down &&
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