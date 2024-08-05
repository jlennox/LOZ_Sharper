namespace z1;

internal partial class World
{
    private static readonly byte[] levelGroups = { 0, 0, 1, 1, 0, 1, 0, 1, 2 };

    private readonly record struct EquipValue(byte Value, ItemSlot Slot);

    // The item ID to item slot map is at $6B14, and copied to RAM at $72A4.
    // The item ID to item value map is at $6B38, and copied to RAM at $72C8.
    // They're combined here.
    private static readonly EquipValue[] sItemToEquipment = {
        new(4, ItemSlot.Bombs),
        new(1, ItemSlot.Sword),
        new(2, ItemSlot.Sword),
        new(3, ItemSlot.Sword),
        new(1, ItemSlot.Food),
        new(1, ItemSlot.Recorder),
        new(1, ItemSlot.Candle),
        new(2, ItemSlot.Candle),
        new(1, ItemSlot.Arrow),
        new(2, ItemSlot.Arrow),
        new(1, ItemSlot.Bow),
        new(1, ItemSlot.MagicKey),
        new(1, ItemSlot.Raft),
        new(1, ItemSlot.Ladder),
        new(1, ItemSlot.PowerTriforce),
        new(5, ItemSlot.RupeesToAdd),
        new(1, ItemSlot.Rod),
        new(1, ItemSlot.Book),
        new(1, ItemSlot.Ring),
        new(2, ItemSlot.Ring),
        new(1, ItemSlot.Bracelet),
        new(1, ItemSlot.Letter),
        new(1, ItemSlot.Compass9),
        new(1, ItemSlot.Map9),
        new(1, ItemSlot.RupeesToAdd),
        new(1, ItemSlot.Keys),
        new(1, ItemSlot.HeartContainers),
        new(1, ItemSlot.TriforcePieces),
        new(1, ItemSlot.MagicShield),
        new(1, ItemSlot.Boomerang),
        new(2, ItemSlot.Boomerang),
        new(1, ItemSlot.Potion),
        new(2, ItemSlot.Potion),
        new(1, ItemSlot.Clock),
        new(1, ItemSlot.Bombs),
        new(3, ItemSlot.Bombs),
    };

    private readonly record struct DoorStateBehaviors(TileBehavior Closed, TileBehavior Open);

    private static readonly DoorStateBehaviors[] doorBehaviors = {
        new(TileBehavior.Doorway, TileBehavior.Doorway),     // Open
        new(TileBehavior.Wall, TileBehavior.Wall),           // Wall (None)
        new(TileBehavior.Door, TileBehavior.Door),           // False Wall
        new(TileBehavior.Door, TileBehavior.Door),           // False Wall 2
        new(TileBehavior.Door, TileBehavior.Door),           // Bombable
        new(TileBehavior.Door, TileBehavior.Doorway),        // Key
        new(TileBehavior.Door, TileBehavior.Doorway),        // Key 2
        new(TileBehavior.Door, TileBehavior.Doorway),        // Shutter
    };

    private static readonly Point[] doorMiddles = {
        new(0xE0, 0x98 ),
        new(0x20, 0x98 ),
        new(0x80, 0xD0 ),
        new(0x80, 0x60),
    };

    private static readonly int[] doorSrcYs = { 64, 96, 0, 32 };

    private static readonly Point[] doorPos = {
        new Point(224,  136),
        new Point(0,    136),
        new Point(112,  208),
        new Point(112,  64),
    };

    private readonly record struct DoorStateFaces(byte Closed, byte Open);

    private static readonly DoorStateFaces[] doorFaces = {
        new(0, 0),
        new(3, 3),
        new(3, 3),
        new(3, 3),
        new(3, 4),
        new(1, 0),
        new(1, 0),
        new(2, 0),
    };

    private static readonly Cell[] doorCorners = {
        new(0x0A, 0x1C),
        new(0x0A, 0x02),
        new(0x12, 0x0F),
        new(0x02, 0x0F),
    };

    private static readonly Cell[] behindDoorCorners = {
        new(0x0A, 0x1E),
        new(0x0A, 0x00),
        new(0x14, 0x0F),
        new(0x00, 0x0F),
    };

    private delegate void TileActionDel(int row, int col, TileInteraction interaction);

    private TileActionDel[] sActionFuncs => new TileActionDel[]
    {
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
        BlockTileAction,
    };

    private TileActionDel[] sBehaviorFuncs => new TileActionDel[]
    {
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
        NoneTileAction,
    };

    private Action?[] sModeFuncs => new Action?[]
    {
        null,
        UpdateGameMenu,
        UpdateLoadLevel,
        UpdateUnfurl,
        UpdateEnter,
        UpdatePlay,
        UpdateLeave,
        UpdateScroll,
        UpdateContinueQuestion,
        UpdatePlay,
        UpdateLeaveCellar,
        UpdatePlay,
        null,
        null,
        UpdateRegisterMenu,
        UpdateEliminateMenu,
        UpdateStairsState,
        UpdateDie,
        UpdateEndLevel,
        UpdateWinGame,
        UpdatePlayCellar,
        UpdatePlayCave,
    };

    private Action?[] sDrawFuncs => new Action?[]
    {
        null,
        DrawGameMenu,
        DrawLoadLevel,
        DrawUnfurl,
        DrawEnter,
        DrawPlay,
        DrawLeave,
        DrawScroll,
        DrawContinueQuestion,
        DrawPlay,
        DrawLeaveCellar,
        DrawPlay,
        null,
        null,
        DrawGameMenu,
        DrawGameMenu,
        DrawStairsState,
        DrawDie,
        DrawEndLevel,
        DrawWinGame,
        DrawPlayCellar,
        DrawPlayCave,
    };

    private Action[] sPlayCellarFuncs => new Action[]
    {
        UpdatePlayCellar_Start,
        UpdatePlayCellar_FadeOut,
        UpdatePlayCellar_LoadRoom,
        UpdatePlayCellar_FadeIn,
        UpdatePlayCellar_Walk,
    };

    private Action[] sPlayCaveFuncs => new Action[]
    {
        UpdatePlayCave_Start,
        UpdatePlayCave_Wait,
        UpdatePlayCave_LoadRoom,
        UpdatePlayCave_Walk,
    };

    private Action[] sEndLevelFuncs => new Action[]
    {
        UpdateEndLevel_Start,
        UpdateEndLevel_Wait,
        UpdateEndLevel_Flash,
        UpdateEndLevel_FillHearts,
        UpdateEndLevel_Wait,
        UpdateEndLevel_Furl,
        UpdateEndLevel_Wait,
    };

    private Action[] sWinGameFuncs => new Action[]
    {
        UpdateWinGame_Start,
        UpdateWinGame_Text1,
        UpdateWinGame_Stand,
        UpdateWinGame_Hold1,
        UpdateWinGame_Colors,
        UpdateWinGame_Hold2,
        UpdateWinGame_Text2,
        UpdateWinGame_Hold3,
        UpdateWinGame_NoObjects,
        UpdateWinGame_Credits,
    };

    private Action[] sScrollFuncs => new Action[]
    {
        UpdateScroll_Start,
        UpdateScroll_AnimatingColors,
        UpdateScroll_FadeOut,
        UpdateScroll_LoadRoom,
        UpdateScroll_Scroll,
    };

    private Action[] sDeathFuncs => new Action[]
    {
        UpdateDie_Start,
        UpdateDie_Flash,
        UpdateDie_Wait1,
        UpdateDie_Turn,
        UpdateDie_Fade,
        UpdateDie_GrayLink,
        UpdateDie_Spark,
        UpdateDie_Wait2,
        UpdateDie_GameOver,
    };

    private Action[] sLeaveCellarFuncs => new Action[]
    {
        UpdateLeaveCellar_Start,
        UpdateLeaveCellar_FadeOut,
        UpdateLeaveCellar_LoadRoom,
        UpdateLeaveCellar_FadeIn,
        UpdateLeaveCellar_Walk,
        UpdateLeaveCellar_Wait,
        UpdateLeaveCellar_LoadOverworldRoom,
    };

    private Action[] sEnterFuncs => new Action[]
    {
        UpdateEnter_Start,
        UpdateEnter_Wait,
        UpdateEnter_FadeIn,
        UpdateEnter_Walk,
        UpdateEnter_WalkCave,
    };
}
