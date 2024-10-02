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
    // public const string IsEntryRoom = nameof(IsEntryRoom);
    // public const string AmbientSound = nameof(AmbientSound);
    // public const string IsLadderAllowed = nameof(IsLadderAllowed);

    // Overworld
    public const string MonstersEnter = nameof(MonstersEnter);
    public const string Maze = nameof(Maze);
    // public const string MazeExit = nameof(MazeExit);
    public const string RoomInformation = nameof(RoomInformation);
    // public const string PlaysSecretChime = nameof(PlaysSecretChime);
    // public const string CaveId = nameof(CaveId);
    // public const string RoomItemId = nameof(RoomItemId);

    // Underworld
    public const string DungeonDoors = nameof(DungeonDoors);
    // public const string IsDark = nameof(IsDark);
    public const string Secret = nameof(Secret);
    public const string ItemId = nameof(ItemId);
    public const string ItemPosition = nameof(ItemPosition);
    public const string FireballLayout = nameof(FireballLayout);
    // public const string IsBossRoom = nameof(IsBossRoom);
    public const string CellarItem = nameof(CellarItem);
    public const string CellarStairsLeft = nameof(CellarStairsLeft);
    public const string CellarStairsRight = nameof(CellarStairsRight);
    public const string HiddenFromMap = nameof(HiddenFromMap);

    public static readonly ImmutableArray<Direction> DoorDirectionOrder = [Direction.Right, Direction.Left, Direction.Down, Direction.Up];

    // Action
    // public const string TileAction = nameof(TileAction);
    // public const string Enters = nameof(Enters);
    // public const string ExitPosition = nameof(ExitPosition);
    // public const string Owner = nameof(Owner);
    // public const string Reveals = nameof(Reveals);
    public const string Interaction = nameof(Interaction);
    // public const string Raft = nameof(Raft);
    public const string Argument = nameof(Argument);
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
public enum Interaction { Unknown, None, Bomb, Burn, Push, Recorder, Touch, Cover }

[TiledClass]
public sealed class MazeRoom
{
    [JsonConverter(typeof(EnumArrayJsonConverter<Direction>))]
    public Direction[] Path { get; set; }
    public Direction ExitDirection { get; set; }
}

// When this block is interacted with,
public interface IInteractable
{
    Interaction Interaction { get; set; }
    RoomItem? Item { get; set; }
    Entrance? Entrance { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EntranceType
{
    Level, Cave, World
}

[TiledClass]
public sealed class Entrance
{
    public EntranceType DestinationType { get; set; }
    public string Destination { get; set; }
    public PointXY ExitPosition { get; set; }
    public CaveSpec? Cave { get; set; }

    public bool TryGetLevelNumber(out int levelNumber)
    {
        return int.TryParse(Destination, out levelNumber);
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

public sealed class InteractableBlock : IInteractable
{
    public Interaction Interaction { get; set; }
    public RoomItem? Item { get; set; }
    public Entrance? Entrance { get; set; }
    public CaveShopItem[]? CaveItems { get; set; } // These are here so that we can have an array.
    public ObjType? SpawnedType { get; set; }
    public Raft? Raft { get; set; }
    public bool Repeatable { get; set; }
    public bool Persisted { get; set; }
    public string? Reveals { get; set; }
}
// public sealed class InteractableBlock : IInteractable
// {
//     public Interaction Interaction { get; set; }
//     public RoomItem? RoomItem { get; set; }
//     public Entrance? Entrance { get; set; }
//     public ObjType? SpawnedType { get; set; }
//     public Raft? Raft { get; set; }
//     public bool Repeatable { get; set; }
//     public bool Persisted { get; set; }
//     public string? Reveals { get; set; }
// }

// Interaction causes SpawnedType to spawn. RemoveSpawner is used to remove the spawner after the object is spawned.
[TiledClass]
public sealed class Spawner : IInteractable
{
    public Interaction Interaction { get; set; }
    public RoomItem? Item { get; set; }
    public Entrance? Entrance { get; set; }
    public ObjType SpawnedType { get; set; }
    public bool RemoveSpawner { get; set; }
}

[TiledClass]
public sealed class RoomItem
{
    public ItemId Item { get; set; }
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
}

[TiledClass]
public sealed class Raft
{
    public Direction Direction { get; set; }
    public string? Destination { get; set; }
}

[TiledClass]
public sealed class RecorderDestination
{
    // What sequence the recorder will warp you in.
    public int Slot { get; set; }
}