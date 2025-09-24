using System;
using System.Collections.Immutable;
using System.Text;

namespace z1.Randomizer;

internal readonly record struct RoomPathRequirement(
    Direction StartingDoor,
    Direction ExitDoor,
    ImmutableArray<ItemId> Requirements);

[Flags]
internal enum RoomRequirementFlags
{
    None,
    HasStaircase = 1 << 0,
    HasItem = 1 << 1,
    HasPushBlock = 1 << 2,
}

internal readonly record struct RoomRequirements(
    Direction ConnectableDirections, // Does not mean they _are_ connected, just that there's nothing hard blocking it from being connected.
    ImmutableArray<RoomPathRequirement> Paths,
    RoomRequirementFlags Flags,
    int Difficulty);

internal sealed class RandomizerDungeonFlags
{
    public bool Shapes { get; set; } = true;
    public int ShapesSizeVariance { get; set; } = 2;
    public bool RandomizeMonsters { get; set; } = true;
    public bool AlwaysHaveCompass { get; set; } = true;
    public bool AlwaysHaveMap { get; set; } = true;
}

internal sealed class RandomizerFlags
{
    public RandomizerDungeonFlags Dungeon { get; } = new();
}

internal sealed class RandomizerState
{
    public RandomizerFlags Flags { get; }
    public Random RoomRandom { get; }

    public RandomizerState(int seed, RandomizerFlags flags)
    {
        Flags = flags;

        var seedRandom = new Random(seed);
        RoomRandom = new Random(seedRandom.Next());
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

    private enum RandomRoomType { None, Normal, Entrance, FloorDrop, ItemStaircase }

    public static void CreateDungeonShape(GameWorld world, RandomizerState state)
    {
        static bool IsDungeonItem(ItemId? item) => item
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

        static bool HasItemStaircase(GameRoom room)
        {
            return room.InteractableBlockObjects.Any(static t => IsDungeonItem(t.Interaction.Entrance?.Arguments?.ItemId));
        }

        static bool HasFloorItem(GameRoom room)
        {
            foreach (var obj in room.ObjectLayer.Objects)
            {
                if (obj is InteractableBlockObject blk && IsDungeonItem(blk.Interaction.Item?.Item)) return true;
            }
            return false;
        }

        var staircaseItemCount = world.Rooms.Count(HasItemStaircase);
        var floorItemCount = world.Rooms.Count(HasFloorItem);
        var rng = state.RoomRandom;

        var hasCompass = state.Flags.Dungeon.AlwaysHaveCompass || rng.GetBool();
        var hasMap = state.Flags.Dungeon.AlwaysHaveMap || rng.GetBool();

        if (hasCompass) ++floorItemCount;
        if (hasMap) ++floorItemCount;

        var sizeVariance = state.Flags.Dungeon.ShapesSizeVariance;
        var newSize = world.Rooms.Length + state.RoomRandom.Next(-sizeVariance, sizeVariance);
        const int maxWidth = 8;
        const int maxHeight = 8;
        var layout = new RandomRoomType[maxWidth, maxHeight];
        var roomCount = 0;
        var entrance = new Point(maxWidth / 2, maxHeight - 1);
        var roomType = RandomRoomType.Entrance;
        var normalRooms = new List<Point>();
        var restartsStat = 0;
        var iterationsState = 0;
        while (roomCount < newSize)
        {
            var path = new Stack<Point>();
            path.Push(entrance);
            ++restartsStat;

            while (path.Count > 0 && roomCount < newSize)
            {
                var current = path.Pop();
                if (layout[current.X, current.Y] == RandomRoomType.None)
                {
                    if (roomType == RandomRoomType.Normal) normalRooms.Add(current);
                    layout[current.X, current.Y] = roomType;
                    roomType = RandomRoomType.Normal;
                    roomCount++;
                }

                if (rng.GetBool() && current.X > 0) path.Push(new Point(current.X - 1, current.Y));
                if (rng.GetBool() && current.X < maxWidth - 1) path.Push(new Point(current.X + 1, current.Y));
                if (rng.GetBool() && current.Y > 0) path.Push(new Point(current.X, current.Y - 1));
                if (rng.GetBool() && current.Y < maxHeight - 1) path.Push(new Point(current.X, current.Y + 1));
                ++iterationsState;
            }
        }

        void SetRoomTypeRandomly(int count, RandomRoomType type)
        {
            for (var i = 0; i < count; i++)
            {
                if (normalRooms.Count == 0) throw new Exception();
                var index = rng.Next(normalRooms.Count);
                var roomPoint = normalRooms[index];
                layout[roomPoint.X, roomPoint.Y] = type;
                normalRooms.RemoveAt(index);
            }
        }

        SetRoomTypeRandomly(staircaseItemCount, RandomRoomType.ItemStaircase);
        SetRoomTypeRandomly(floorItemCount, RandomRoomType.FloorDrop);

        string GetDebugDisplay()
        {
            var sb = new StringBuilder();
            for (var y = 0; y < maxHeight; y++)
            {
                for (var x = 0; x < maxWidth; x++)
                {
                    sb.Append(layout[x, y] switch
                    {
                        RandomRoomType.None => ' ',
                        RandomRoomType.Normal => 'N',
                        RandomRoomType.Entrance => 'E',
                        RandomRoomType.FloorDrop => 'F',
                        RandomRoomType.ItemStaircase => 'I',
                        _ => '?',
                    });
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        var asd = GetDebugDisplay();
    }

    // TODO: This is for future randomizer considerations.
    public static RoomRequirements GetPathRequirements(GameRoom room)
    {
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
                    flags |= RoomRequirementFlags.HasItem;
                    break;
                case InteractableBlockObject { Interaction.Interaction: Interaction.Push }:
                    flags |= RoomRequirementFlags.HasPushBlock;
                    break;
            }
        }

        return new RoomRequirements(
            validDirections,
            paths.ToImmutableArray(),
            flags,
            0);
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
