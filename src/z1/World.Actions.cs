using System.Collections.Immutable;

namespace z1;

internal partial class World
{
    private static readonly ImmutableArray<byte> _levelGroups = [0, 0, 1, 1, 0, 1, 0, 1, 2];

    public readonly record struct EquipValue(ItemSlot Slot, byte Value, ItemSlot? Max = null, int? MaxValue = null);

    // The item ID to item slot map is at $6B14, and copied to RAM at $72A4.
    // The item ID to item value map is at $6B38, and copied to RAM at $72C8.
    // They're combined here.
    public static readonly ImmutableDictionary<ItemId, EquipValue> ItemToEquipment = new Dictionary<ItemId, EquipValue> {
        { ItemId.Bomb,           new EquipValue(ItemSlot.Bombs,           4, ItemSlot.MaxBombs) },
        { ItemId.WoodSword,      new EquipValue(ItemSlot.Sword,           1) },
        { ItemId.WhiteSword,     new EquipValue(ItemSlot.Sword,           2) },
        { ItemId.MagicSword,     new EquipValue(ItemSlot.Sword,           3) },
        { ItemId.Food,           new EquipValue(ItemSlot.Food,            1) },
        { ItemId.Recorder,       new EquipValue(ItemSlot.Recorder,        1) },
        { ItemId.BlueCandle,     new EquipValue(ItemSlot.Candle,          1) },
        { ItemId.RedCandle,      new EquipValue(ItemSlot.Candle,          2) },
        { ItemId.WoodArrow,      new EquipValue(ItemSlot.Arrow,           1) },
        { ItemId.SilverArrow,    new EquipValue(ItemSlot.Arrow,           2) },
        { ItemId.Bow,            new EquipValue(ItemSlot.Bow,             1) },
        { ItemId.MagicKey,       new EquipValue(ItemSlot.MagicKey,        1) },
        { ItemId.Raft,           new EquipValue(ItemSlot.Raft,            1) },
        { ItemId.Ladder,         new EquipValue(ItemSlot.Ladder,          1) },
        { ItemId.PowerTriforce,  new EquipValue(ItemSlot.PowerTriforce,   1) },
        { ItemId.FiveRupees,     new EquipValue(ItemSlot.RupeesToAdd,     5, ItemSlot.MaxRupees) },
        { ItemId.Rod,            new EquipValue(ItemSlot.Rod,             1) },
        { ItemId.Book,           new EquipValue(ItemSlot.Book,            1) },
        { ItemId.BlueRing,       new EquipValue(ItemSlot.Ring,            1) },
        { ItemId.RedRing,        new EquipValue(ItemSlot.Ring,            2) },
        { ItemId.Bracelet,       new EquipValue(ItemSlot.Bracelet,        1) },
        { ItemId.Letter,         new EquipValue(ItemSlot.Letter,          1) },
        { ItemId.Rupee,          new EquipValue(ItemSlot.RupeesToAdd,     1, ItemSlot.MaxRupees) },
        { ItemId.Key,            new EquipValue(ItemSlot.Keys,            1) },
        { ItemId.HeartContainer, new EquipValue(ItemSlot.HeartContainers, 1) },
        { ItemId.TriforcePiece,  new EquipValue(ItemSlot.TriforcePieces,  1) },
        { ItemId.MagicShield,    new EquipValue(ItemSlot.MagicShield,     1) },
        { ItemId.WoodBoomerang,  new EquipValue(ItemSlot.Boomerang,       1) },
        { ItemId.MagicBoomerang, new EquipValue(ItemSlot.Boomerang,       2) },
        { ItemId.BluePotion,     new EquipValue(ItemSlot.Potion,          1, null, 2) },
        { ItemId.RedPotion,      new EquipValue(ItemSlot.Potion,          2, null, 2) },
        { ItemId.Clock,          new EquipValue(ItemSlot.Clock,           1) },
        { ItemId.Heart,          new EquipValue(ItemSlot.None,            1) },
        { ItemId.Fairy,          new EquipValue(ItemSlot.None,            3) },
        { ItemId.MaxBombs,       new EquipValue(ItemSlot.MaxBombs,        4) },
    }.ToImmutableDictionary();

    private readonly record struct DoorStateBehaviors(TileBehavior Closed, TileBehavior Open)
    {
        public TileBehavior GetBehavior(bool isOpen) => isOpen ? Open : Closed;
    }

    private static readonly ImmutableArray<DoorStateBehaviors> _doorBehaviors = [
        new DoorStateBehaviors(TileBehavior.Doorway, TileBehavior.Doorway),     // Open
        new DoorStateBehaviors(TileBehavior.Wall, TileBehavior.Wall),           // Wall (None)
        new DoorStateBehaviors(TileBehavior.Door, TileBehavior.Door),           // False Wall
        new DoorStateBehaviors(TileBehavior.Door, TileBehavior.Door),           // False Wall 2
        new DoorStateBehaviors(TileBehavior.Door, TileBehavior.Door),           // Bombable
        new DoorStateBehaviors(TileBehavior.Door, TileBehavior.Doorway),        // Key
        new DoorStateBehaviors(TileBehavior.Door, TileBehavior.Doorway),        // Key 2
        new DoorStateBehaviors(TileBehavior.Door, TileBehavior.Doorway)         // Shutter
    ];

    private static readonly Dictionary<Direction, Point> _doorMiddles = new() {
        { Direction.Right, new Point(0xE0, 0x98) },
        { Direction.Left, new Point(0x20, 0x98) },
        { Direction.Down, new Point(0x80, 0xD0) },
        { Direction.Up, new Point(0x80, 0x60) },
    };

    private readonly record struct DoorPosition(int SourceY, int X, int Y);

    private static readonly Dictionary<Direction, DoorPosition> _doorPos = new() {
        { Direction.Right, new DoorPosition(64, 224, 136) },
        { Direction.Left, new DoorPosition(96, 0,   136) },
        { Direction.Down, new DoorPosition(0, 112, 208) },
        { Direction.Up, new DoorPosition(32, 112, 64) },
    };

    private enum DoorState { Open, Locked, Shutter, Wall, Bombed }
    private readonly record struct DoorStateFaces(DoorState Closed, DoorState Open);
    private static DoorStateFaces GetDoorFace(DoorType type) => type switch
    {
        DoorType.Open => new DoorStateFaces(DoorState.Open, DoorState.Open),
        DoorType.Wall => new DoorStateFaces(DoorState.Wall, DoorState.Wall),
        DoorType.FalseWall => new DoorStateFaces(DoorState.Wall, DoorState.Wall),
        DoorType.FalseWall2 => new DoorStateFaces(DoorState.Wall, DoorState.Wall),
        DoorType.Bombable => new DoorStateFaces(DoorState.Wall, DoorState.Bombed),
        DoorType.Key => new DoorStateFaces(DoorState.Locked, DoorState.Open),
        DoorType.Key2 => new DoorStateFaces(DoorState.Locked, DoorState.Open),
        DoorType.Shutter => new DoorStateFaces(DoorState.Shutter, DoorState.Open),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported door type.")
    };

    private static readonly ImmutableArray<Cell> _doorCorners = [
        new Cell(0x0A, 0x1C),
        new Cell(0x0A, 0x02),
        new Cell(0x12, 0x0F),
        new Cell(0x02, 0x0F)
    ];

    private static readonly ImmutableArray<Cell> _behindDoorCorners = [
        new Cell(0x0A, 0x1E),
        new Cell(0x0A, 0x00),
        new Cell(0x14, 0x0F),
        new Cell(0x00, 0x0F)
    ];

    private delegate void TileActionDel(int tileY, int tileX, TileInteraction interaction);

    private TileActionDel GetTileActionFunction(TileAction action) => action switch
    {
        TileAction.None => NoneTileAction,
        TileAction.Push => PushTileAction,
        TileAction.Bomb => BombTileAction,
        TileAction.Burn => BurnTileAction,
        TileAction.PushHeadstone => HeadstoneTileAction,
        TileAction.Ladder => LadderTileAction,
        TileAction.Raft => RaftTileAction,
        TileAction.Cave => CaveTileAction,
        TileAction.Stairs => StairsTileAction,
        TileAction.Ghost => GhostTileAction,
        TileAction.Armos => ArmosTileAction,
        TileAction.PushBlock => BlockTileAction,
        TileAction.Recorder => RecorderTileAction,

        TileAction.RecorderDestination => NoneTileAction,
        TileAction.Item => NoneTileAction,

        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action type.")
    };

    private ImmutableArray<TileActionDel> BehaviorFuncs => [
        NoneTileAction,
        NoneTileAction,
        NoneTileAction,
        StairsTileAction,
        NoneTileAction,

        NoneTileAction,
        NoneTileAction,
        CaveTileAction,
        GhostTileAction,
        GhostTileAction,
        GhostTileAction,
        GhostTileAction,
        GhostTileAction,
        GhostTileAction,
        GhostTileAction,
        GhostTileAction,
        GhostTileAction,
        GhostTileAction,
        GhostTileAction,
        GhostTileAction,
        GhostTileAction,
        GhostTileAction,
        GhostTileAction,
        GhostTileAction,
        ArmosTileAction,
        ArmosTileAction,
        ArmosTileAction,
        ArmosTileAction,
        ArmosTileAction,
        ArmosTileAction,
        ArmosTileAction,
        ArmosTileAction,
        ArmosTileAction,
        ArmosTileAction,
        ArmosTileAction,
        ArmosTileAction,
        ArmosTileAction,
        ArmosTileAction,
        ArmosTileAction,
        ArmosTileAction,
        DoorTileAction,
        NoneTileAction
    ];

    private Action? GetUpdateFunction(GameMode mode) => mode switch
    {
        GameMode.Demo => null,
        GameMode.LoadLevel => UpdateLoadLevel,
        GameMode.Unfurl => UpdateUnfurl,
        GameMode.Enter => UpdateEnter,
        GameMode.Play => UpdatePlay,
        GameMode.Leave => UpdateLeave,
        GameMode.Scroll => UpdateScroll,
        GameMode.ContinueQuestion => UpdateContinueQuestion,
        GameMode.PlayCellar => UpdatePlay,
        GameMode.LeaveCellar => UpdateLeaveCellar,
        GameMode.PlayCave => UpdatePlay,
        GameMode.PlayShortcuts => null,
        GameMode.Stairs => UpdateStairsState,
        GameMode.Death => UpdateDie,
        GameMode.EndLevel => UpdateEndLevel,
        GameMode.WinGame => UpdateWinGame,
        GameMode.InitPlayCellar => UpdatePlayCellar,
        GameMode.InitPlayCave => UpdatePlayCave,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid game mode.")
    };

    private Action? GetDrawFunction(GameMode mode) => mode switch
    {
        GameMode.Demo => null,
        GameMode.LoadLevel => DrawLoadLevel,
        GameMode.Unfurl => DrawUnfurl,
        GameMode.Enter => DrawEnter,
        GameMode.Play => DrawPlay,
        GameMode.Leave => DrawLeave,
        GameMode.Scroll => DrawScroll,
        GameMode.ContinueQuestion => DrawContinueQuestion,
        GameMode.PlayCellar => DrawPlay,
        GameMode.LeaveCellar => DrawLeaveCellar,
        GameMode.PlayCave => DrawPlay,
        GameMode.PlayShortcuts => null,
        GameMode.Stairs => DrawStairsState,
        GameMode.Death => DrawDie,
        GameMode.EndLevel => DrawEndLevel,
        GameMode.WinGame => DrawWinGame,
        GameMode.InitPlayCellar => DrawPlayCellar,
        GameMode.InitPlayCave => DrawPlayCave,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid game mode.")
    };
}
