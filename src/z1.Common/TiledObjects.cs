using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using z1.Common.Data;

namespace z1.Common;

public static class TiledLayerProperties
{
    public const string QuestId = nameof(QuestId);
}

public static class TiledWorldProperties
{
    public const string WorldSettings = nameof(WorldSettings);
}

public static class TiledRoomProperties
{
    // Room
    public const string Id = nameof(Id);
    public const string OriginalUniqueId = nameof(OriginalUniqueId);
    public const string Monsters = nameof(Monsters);
    public const string CaveSpec = nameof(CaveSpec);
    // When a player "enters" this room from a non-screen scroll, this is where they appear.
    // IE, the player enters a dungeon. The player enters a cellar. Etc.
    public const string EntryPosition = nameof(EntryPosition);

    // Overworld
    public const string MonstersEnter = nameof(MonstersEnter);
    public const string Maze = nameof(Maze);
    public const string RoomSettings = nameof(RoomSettings);

    // Underworld
    public const string UnderworldDoors = nameof(UnderworldDoors);
    public const string FireballLayout = nameof(FireballLayout);

    // This ordering is exposed to the game developer. It's clockwise because that's intuitive and easy to remember.
    public static readonly ImmutableArray<Direction> DoorDirectionOrder = [Direction.Right, Direction.Left, Direction.Down, Direction.Up];
}

public static class TiledObjectProperties
{
    public const string Type = nameof(Type);
    public const string Id = nameof(Id);

    // Action
    public const string Interaction = nameof(Interaction);
    public const string RecorderDestination = nameof(RecorderDestination); // What sequence the recorder will warp you in.

    // TileBehavior
    public const string TileBehavior = nameof(TileBehavior);
}

public static class TiledTileSetTileProperties
{
    public static readonly TileBehavior DefaultTileBehavior = TileBehavior.GenericWalkable;

    public const string Behavior = nameof(Behavior);
    public const string Object = nameof(Object);
    public const string ObjectOffsets = nameof(ObjectOffsets);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameObjectLayerObjectType
{
    Unknown,
    Interactive,
    TileBehavior,
}

[TiledClass]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Interaction
{
    Unknown,
    None,
    Bomb,
    Burn,
    Push,
    PushVertical,
    Recorder,
    Touch,
    TouchOnce,
    Cover,
    // Requires a "revealed" to show.
    Revealed,
}

[TiledClass]
public sealed class MazeRoom
{
    [JsonConverter(typeof(EnumArrayJsonConverter<Direction>))]
    public required Direction[] Path { get; init; }
    public Direction ExitDirection { get; set; }
}

public sealed class EntryPosition
{
    public int X { get; set; }
    public int Y { get; set; }
    public Direction Facing { get; set; }
    public int? TargetX { get; set; }
    public int? TargetY { get; set; }

    public EntryPosition() { }

    public EntryPosition(int x, int y, Direction facing)
    {
        X = x;
        Y = y;
        Facing = facing;
    }

    public EntryPosition(int x, int y, Direction facing, int targetY)
    {
        X = x;
        Y = y;
        Facing = facing;
        TargetY = targetY;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameWorldType
{
    // Can be in the format of "1" or "0/1" for quest/level number format.
    Underworld,
    Overworld,
    UnderworldCommon,
    OverworldCommon
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EntranceAnimation
{
    None, Descend
}

[TiledClass]
public sealed class Entrance
{
    public GameWorldType DestinationType { get; set; }
    public string Destination { get; set; }
    public EntranceAnimation Animation { get; set; }
    // When the player leave this new area, the position on this map the player should be.
    public PointXY? ExitPosition { get; set; }
    // Where inside the new map the player should be.
    public EntryPosition? EntryPosition { get; set; }
    public ShopSpec? Shop { get; set; }
    public BlockType BlockType { get; set; }
    public RoomArguments? Arguments { get; set; }

    public override string ToString() => Destination;

    public static Entrance CreateItemCellar(ItemId item, PointXY exitRoom)
    {
        return new Entrance
        {
            Arguments = new RoomArguments
            {
                ExitLeft = $"{exitRoom.X},{exitRoom.Y}",
                ItemId = item
            },
            BlockType = BlockType.Stairs,
            Destination = CommonUnderworldRoomName.ItemCellar,
            DestinationType = GameWorldType.UnderworldCommon,
            EntryPosition = new EntryPosition(48, 96, Direction.Down)
            {
                TargetX = 0,
                TargetY = 96,
            },
            ExitPosition = new PointXY(96, 160)
        };
    }

    public static Entrance CreateTransportRoom(PointXY roomA, PointXY roomB, bool isLeft)
    {
        var (exit, entranceX) = isLeft
            ? (new PointXY(96, 160), 48)
            : (new PointXY(96, 192), 192);

        return new Entrance
        {
            Arguments = new RoomArguments
            {
                ExitLeft = $"{roomA.X},{roomA.Y}",
                ExitRight = $"{roomB.X},{roomB.Y}",
            },
            BlockType = BlockType.Stairs,
            Destination = CommonUnderworldRoomName.Transport,
            DestinationType = GameWorldType.UnderworldCommon,
            EntryPosition = new EntryPosition(entranceX, 96, Direction.Down)
            {
                TargetX = 0,
                TargetY = 96,
            },
            ExitPosition = exit
        };
    }
}

public static class CaveExntranceEx
{
    public static bool IsValid([MaybeNullWhen(false)] this Entrance entrance)
    {
        return entrance is not null && !string.IsNullOrEmpty(entrance.Destination);
    }
}

public sealed class InteractionItemRequirement
{
    public ItemSlot ItemSlot { get; set; }
    // Item needs to be "at least" this level. IE, if set to 1 and they have level 2, it still triggers.
    public int ItemLevel { get; set; }

    public InteractionItemRequirement() { }
    public InteractionItemRequirement(ItemSlot itemSlot, int itemLevel)
    {
        ItemSlot = itemSlot;
        ItemLevel = itemLevel;
    }
}

[Flags]
[JsonConverter(typeof(TiledJsonSelectableEnumConverter<InteractionRequirements>))]
[TiledSelectableEnum]
public enum InteractionRequirements
{
    None = 0,
    AllEnemiesDefeated = 1 << 0
}

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
[TiledSelectableEnum]
public enum InteractionEffect
{
    None = 0,
    OpenShutterDoors = 1 << 0,
    DryoutWater = 1 << 1,
}

public abstract class InteractableBase
{
    public required string Name { get; init; }
    public Interaction Interaction { get; set; }
    public InteractionItemRequirement? ItemRequirement { get; set; }
    public InteractionRequirements Requirements { get; set; }
    public InteractionEffect Effect { get; set; }
    public bool Persisted { get; set; }
    public string? Reveals { get; set; }

    [TiledIgnore, JsonIgnore] public RoomArguments? ArgumentsIn { get; set; }
    [TiledIgnore, JsonIgnore] public bool RequiresAllEnemiesDefeated => Requirements.HasFlag(InteractionRequirements.AllEnemiesDefeated);

    public abstract void Initialize(RoomArguments arguments);
}

public sealed class RoomInteraction : InteractableBase
{
    public override void Initialize(RoomArguments arguments) { }

    public static RoomInteraction CreateOpenShutterDoors() => new()
    {
        Name = "FoesDoor",
        Interaction = Interaction.None,
        Requirements = InteractionRequirements.AllEnemiesDefeated,
        Effect = InteractionEffect.OpenShutterDoors,
    };
}

public sealed class InteractableBlock : InteractableBase
{
    // How a push block appears as it moves.
    public BlockType? ApparanceBlock { get; set; }
    public Entrance? Entrance { get; set; }
    public ObjType? SpawnedType { get; set; }
    public Raft? Raft { get; set; }
    public RoomItem? Item { get; set; }
    // These are root level, not inside ShopSpec, so that we can have an array via multiple properties.
    public ShopItem[]? CaveItems { get; set; }
    public bool Repeatable { get; set; }

    [TiledIgnore, JsonIgnore]
    private bool _isRoomItemFromArgument;

    // I really hate how this works. There's the problem that if we change the argument id, that the next time we enter
    // the room, it won't be ArgumentItemId and will behave wrong.
    public override void Initialize(RoomArguments arguments)
    {
        ArgumentsIn = arguments;
        if (Item != null && (Item.Item == ItemId.ArgumentItemId || _isRoomItemFromArgument))
        {
            _isRoomItemFromArgument = true;
            Item.Item = ArgumentsIn?.ItemId ?? throw new Exception($"{nameof(ItemId.ArgumentItemId)} is used but no argument provided.");
        }
    }

    public bool IsItemOnly()
    {
        // TODO: This isn't my happy place. We really should just have an item type instead of reusing plain
        // interactable blocks.
        return Item != null && Interaction == Interaction.None
            && ItemRequirement == null && Requirements == InteractionRequirements.None
            && Effect == InteractionEffect.None && Reveals == null
            && ApparanceBlock == null && Entrance == null
            && SpawnedType == null && Raft == null && (CaveItems == null || CaveItems.Length == 0);
    }
}

[Flags]
[JsonConverter(typeof(TiledJsonSelectableEnumConverter<ItemObjectOptions>))]
[TiledSelectableEnum]
public enum ItemObjectOptions
{
    None = 0,
    IsRoomItem = 1 << 0,
    LiftOverhead = 1 << 1,
    BecomesInactive = 1 << 2,
    Persisted = 1 << 3,
    MakeItemSound = 1 << 4,
}

[TiledClass]
public sealed class RoomItem
{
    public ItemId Item { get; set; }
    public ItemObjectOptions Options { get; set; }
}

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
[TiledSelectableEnum]
public enum RoomFlags
{
    [TiledIgnore]
    None = 0,
    PlaysSecretChime = 1 << 0,
    IsEntrance = 1 << 1,
    // Used to know when to no longer play the bossroar AmbientSound.
    IsBossRoom = 1 << 2,
    IsLadderAllowed = 1 << 3,
    IsDark = 1 << 4,
    // For a cellar, for example, instead shows the level's map you came from.
    ShowPreviousMap = 1 << 5,
    // This room does not show when the map is revealed.
    HiddenFromMap = 1 << 6,
}

[TiledClass]
public sealed class RoomSettings
{
    public Palette InnerPalette { get; set; }
    public Palette OuterPalette { get; set; }
    public SoundEffect? AmbientSound { get; set; }
    public RoomFlags Options { get; set; }
    // Only used when something is destroyed or moved.
    public BlockType FloorTile { get; set; }

    [TiledIgnore, JsonIgnore] public bool PlaysSecretChime => Options.HasFlag(RoomFlags.PlaysSecretChime);
    [TiledIgnore, JsonIgnore] public bool IsEntrance => Options.HasFlag(RoomFlags.IsEntrance);
    [TiledIgnore, JsonIgnore] public bool IsBossRoom => Options.HasFlag(RoomFlags.IsBossRoom);
    [TiledIgnore, JsonIgnore] public bool IsLadderAllowed => Options.HasFlag(RoomFlags.IsLadderAllowed);
    [TiledIgnore, JsonIgnore] public bool IsDark => Options.HasFlag(RoomFlags.IsDark);
    [TiledIgnore, JsonIgnore] public bool HideMap => Options.HasFlag(RoomFlags.ShowPreviousMap);
    [TiledIgnore, JsonIgnore] public bool HiddenFromMap => Options.HasFlag(RoomFlags.HiddenFromMap);
}

[TiledClass]
public sealed class RoomInteractions
{
    public required RoomInteraction[] Interactions { get; init; }
}

[TiledClass]
public sealed class Raft
{
    public Direction Direction { get; set; }
    public string? Destination { get; set; }

    public Raft() { }
    public Raft(Direction direction)
    {
        Direction = direction;
    }
}

[TiledClass]
public sealed class RecorderDestination
{
    // What sequence the recorder will warp you in.
    public int Slot { get; set; }
}

[TiledClass]
public sealed class RoomArguments
{
    public string? ExitLeft { get; set; }
    public string? ExitRight { get; set; }
    public ItemId? ItemId { get; set; }

    // YO. This is technically wrong. They're ID's, not points.
    public PointXY GetExitLeftPoint() => ParsePoint(ExitLeft ?? throw new Exception($"{nameof(ExitLeft)} is not set."));
    public PointXY GetExitRightPoint() => ParsePoint(ExitRight ?? throw new Exception($"{nameof(ExitRight)} is not set."));

    public bool TryGetExitLeftPoint([NotNullWhen(false)] out PointXY point) => TryParsePoint(ExitLeft, out point);
    public bool TryGetExitRightPoint([NotNullWhen(false)] out PointXY point) => TryParsePoint(ExitRight, out point);

    private static PointXY ParsePoint(string input)
    {
        if (!TryParsePoint(input, out var result)) throw new FormatException($"Point \"{input}\" is not in the correct format of \"x,y\".");
        return result;
    }

    private static bool TryParsePoint(string? input, [MaybeNullWhen(false)] out PointXY point)
    {
        point = null;
        if (input == null) return false;
        var commaIndex = input.IndexOf(',');
        if (commaIndex < 0) return false;

        var xSpan = input[..commaIndex];
        var ySpan = input[(commaIndex + 1)..];

        if (!int.TryParse(xSpan, out var x) || !int.TryParse(ySpan, out var y)) return false;
        point = new PointXY(x, y);
        return true;
    }
}