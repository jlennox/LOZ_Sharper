using System;
using z1.IO;

namespace z1.Randomizer;

internal readonly record struct RoomRequirements(
    Direction ConnectableDirections, // Does not mean they _are_ connected, just that there's nothing hard blocking it from being connected.
    IReadOnlyDictionary<DoorPair, PathRequirements> Paths,
    RoomRequirementFlags Flags,
    InteractableBlockObject? PushBlock,
    InteractableBlockObject? Staircase)
{
    private readonly record struct RoomPaths(Dictionary<DoorPair, PathRequirements> Paths, Direction ValidDirections);

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
        if (!room.HasUnderworldDoors) return new RoomPaths([], Direction.None);

        var cacheId = room.OriginalUniqueId;
        if (cacheId.HasValue && _doorCache.TryGetValue(cacheId.Value, out var cached))
        {
            return cached;
        }

        // This function operates on tile coordinate space but moves in block space (thusly, movementSize = 2).
        // Hit detection/etc are done in tile space, but
        // - We can drastically speed up checks by moving twice the distance each time.
        // - The player can only move over a single block of water when using the ladder. So moving in block space
        //   already makes that check much easier.
        const int movementSize = 2;

        // The location "infront" of the door is used. This is where the player needs to walk to to be able to get to
        // the door. Our map data may not have "doors" actually assigned to this room, so we can't search for them
        // directly.
        static Point GetLocationInfrontOfEntrance(GameRoom room, Direction direction)
        {
            const int blockToTileRatio = 2;
            return direction switch
            {
                Direction.None => GetTransportExitPosition(room),
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

        using var logger = _log.CreateScopedFunctionLog(room.UniqueId);
        var validDirections = Direction.None;
        var invalidDirections = Direction.None;
        // All cave people block upward movement, except grumbles can be removed.
        var hasOldMan = room.CaveSpec != null && room.CaveSpec.PersonType != PersonType.Grumble;
        if (hasOldMan) invalidDirections |= Direction.Up;

        foreach (var direction in Direction.DoorDirectionOrder)
        {
            if (invalidDirections.HasFlag(direction)) continue;
            var infront = GetLocationInfrontOfEntrance(room, direction);
            var behavior = room.RoomMap.Behavior(infront.X, infront.Y);
            logger.Write($"Behavior for door {direction} was {behavior}.");
            if (behavior is TileBehavior.GenericWalkable or TileBehavior.Sand)
            {
                validDirections |= direction;
            }
        }

        if (validDirections == Direction.None)
        {
            throw logger.Fatal($"Room {room.UniqueId} has no valid directions.");
        }

        var paths = new Dictionary<DoorPair, PathRequirements>();
        var walkingSearch = new PriorityQueue<DoorToDoorSearchPath, int>();
        var visited = new HashSet<DoorToDoorSearchPath>();

        // Walk from each door to each other door, and record if a ladder was required.
        // Direction.None is used for transport entrances.
        foreach (var startingDirection in ((IEnumerable<Direction>)Direction.DoorDirectionOrder).Add(Direction.None))
        {
            var isTransportedInEntrance = startingDirection == Direction.None;
            if (!validDirections.HasFlag(startingDirection) && !isTransportedInEntrance) continue;

            var from = new DoorToDoorSearchPath(GetLocationInfrontOfEntrance(room, startingDirection), false);

            foreach (var endingDirection in Direction.DoorDirectionOrder)
            {
                if (endingDirection == startingDirection) continue;
                if (!validDirections.HasFlag(endingDirection)) continue;

                var doorPair = DoorPair.Create(startingDirection, endingDirection);
                if (paths.ContainsKey(doorPair)) continue;

                logger.Enter($"Searching path for {doorPair}...");

                var to = new DoorToDoorSearchPath(GetLocationInfrontOfEntrance(room, endingDirection), false);

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

        bool IsPrincessRoom()
        {
            // Not exact but good enough.
            return validDirections == Direction.Down &&
                room.InteractableBlockObjects.Any(static t => t.Interaction.Item?.Item == ItemId.TriforcePiece);
        }

        if (paths.Count == 0 && !IsPrincessRoom())
        {
            throw logger.Fatal($"Room {room.UniqueId} can't connect to other rooms?");
        }

        var entry = new RoomPaths(paths, validDirections);
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
            paths.ValidDirections,
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