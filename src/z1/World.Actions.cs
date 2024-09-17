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

    private readonly record struct DoorStateBehaviors(TileBehavior Closed, TileBehavior Open);

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

    private static readonly ImmutableArray<Point> _doorMiddles = [
        new Point(0xE0, 0x98),
        new Point(0x20, 0x98),
        new Point(0x80, 0xD0),
        new Point(0x80, 0x60)
    ];

    private static readonly ImmutableArray<int> _doorSrcYs = [64, 96, 0, 32];

    private static readonly ImmutableArray<Point> _doorPos = [
        new Point(224, 136),
        new Point(0,   136),
        new Point(112, 208),
        new Point(112, 64)
    ];

    private readonly record struct DoorStateFaces(byte Closed, byte Open);

    private static readonly ImmutableArray<DoorStateFaces> _doorFaces = [
        new DoorStateFaces(0, 0),
        new DoorStateFaces(3, 3),
        new DoorStateFaces(3, 3),
        new DoorStateFaces(3, 3),
        new DoorStateFaces(3, 4),
        new DoorStateFaces(1, 0),
        new DoorStateFaces(1, 0),
        new DoorStateFaces(2, 0)
    ];

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

    private delegate void TileActionDel(int row, int col, TileInteraction interaction);

    private ImmutableArray<TileActionDel> ActionFuncs => [
        NoneTileAction,
        PushTileAction,
        BombTileAction,
        BurnTileAction,
        HeadstoneTileAction,
        LadderTileAction,
        RaftTileAction,
        CaveTileAction,
        StairsTileAction,
        GhostTileAction,
        ArmosTileAction,
        BlockTileAction
    ];

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
        GameMode.GameMenu => UpdateGameMenu,
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
        GameMode.UnknownD__ => null,
        GameMode.Register => UpdateRegisterMenu,
        GameMode.Elimination => UpdateEliminateMenu,
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
        GameMode.GameMenu => DrawGameMenu,
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
        GameMode.UnknownD__ => null,
        GameMode.Register => DrawGameMenu,
        GameMode.Elimination => DrawGameMenu,
        GameMode.Stairs => DrawStairsState,
        GameMode.Death => DrawDie,
        GameMode.EndLevel => DrawEndLevel,
        GameMode.WinGame => DrawWinGame,
        GameMode.InitPlayCellar => DrawPlayCellar,
        GameMode.InitPlayCave => DrawPlayCave,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid game mode.")
    };

    private ImmutableArray<Action> PlayCellarFuncs => [
        UpdatePlayCellar_Start,
        UpdatePlayCellar_FadeOut,
        UpdatePlayCellar_LoadRoom,
        UpdatePlayCellar_FadeIn,
        UpdatePlayCellar_Walk
    ];

    private ImmutableArray<Action> PlayCaveFuncs => [
        UpdatePlayCave_Start,
        UpdatePlayCave_Wait,
        UpdatePlayCave_LoadRoom,
        UpdatePlayCave_Walk
    ];

    private ImmutableArray<Action> EndLevelFuncs => [
        UpdateEndLevel_Start,
        UpdateEndLevel_Wait,
        UpdateEndLevel_Flash,
        UpdateEndLevel_FillHearts,
        UpdateEndLevel_Wait,
        UpdateEndLevel_Furl,
        UpdateEndLevel_Wait
    ];

    private ImmutableArray<Action> WinGameFuncs => [
        UpdateWinGame_Start,
        UpdateWinGame_Text1,
        UpdateWinGame_Stand,
        UpdateWinGame_Hold1,
        UpdateWinGame_Colors,
        UpdateWinGame_Hold2,
        UpdateWinGame_Text2,
        UpdateWinGame_Hold3,
        UpdateWinGame_NoObjects,
        UpdateWinGame_Credits
    ];

    private ImmutableArray<Action> ScrollFuncs => [
        UpdateScroll_Start,
        UpdateScroll_AnimatingColors,
        UpdateScroll_FadeOut,
        UpdateScroll_LoadRoom,
        UpdateScroll_Scroll
    ];

    private ImmutableArray<Action> DeathFuncs => [
        UpdateDie_Start,
        UpdateDie_Flash,
        UpdateDie_Wait1,
        UpdateDie_Turn,
        UpdateDie_Fade,
        UpdateDie_GrayLink,
        UpdateDie_Spark,
        UpdateDie_Wait2,
        UpdateDie_GameOver
    ];

    private ImmutableArray<Action> LeaveCellarFuncs => [
        UpdateLeaveCellar_Start,
        UpdateLeaveCellar_FadeOut,
        UpdateLeaveCellar_LoadRoom,
        UpdateLeaveCellar_FadeIn,
        UpdateLeaveCellar_Walk,
        UpdateLeaveCellar_Wait,
        UpdateLeaveCellar_LoadOverworldRoom
    ];

    private ImmutableArray<Action> EnterFuncs => [
        UpdateEnter_Start,
        UpdateEnter_Wait,
        UpdateEnter_FadeIn,
        UpdateEnter_Walk,
        UpdateEnter_WalkCave
    ];
}
