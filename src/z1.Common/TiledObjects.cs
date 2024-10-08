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
    public const string WorldInfo = nameof(WorldInfo);
}

public static class TiledObjectProperties
{
    public const string Type = nameof(Type);

    // Room
    public const string Id = nameof(Id);
    public const string Monsters = nameof(Monsters);

    // Overworld
    public const string MonstersEnter = nameof(MonstersEnter);
    public const string Maze = nameof(Maze);
    public const string RoomInformation = nameof(RoomInformation);

    // Underworld
    public const string DungeonDoors = nameof(DungeonDoors);
    public const string Secret = nameof(Secret);
    public const string ItemId = nameof(ItemId);
    public const string ItemPosition = nameof(ItemPosition);
    public const string FireballLayout = nameof(FireballLayout);
    public const string CellarItem = nameof(CellarItem);
    public const string CellarStairsLeft = nameof(CellarStairsLeft);
    public const string CellarStairsRight = nameof(CellarStairsRight);
    public const string HiddenFromMap = nameof(HiddenFromMap);

    public static readonly ImmutableArray<Direction> DoorDirectionOrder = [Direction.Right, Direction.Left, Direction.Down, Direction.Up];

    // Action
    public const string Interaction = nameof(Interaction);
    public const string RecorderDestination = nameof(RecorderDestination); // What sequence the recorder will warp you in.

    // TileBehavior
    public const string TileBehavior = nameof(TileBehavior);

    public static TiledProperty CreateArgument(string name, string value)
    {
        return new TiledProperty
        {
            Name = $"Argument.{name}",
            Value = value,
        };
    }
}

public enum TiledArgument
{
    None,
    ItemId,
    CellarStairsLeft,
    CellarStairsRight,
}

public static class TiledArgumentProperties
{
    private static string GetPropertyName(TiledArgument name) => $"Argument.{name}";

    public static TiledProperty CreateArgument(TiledArgument name, string value)
    {
        return new TiledProperty
        {
            Name = GetPropertyName(name),
            Value = value,
        };
    }

    public static TiledProperty? GetArgument(this IHasTiledProperties tiled, TiledArgument name)
    {
        return tiled.GetPropertyEntry(GetPropertyName(name));
    }

    public static TiledProperty ExpectArgument(this IHasTiledProperties tiled, TiledArgument name)
    {
        return tiled.GetPropertyEntry(GetPropertyName(name))
            ?? throw new Exception($"Unable to find argument \"{name}\"");
    }
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
public enum Interaction { Unknown, None, Bomb, Burn, Push, PushVertical, Recorder, Touch, TouchOnce, Cover }

[TiledClass]
public sealed class MazeRoom
{
    [JsonConverter(typeof(EnumArrayJsonConverter<Direction>))]
    public Direction[] Path { get; set; }
    public Direction ExitDirection { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameWorldType
{
    Underworld, Overworld, UnderworldCommon, OverworldCommon
}

[TiledClass]
public sealed class Entrance
{
    public GameWorldType DestinationType { get; set; }
    public string Destination { get; set; }
    public PointXY? ExitPosition { get; set; }
    public CaveSpec? Cave { get; set; }
    public BlockType BlockType { get; set; }
    public RoomArguments? Arguments { get; set; }

    public int GetLevelNumber()
    {
        return int.TryParse(Destination, out var levelNumber)
            ? levelNumber : throw new Exception($"Invalid level number \"{Destination}\"");
    }

    public override string ToString() => Destination;
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
public enum InteractionRequirements { None, AllEnemiesDefeated }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InteractionEffect { None, OpenShutterDoors }

public sealed class InteractableBlock
{
    public Interaction Interaction { get; set; }
    public InteractionItemRequirement? ItemRequirement { get; set; }
    public InteractionRequirements Requirements { get; set; }
    public RoomItem? Item { get; set; }
    public TileType? ApparanceBlock { get; set; }
    public Entrance? Entrance { get; set; }
    public InteractionEffect Effect { get; set; }
    // These are root level, not inside CaveSpec, so that we can have an array via multiple properties.
    public CaveShopItem[]? CaveItems { get; set; }
    public ObjType? SpawnedType { get; set; }
    public Raft? Raft { get; set; }
    public bool Repeatable { get; set; }
    public bool Persisted { get; set; }
    public string? Reveals { get; set; }
    public RoomArguments? ArgumentsIn { get; set; }

    public void Initialize(RoomArguments arguments)
    {
        if (Item?.Item == ItemId.ArgumentItemId)
        {
            Item.Item = ArgumentsIn?.ItemId ?? throw new Exception($"{nameof(ItemId.ArgumentItemId)} is used but no argument provided.");
        }
    }
}

[TiledClass]
public sealed class RoomItem
{
    public ItemId Item { get; set; }
    public bool IsRoomItem { get; set; } // UW only.
}

[TiledClass]
public sealed class RoomInformation
{
    public Palette InnerPalette { get; set; }
    public Palette OuterPalette { get; set; }
    public SoundEffect? AmbientSound { get; set; }
    public bool PlaysSecretChime { get; set; }
    public bool IsEntryRoom { get; set; }
    // Used to know when to no longer play the bossroar AmbientSound.
    public bool IsBossRoom { get; set; }
    public bool IsLadderAllowed { get; set; }
    public bool IsDark { get; set; }
    // Only used when something is destroyed or moved.
    public TileType FloorTile { get; set; }
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
}