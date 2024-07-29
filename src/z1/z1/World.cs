using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;
using z1.Actors;
using z1.UI;

namespace z1;

internal enum DoorType { Open, None, FalseWall, FalseWall2, Bombable, Key, Key2, Shutter }
internal enum TileInteraction { Load, Push, Touch, Cover }
internal enum SpritePriority { None, AboveBg, BelowBg }
internal enum SubmenuState { IdleClosed, StartOpening, EndOpening = 7, IdleOpen, StartClose }

internal record Cell(byte Row, byte Col)
{
    public const int MobPatchCellCount = 16;
    public static Cell[] MakeMobPatchCell() => new Cell[MobPatchCellCount];
}

internal sealed class TileMap
{
    public const int Size = World.Rows * World.Columns;

    private readonly byte[] _tileRefs = new byte[Size];
    private readonly byte[] _tileBehaviors = new byte[World.Rows * World.Columns];

    public ref byte Refs(int index) => ref _tileRefs[index];
    public ref byte Refs(int row, int col) => ref _tileRefs[row * World.Columns + col];
    public ref byte Behaviors(int row, int col) => ref _tileBehaviors[row * World.Columns + col];
    public ref byte Behaviors(int index) => ref _tileBehaviors[index];
    public TileBehavior AsBehaviors(int row, int col)
    {
        // JOE: TODO: Think this through properly. The checks should be against World.Rows/World.Columns.
        if (row > 15)
        {
            Debug.WriteLine($"Row greater than 15 being accessed {row}.");
            row = 15;
        }

        return (TileBehavior)_tileBehaviors[row * World.Columns + col];
    }
}

internal sealed unsafe partial class World
{
    public const int LevelGroups = 3;

    internal enum Cave { Items = 0x79, Shortcut = 0x7A, }
    internal enum Secret { None, FoesDoor, Ringleader, LastBoss, BlockDoor, BlockStairs, MoneyOrLife, FoesItem }
    internal enum TileScheme { Overworld, UnderworldMain, UnderworldCellar }
    internal enum UniqueRoomIds { TopRightOverworldSecret = 0x0F }

    public const int MobRows = 11;
    public const int MobColumns = 16;
    public const int Rows = 22;
    public const int Columns = 32;
    public const int BaseRows = 8;
    public const int MobTileWidth = 16;
    public const int MobTileHeight = 16;
    public const int TileWidth = 8;
    public const int TileHeight = 8;
    public const int TileMapWidth = Columns * TileWidth;
    public const int TileMapHeight = Rows * TileHeight;
    public const int TileMapBaseY = 64;
    public const int Doors = 4;

    public const int WorldLimitLeft = 0;
    public const int WorldLimitRight = TileMapWidth;
    public const int WorldLimitTop = TileMapBaseY;
    public const int WorldLimitBottom = WorldLimitTop + TileMapHeight;

    public const int WorldMidX = WorldLimitLeft + TileMapWidth / 2;

    public const int WorldWidth = 16;
    public const int WorldHeight = 8;

    public const int StartX = 0x78;
    public const int FirstCaveIndex = 0x10;
    public const int TriforcePieceX = 0x78;

    private const int Rooms = 128;
    private const int UniqueRooms = 124;
    private const int ColumnTables = 16;
    private const int ScrollSpeed = 4;
    private const int MobTypes = 56;
    private const int TileTypes = 256;
    private const int TileActions = 16;
    private const int LoadingTileActions = 4;
    private const int SparseAttrs = 11;
    private const int RoomHistoryLength = 6; // JOE: TODO: Nuke this and just use the array length.
    private const int Modes = (int)GameMode.Max;

    private const int OWMarginRight = 0xE0;
    private const int OWMarginLeft = 0x10;
    private const int OWMarginTop = 0x4D;
    private const int OWMarginBottom = 0xCD;

    private const int UWMarginRight = 0xD0;
    private const int UWMarginLeft = 0x20;
    private const int UWMarginTop = 0x5D;
    private const int UWMarginBottom = 0xBD;

    private const int UWBorderRight = 0xE0;
    private const int UWBorderLeft = 0x20;
    private const int UWBorderTop = 0x60;
    private const int UWBorderBottom = 0xD0;

    private const int UWBlockRow = 10;

    private const int DoorWidth = 32;
    private const int DoorHeight = 32;
    private const int DoorOverlayBaseY = 128;
    private const int DoorUnderlayBaseY = 0;

    private const int DoorHoleCoordH = 0x90;
    private const int DoorHoleCoordV = 0x78;

    private const int UWBombRadius = 32;

    public delegate void LoadMobDelegate(ref TileMap map, int row, int col, int mobIndex);

    public readonly OnScreenDisplay OnScreenDisplay = new();

    public LevelDirectory Directory;
    public LevelInfoBlock InfoBlock;
    public RoomCols[] RoomCols = new RoomCols[UniqueRooms];
    public TableResource<byte> ColTables;
    public readonly TileMap[] TileMaps = { new(), new(), new() };
    public RoomAttrs[] RoomAttrs = new RoomAttrs[Rooms];
    public int CurRoomId;
    public int CurTileMapIndex;
    public byte[] TileAttrs = new byte[MobTypes];
    public byte[] TileBehaviors = new byte[TileTypes];
    public TableResource<byte> SparseRoomAttrs;
    public TableResource<byte> ExtraData;
    public TableResource<byte> ObjLists;
    public TableResource<byte> TextTable;
    public ListResource<byte> PrimaryMobs;
    public ListResource<byte> SecondaryMobs;
    public LoadMobDelegate LoadMobFunc;

    public int RowCount;
    public int ColCount;
    public int StartRow;
    public int StartCol;
    public int TileTypeCount;
    // JOE: TODO: Move to rect.
    public int MarginRight;
    public int MarginLeft;
    public int MarginBottom;
    public int MarginTop;
    public SKBitmap? WallsBmp;
    public SKBitmap? DoorsBmp;

    public GameMode LastMode;
    public GameMode CurMode;
    public StatusBar StatusBar;
    public SubmenuType Menu;
    public CreditsType? Credits;
    public TextBox? TextBox1;
    public TextBox? TextBox2;
    public Menu? GameMenu;
    public Menu? NextGameMenu;

    internal enum PauseState { Unpaused, Paused, FillingHearts }

    public readonly WorldState State = new();
    public int CurColorSeqNum;
    public int DarkRoomFadeStep;
    public int CurMazeStep;
    private int _spotIndex;
    public int TempShutterRoomId;
    public Direction TempShutterDoorDir;
    public int TempShuttersRoomId;
    public bool TempShutters;
    public bool PrevRoomWasCellar;
    public int SavedOWRoomId;
    public int EdgeX;
    public int EdgeY;
    public int NextRoomHistorySlot;    // 620
    public int RoomObjCount;           // 34E
    //public int RoomObjId;              // 35F
    public Actor? RoomObj;              // 35F
    public byte WorldKillCycle;         // 52A
    public byte WorldKillCount;         // 627
    public byte HelpDropCounter;        // 50
    public byte HelpDropValue;          // 51
    public int RoomKillCount;          // 34F
    public bool RoomAllDead;            // 34D
    public bool MadeRoomItem;
    public bool EnablePersonFireballs;
    public bool SwordBlocked;           // 52E
    public byte WhirlwindTeleporting;   // 522
    public byte TeleportingRoomIndex;   // 523
    public PauseState Pause;                  // E0
    public SubmenuState Submenu;                // E1
    public int SubmenuOffsetY;         // EC
    public bool StatusBarVisible;
    public int[] LevelKillCounts = new int[(int)LevelBlock.Rooms];
    public byte[] RoomHistory = new byte[RoomHistoryLength];

    public Link Player => Game.Link;
    public bool GiveFakePlayerPos;
    public Point FakePlayerPos;

    public Actor?[] Objects = new Actor[(int)ObjectSlot.MaxObjects];
    public Actor?[] ObjectsToDelete = new Actor[(int)ObjectSlot.MaxObjects];
    public int ObjectsToDeleteCount;
    public int[] ObjectTimers = new int[(int)ObjectSlot.MaxObjects];
    public int CurObjSlot;
    public ObjectSlot CurObjectSlot
    {
        get => (ObjectSlot)CurObjSlot;
        set => CurObjSlot = (int)value;
    }
    public int LongTimer;
    public int[] StunTimers = new int[(int)ObjectSlot.MaxObjects];
    public byte[] PlaceholderTypes = new byte[(int)ObjectSlot.MaxObjects];

    public Direction DoorwayDir;         // 53
    public int TriggeredDoorCmd;   // 54
    public Direction TriggeredDoorDir;   // 55
    public int FromUnderground;    // 5A
    // JOE: TODO: ActiveShots doesn't need to be reference counted anymore and should be based on the object table.
    // Though note that ones owned by Link should be excluded.
    public int ActiveShots;        // 34C
    public bool TriggerShutters;    // 4CE
    public bool SummonedWhirlwind;  // 508
    public bool PowerTriforceFanfare;   // 509
    public int RecorderUsed;       // 51B
    public bool CandleUsed;         // 513
    public Direction ShuttersPassedDirs; // 519
    public bool BrightenRoom;       // 51E
    public PlayerProfile Profile { get; private set; }
    public UWRoomFlags[] CurUWBlockFlags = { };
    public int GhostCount;
    public int ArmosCount;
    public Cell[] GhostCells = Cell.MakeMobPatchCell();
    public Cell[] ArmosCells = Cell.MakeMobPatchCell();

    private UWRoomAttrs CurrentUWRoomAttrs => GetUWRoomAttrs(CurRoomId);
    private OWRoomAttrs CurrentOWRoomAttrs => GetOWRoomAttrs(CurRoomId);

    private UWRoomAttrs GetUWRoomAttrs(int roomId) => RoomAttrs[roomId];
    private OWRoomAttrs GetOWRoomAttrs(int roomId) => RoomAttrs[roomId];

    public Game Game { get; }

    public World(Game game)
    {
        Game = game;
        StatusBar = new StatusBar(this);
        Menu = new SubmenuType(game);

        LastMode = GameMode.Demo;
        CurMode = GameMode.Play;
        EdgeY = 0x40;

        Init();
    }

    private void LoadOpenRoomContext()
    {
        ColCount = 32;
        RowCount = 22;
        StartRow = 0;
        StartCol = 0;
        TileTypeCount = 56;
        MarginRight = OWMarginRight;
        MarginLeft = OWMarginLeft;
        MarginBottom = OWMarginBottom;
        MarginTop = OWMarginTop;
    }

    private void LoadClosedRoomContext()
    {
        ColCount = 24;
        RowCount = 14;
        StartRow = 4;
        StartCol = 4;
        TileTypeCount = 9;
        MarginRight = UWMarginRight;
        MarginLeft = UWMarginLeft;
        MarginBottom = UWMarginBottom;
        MarginTop = UWMarginTop;
    }

    private void LoadMapResourcesFromDirectory(int uniqueRoomCount)
    {
        RoomCols = ListResource<RoomCols>.LoadList(Directory.RoomCols.ToString(), uniqueRoomCount).ToArray();
        ColTables = TableResource<byte>.Load(Directory.ColTables.ToString());
        TileAttrs = ListResource<byte>.LoadList(Directory.TileAttrs.ToString(), TileTypeCount).ToArray();

        Graphics.LoadTileSheet(TileSheet.Background, Directory.TilesImage.ToString());
    }

    private void LoadOverworldContext()
    {
        LoadOpenRoomContext();
        LoadMapResourcesFromDirectory(124);
        PrimaryMobs = ListResource<byte>.Load("owPrimaryMobs.list");
        SecondaryMobs = ListResource<byte>.Load("owSecondaryMobs.list");
        TileBehaviors = ListResource<byte>.LoadList("owTileBehaviors.dat", TileTypes).ToArray();
    }

    private void LoadUnderworldContext()
    {
        LoadClosedRoomContext();
        LoadMapResourcesFromDirectory(64);
        PrimaryMobs = ListResource<byte>.Load("uwPrimaryMobs.list");
        TileBehaviors = ListResource<byte>.LoadList("uwTileBehaviors.dat", TileTypes).ToArray();
    }

    private void LoadCellarContext()
    {
        LoadOpenRoomContext();

        RoomCols = ListResource<RoomCols>.LoadList("underworldCellarRoomCols.dat", 2).ToArray();
        ColTables = TableResource<byte>.Load("underworldCellarCols.tab");

        TileAttrs = ListResource<byte>.LoadList("underworldCellarTileAttrs.dat", TileTypeCount).ToArray();

        Graphics.LoadTileSheet(TileSheet.Background, "underworldTiles.png");

        PrimaryMobs = ListResource<byte>.Load("uwCellarPrimaryMobs.list");
        SecondaryMobs = ListResource<byte>.Load("uwCellarSecondaryMobs.list");
        TileBehaviors = ListResource<byte>.LoadList("uwTileBehaviors.dat", TileTypes).ToArray();
    }

    private void LoadLevel(int level)
    {
        var levelDirName = $"levelDir_{Profile.Quest}_{level}.dat";

        Directory = ListResource<LevelDirectory>.LoadSingle(levelDirName);
        InfoBlock = ListResource<LevelInfoBlock>.LoadSingle(Directory.LevelInfoBlock.ToString());

        WallsBmp?.Dispose();
        WallsBmp = null;
        DoorsBmp?.Dispose();
        DoorsBmp = null;

        TempShutterRoomId = 0;
        TempShutterDoorDir = 0;
        TempShuttersRoomId = 0;
        TempShutters = false;
        PrevRoomWasCellar = false;
        DarkRoomFadeStep = 0;
        Array.Clear(LevelKillCounts);
        Array.Clear(RoomHistory);
        WhirlwindTeleporting = 0;

        if (level == 0)
        {
            LoadOverworldContext();
            CurUWBlockFlags = null; // JOE: TODO: This seems wrong.
        }
        else
        {
            LoadUnderworldContext();
            WallsBmp = SKBitmap.Decode(Directory.Extra2.FullPath());
            DoorsBmp = SKBitmap.Decode(Directory.Extra3.FullPath());
            CurUWBlockFlags = level < 7 ? Profile.LevelFlags1 : Profile.LevelFlags2;

            foreach (var tileMap in TileMaps)
            {
                for (var x = 0; x < TileMap.Size; x++)
                {
                    tileMap.Refs(x) = (byte)BlockObjType.TileWallEdge;
                }
            }
        }

        Graphics.LoadTileSheet(TileSheet.PlayerAndItems, Directory.PlayerImage.ToString(), Directory.PlayerSheet.ToString());
        Graphics.LoadTileSheet(TileSheet.Npcs, Directory.NpcImage.ToString(), Directory.NpcSheet.ToString());

        if (!Directory.BossImage.IsNull)
        {
            Graphics.LoadTileSheet(TileSheet.Boss, Directory.BossImage.ToString(), Directory.BossSheet.ToString());
        }

        RoomAttrs = ListResource<RoomAttrs>.LoadList(Directory.RoomAttrs.ToString(), Rooms).ToArray();
        ExtraData = TableResource<byte>.Load(Directory.LevelInfoEx.ToString());
        ObjLists = TableResource<byte>.Load(Directory.ObjLists.ToString());
        SparseRoomAttrs = TableResource<byte>.Load(Directory.Extra1.ToString());

        var facing = Game.Link?.Facing ?? Direction.Up;

        Game.Link = new Link(Game, facing);

        // Replace room attributes, if in second quest.

        if (level == 0 && Profile.Quest == 1)
        {
            var pReplacement = SparseRoomAttrs.GetItems<byte>(Sparse.RoomReplacement);
            int replacementCount = pReplacement[0];
            var sparseAttr = MemoryMarshal.Cast<byte, SparseRoomAttr>(pReplacement[2..]); // JOE: Go until replacementCount * sizeof(SparseRoomAttr) ??

            for (var i = 0; i < replacementCount; i++)
            {
                int roomId = sparseAttr[i].roomId;
                RoomAttrs[roomId] = sparseAttr[i].attrs;
            }
        }
    }

    private void Init()
    {
        var sysPal = ListResource<int>.LoadList("pal.dat", Global.SysPaletteLength).ToArray();
        Graphics.LoadSystemPalette(sysPal);

        Graphics.LoadTileSheet(TileSheet.Font, "font.png");
        Graphics.LoadTileSheet(TileSheet.PlayerAndItems, "playerItem.png", "playerItemsSheet.tab");

        TextTable = TableResource<byte>.Load("text.tab");

        GotoFileMenu();
    }

    public void Start(PlayerProfile profile)
    {
        Profile = profile;
        Profile.Hearts = PlayerProfile.GetMaxHeartsValue(PlayerProfile.DefaultHearts);

        GotoLoadLevel(0, true);
    }

    public void Update()
    {
        var mode = GetMode();

        if (LastMode != mode)
        {
            if (IsPlaying(LastMode) && mode != GameMode.WinGame)
            {
                CleanUpRoomItems();
                Graphics.DisableGrayscale();
                if (mode != GameMode.Unfurl)
                {
                    OnLeavePlay();
                    Game.Link?.Stop();
                }
            }

            LastMode = mode;

            GameMenu = NextGameMenu;
            NextGameMenu = null;
        }

        sModeFuncs[(int)CurMode]!();
    }

    public void Draw()
    {
        if (StatusBarVisible)
        {
            StatusBar.Draw(SubmenuOffsetY);
        }

        sDrawFuncs[(int)CurMode]!();

        OnScreenDisplay.Draw();
    }

    private void DrawRoom()
    {
        DrawMap(CurRoomId, CurTileMapIndex, 0, 0);
    }

    public void PauseFillHearts()
    {
        Pause = PauseState.FillingHearts;
    }

    public void LeaveRoom(Direction dir, int roomId)
    {
        GotoLeave(dir, roomId);
    }

    public void LeaveCellar()
    {
        GotoLeaveCellar();
    }

    public void LeaveCellarByShortcut(int targetRoomId)
    {
        CurRoomId = targetRoomId;
        TakeShortcut();
        LeaveCellar();
    }

    public void UnfurlLevel() => GotoUnfurl();
    public void ChooseFile(PlayerProfile[] summaries) => GotoFileMenu(summaries);
    public void RegisterFile(PlayerProfile[] summaries) => GotoRegisterMenu(summaries);
    public void EliminateFile(PlayerProfile[] summaries) => GotoEliminateMenu(summaries);
    private bool IsPlaying() => IsPlaying(CurMode);
    private static bool IsPlaying(GameMode mode) => mode is GameMode.Play or GameMode.PlayCave or GameMode.PlayCellar or GameMode.PlayShortcuts;
    private bool IsPlayingCave() => GetMode() == GameMode.PlayCave;

    public GameMode GetMode() => CurMode switch
    {
        GameMode.InitPlayCave => GameMode.PlayCave,
        GameMode.InitPlayCellar => GameMode.PlayCellar,
        _ => CurMode
    };

    public Point GetObservedPlayerPos()
    {
        return FakePlayerPos;
    }

    public LadderActor? GetLadder()
    {
        return GetLadderObj();
    }

    public void SetLadder(LadderActor? ladder)
    {
        SetLadderObj(ladder);
    }

    public void UseRecorder()
    {
        Game.Sound.PushSong(SongId.Recorder);
        ObjectTimers[(int)ObjectSlot.FluteMusic] = 0x98;

        if (!IsOverworld())
        {
            RecorderUsed = 1;
            return;
        }

        if (IsPlaying() && State.Play.RoomType == RoomType.Regular)
        {
            ReadOnlySpan<byte> RoomIds = [ 0x42, 0x06, 0x29, 0x2B, 0x30, 0x3A, 0x3C, 0x58, 0x60, 0x6E, 0x72 ];

            var makeWhirlwind = true;

            for (var i = 0; i < RoomIds.Length; i++)
            {
                if (RoomIds[i] == CurRoomId)
                {
                    if ((i == 0 && Profile.Quest == 0)
                        || (i != 0 && Profile.Quest != 0))
                    {
                        makeWhirlwind = false;
                    }
                    break;
                }
            }

            if (makeWhirlwind)
            {
                SummonWhirlwind();
            }
            else
            {
                MakeFluteSecret();
            }
        }
    }

    private void SummonWhirlwind()
    {
        if (!SummonedWhirlwind
            && WhirlwindTeleporting == 0
            && IsOverworld()
            && IsPlaying()
            && State.Play.RoomType == RoomType.Regular
            && GetItem(ItemSlot.TriforcePieces) != 0)
        {
            var slot = FindEmptyMonsterSlot();
            if (slot != ObjectSlot.NoneFound)
            {
                static ReadOnlySpan<byte> TeleportRoomIds() => new byte[] { 0x36, 0x3B, 0x73, 0x44, 0x0A, 0x21, 0x41, 0x6C };

                var whirlwind = new WhirlwindActor(Game, 0, Game.Link.Y);
                SetObject(slot, whirlwind);

                SummonedWhirlwind = true;
                TeleportingRoomIndex = GetNextTeleportingRoomIndex();
                whirlwind.SetTeleportPrevRoomId(TeleportRoomIds()[TeleportingRoomIndex]);
            }
        }
    }

    private void MakeFluteSecret()
    {
        // TODO:
        // The original game makes a FluteSecret object (type $5E) and puts it in one of the first 9
        // object slots that it finds going from higher to lower slots. The FluteSecret object manages
        // the animation. See $EFA4, and the FluteSecret's update routine at $FEF4.
        // But, for now we'll keep doing it as we have been.

        if (!State.Play.UncoveredRecorderSecret && FindSparseFlag(Sparse.Recorder, CurRoomId))
        {
            State.Play.UncoveredRecorderSecret = true;
            State.Play.AnimatingRoomColors = true;
            State.Play.Timer = 88;
        }
    }

    private TileBehavior GetTileBehavior(int row, int col)
    {
        return TileMaps[CurTileMapIndex].AsBehaviors(row, col);
    }

    private TileBehavior GetTileBehaviorXY(int x, int y)
    {
        var col = x / TileWidth;
        var row = (y - TileMapBaseY) / TileHeight;

        return GetTileBehavior(row, col);
    }

    public void SetMobXY(int x, int y, BlockObjType mobIndex)
    {
        var fineCol = x / TileWidth;
        var fineRow = (y - TileMapBaseY) / TileHeight;

        if (fineCol is < 0 or >= Columns || fineRow is < 0 or >= Rows) return;

        SetMob(fineRow, fineCol, mobIndex);
    }

    private void SetMob(int row, int col, BlockObjType mobIndex)
    {
        LoadMobFunc(ref TileMaps[CurTileMapIndex], row, col, (byte)mobIndex); // JOE: FIXME: BlockObjTypes

        for (var r = row; r < row + 2; r++)
        {
            for (var c = col; c < col + 2; c++)
            {
                var t = TileMaps[CurTileMapIndex].Refs(r, c);
                TileMaps[CurTileMapIndex].Behaviors(r, c) = TileBehaviors[t];
            }
        }

        // TODO: Will we need to run some function to initialize the map object, like in LoadLayout?
    }

    public Palette GetInnerPalette()
    {
        return RoomAttrs[CurRoomId].GetInnerPalette();
    }

    public Cell GetRandomWaterTile()
    {
        var waterList = new Cell[Rows * Columns];
        var waterCount = 0;

        for (var r = 0; r < Rows - 1; r++)
        {
            for (var c = 0; c < Columns - 1; c++)
            {
                if (GetTileBehavior(r, c) == TileBehavior.Water
                    && GetTileBehavior(r, c + 1) == TileBehavior.Water
                    && GetTileBehavior(r + 1, c) == TileBehavior.Water
                    && GetTileBehavior(r + 1, c + 1) == TileBehavior.Water)
                {
                    waterList[waterCount] = new Cell((byte)r, (byte)c);
                    waterCount++;
                }
            }
        }

        if (waterCount <= 0) throw new Exception();

        var waterRandom = Random.Shared.Next(0, waterCount);
        var cell = waterList[waterRandom];
        return new Cell((byte)(cell.Row + BaseRows), cell.Col);
    }

    public bool HasObject(ObjectSlot slot) => GetObject(slot) != null;
    public T? GetObject<T>(ObjectSlot slot) where T : Actor => GetObject(slot) as T;

    public Actor? GetObject(ObjectSlot slot)
    {
        return slot == ObjectSlot.Player ? Game.Link : Objects[(int)slot];
    }

    public IEnumerable<T> GetMonsters<T>(bool skipStart = false) where T : Actor
    {
        var start = skipStart ? ObjectSlot.Monster1 + 1 : ObjectSlot.Monster1;
        var end = skipStart ? ObjectSlot.Monster1 + 9 : ObjectSlot.MonsterEnd;
        for (var slot = start; slot < end; slot++)
        {
            var obj = GetObject(slot);
            if (obj is T monster)
            {
                yield return monster;
            }
        }
    }

    public T? GetFirstObject<T>(ObjectSlot start, ObjectSlot end) where T : Actor
    {
        for (var slot = start; slot < end; slot++)
        {
            var obj = GetObject<T>(slot);
            if (obj != null) return obj;
        }

        return null;
    }

    public void SetObject(ObjectSlot slot, Actor? obj)
    {
        SetOnlyObject(slot, obj);
    }

    public ObjectSlot FindEmptyFireSlot()
    {
        for (var i = ObjectSlot.FirstFire; i < ObjectSlot.LastFire; i++)
        {
            if (Objects[(int)i] == null) return i;
        }
        return ObjectSlot.NoneFound;
    }

    public ref int GetObjectTimer(ObjectSlot slot) => ref ObjectTimers[(int)slot];
    public void SetObjectTimer(ObjectSlot slot, int value) => ObjectTimers[(int)slot] = value;
    public int GetStunTimer(ObjectSlot slot) => StunTimers[(int)slot];
    public void SetStunTimer(ObjectSlot slot, int value) => StunTimers[(int)slot] = value;
    public void PushTile(int row, int col) => InteractTile(row, col, TileInteraction.Push);
    private void TouchTile(int row, int col) => InteractTile(row, col, TileInteraction.Touch);
    public void CoverTile(int row, int col) => InteractTile(row, col, TileInteraction.Cover);

    private void InteractTile(int row, int col, TileInteraction interaction)
    {
        if (row < 0 || col < 0 || row >= Rows || col >= Columns) return;

        var behavior = GetTileBehavior(row, col);
        var behaviorFunc = sBehaviorFuncs[(int)behavior];
        behaviorFunc(row, col, interaction);
    }

    public static bool CollidesWall(TileBehavior behavior) => behavior is TileBehavior.Wall or TileBehavior.Doorway or TileBehavior.Door;
    private static bool CollidesTile(TileBehavior behavior) => behavior >= TileBehavior.FirstSolid;

    public TileCollision CollidesWithTileStill(int x, int y)
    {
        return CollidesWithTile(x, y, Direction.None, 0);
    }

    public TileCollision CollidesWithTileMoving(int x, int y, Direction dir, bool isPlayer)
    {
        var offset = dir switch
        {
            Direction.Right => 0x10,
            Direction.Down => 8,
            _ => isPlayer ? -8 : -0x10,
        };

        var collision = CollidesWithTile(x, y, dir, offset);

        // Special tile collision in top right corner of OW. Go thru the wall.
        if (isPlayer
            && InfoBlock.LevelNumber == 0
            && CurRoomId == 0x1F
            && dir.IsVertical()
            && x == 0x80
            && y < 0x56)
        {
            collision.Collides = false;
        }

        if (Game.Cheats.NoClip && isPlayer)
        {
            collision.Collides = false;
        }

        return collision;
    }

    private TileCollision CollidesWithTile(int x, int y, Direction dir, int offset)
    {
        y += 0xB;

        if (dir.IsVertical())
        {
            if (dir == Direction.Up || y < 0xDD)
            {
                y += offset;
            }
        }
        else
        {
            if ((dir == Direction.Left && x >= 0x10) || (dir == Direction.Right && x < 0xF0))
            {
                x += offset;
            }
        }

        if (y < TileMapBaseY)
        {
            // JOE: FIXME: Arg. This is a bug in the original C++ but the oringal C++ is a proper translation
            // from the assembly. I was unable to reproduce this issue in the original game, so it's either a
            // logic change or an issue higher up.
            y = TileMapBaseY;

            // throw new Exception("I think this bad.");
            // Debugger.Break();
        }

        var behavior = TileBehavior.FirstWalkable;
        var fineRow = (byte)((y - TileMapBaseY) / 8);
        var fineCol1 = (byte)(x / 8);
        var hitFineCol = fineCol1;

        var fineCol2 = dir.IsVertical() ? (byte)((x + 8) / 8) : fineCol1;

        for (var c = fineCol1; c <= fineCol2; c++)
        {
            var curBehavior = GetTileBehavior(fineRow, c);
            if (curBehavior == TileBehavior.Water && State.Play.AllowWalkOnWater)
            {
                curBehavior = TileBehavior.GenericWalkable;
            }

            if (curBehavior > behavior)
            {
                behavior = curBehavior;
                hitFineCol = c;
            }
        }

        return new TileCollision(CollidesTile(behavior), behavior, hitFineCol, fineRow);
    }

    public TileCollision PlayerCoversTile(int x, int y)
    {
        y += 3;

        var behavior = TileBehavior.FirstWalkable;
        var fineRow1 = (byte)((y - TileMapBaseY) / 8);
        var fineRow2 = (byte)((y + 15 - TileMapBaseY) / 8);
        var fineCol1 = (byte)(x / 8);
        var fineCol2 = (byte)((x + 15) / 8);
        var hitFineCol = fineCol1;
        var hitFineRow = fineRow1;

        for (var r = fineRow1; r <= fineRow2; r++)
        {
            for (var c = fineCol1; c <= fineCol2; c++)
            {
                var curBehavior = GetTileBehavior(r, c);
                if (curBehavior == TileBehavior.Water && State.Play.AllowWalkOnWater)
                {
                    curBehavior = TileBehavior.GenericWalkable;
                }

                // TODO: this isn't the best way to check covered tiles
                //       but it'll do for now.
                if (curBehavior > behavior)
                {
                    behavior = curBehavior;
                    hitFineCol = c;
                    hitFineRow = r;
                }
            }
        }

        return new TileCollision(false, behavior, hitFineCol, hitFineRow);
    }

    public void OnPushedBlock()
    {
        Game.Sound.PlayEffect(SoundEffect.Secret);

        if (IsOverworld())
        {
            if (!GotShortcut(CurRoomId))
            {
                if (FindSparseFlag(Sparse.Shortcut, CurRoomId))
                {
                    TakeShortcut();
                    ShowShortcutStairs(CurRoomId, CurTileMapIndex);
                }
            }
        }
        else
        {
            var uwRoomAttrs = CurrentUWRoomAttrs;
            var secret = uwRoomAttrs.GetSecret();

            if (secret == Secret.BlockDoor)
            {
                TriggerShutters = true;
            }
            else if (secret == Secret.BlockStairs)
            {
                AddUWRoomStairs();
            }
        }
    }

    public void OnActivatedArmos(int x, int y)
    {
        var pos = FindSparsePos2(Sparse.ArmosStairs, CurRoomId);

        if (pos != null && x == pos.Value.x && y == pos.Value.y)
        {
            SetMobXY(x, y, BlockObjType.MobStairs);
            Game.Sound.PlayEffect(SoundEffect.Secret);
        }
        else
        {
            SetMobXY(x, y, BlockObjType.MobGround);
        }

        if (!GotItem())
        {
            var roomItem = FindSparseItem(Sparse.ArmosItem, CurRoomId);

            if (roomItem != null && x == roomItem.Value.x && y == roomItem.Value.y)
            {
                var itemObj = Actor.FromType((ObjType)roomItem.Value.itemId, Game, roomItem.Value.x, roomItem.Value.y);
                Objects[(int)ObjectSlot.Item] = itemObj;
            }
        }
    }

    private static ReadOnlySpan<byte> OnTouchedPowerTriforcePalette => new byte[] { 0, 0x0F, 0x10, 0x30 };

    public void OnTouchedPowerTriforce()
    {
        PowerTriforceFanfare = true;
        Game.Link.SetState(PlayerState.Paused);
        Game.Link.ObjTimer = 0xC0;

        Graphics.SetPaletteIndexed(Palette.LevelFgPalette, OnTouchedPowerTriforcePalette);
        Graphics.UpdatePalettes();
    }

    private void CheckPowerTriforceFanfare()
    {
        if (!PowerTriforceFanfare) return;

        if (Game.Link.ObjTimer == 0)
        {
            PowerTriforceFanfare = false;
            Game.Link.SetState(PlayerState.Idle);
            AddItem(ItemId.PowerTriforce);
            GlobalFunctions.SetPilePalette();
            Graphics.UpdatePalettes();
            Game.Sound.PlaySong(SongId.Level9, SongStream.MainSong, true);
        }
        else
        {
            var timer = Game.Link.ObjTimer;
            if ((timer & 4) > 0)
            {
                SetFlashPalette();
            }
            else
            {
                SetLevelPalette();
            }
        }
    }

    private void AdjustInventory()
    {
        if (Profile.SelectedItem == 0)
        {
            Profile.SelectedItem = ItemSlot.Boomerang;
        }

        for (var i = 0; i < 10; i++)
        {
            if (Profile.SelectedItem is ItemSlot.Arrow or ItemSlot.Bow)
            {
                if (Profile.Items[ItemSlot.Arrow] != 0
                    && Profile.Items[ItemSlot.Bow] != 0)
                {
                    break;
                }
            }
            else
            {
                if (Profile.Items[Profile.SelectedItem] != 0)
                {
                    break;
                }
            }

            switch ((int)Profile.SelectedItem)
            {
                case 0x07: Profile.SelectedItem = (ItemSlot)0x0F; break;
                case 0x0F: Profile.SelectedItem = (ItemSlot)0x06; break;
                case 0x01: Profile.SelectedItem = (ItemSlot)0x1B; break;
                case 0x1B: Profile.SelectedItem = (ItemSlot)0x08; break;
                default: Profile.SelectedItem--; break;
            }
        }
    }

    private void WarnLowHPIfNeeded()
    {
        if (Profile.Hearts >= 0x100) return;

        Game.Sound.PlayEffect(SoundEffect.LowHp);
    }

    private void PlayAmbientSounds()
    {
        var playedSound = false;

        if (IsOverworld())
        {
            if (GetMode() == GameMode.Play)
            {
                var owRoomAttrs = CurrentOWRoomAttrs;
                if (owRoomAttrs.HasAmbientSound())
                {
                    Game.Sound.PlayEffect(SoundEffect.Sea, true, Sound.AmbientInstance);
                    playedSound = true;
                }
            }
        }
        else
        {
            if (CurUWBlockFlags[InfoBlock.BossRoomId].GetObjCount() == 0)
            {
                var uwRoomAttrs = CurrentUWRoomAttrs;
                var ambientSound = uwRoomAttrs.GetAmbientSound();
                if (ambientSound != 0)
                {
                    var id = (int)SoundEffect.BossRoar1 + ambientSound - 1;
                    Game.Sound.PlayEffect((SoundEffect)id, true, Sound.AmbientInstance);
                    playedSound = true;
                }
            }
        }

        if (!playedSound)
        {
            Game.Sound.StopEffects();
        }
    }

    public void ShowShortcutStairs(int roomId, int tileMapIndex) // JOE: TODO: Is _tileMapIndex not being used a mistake?
    {
        var owRoomAttrs = GetOWRoomAttrs(roomId);
        var index = owRoomAttrs.GetShortcutStairsIndex();
        var pos = InfoBlock.ShortcutPosition[index];
        GetRoomCoord(pos, out var row, out var col);
        SetMob(row * 2, col * 2, BlockObjType.MobStairs);
    }

    private void DrawMap(int roomId, int mapIndex, int offsetX, int offsetY)
    {
        Graphics.Begin();

        var outerPalette = RoomAttrs[roomId].GetOuterPalette();
        var innerPalette = RoomAttrs[roomId].GetInnerPalette();
        var map = TileMaps[mapIndex];

        if (IsUWCellar(roomId) || IsPlayingCave())
        {
            outerPalette = (Palette)3;
            innerPalette = (Palette)2;
        }

        var firstRow = 0;
        var lastRow = Rows;
        var tileOffsetY = offsetY;

        var firstCol = 0;
        var lastCol = Columns;
        var tileOffsetX = offsetX;

        if (offsetY < 0)
        {
            firstRow = -offsetY / TileHeight;
            tileOffsetY = -(-offsetY % TileHeight);
        }
        else if (offsetY > 0)
        {
            lastRow = Rows - offsetY / TileHeight;
        }
        else if (offsetX < 0)
        {
            firstCol = -offsetX / TileWidth;
            tileOffsetX = -(-offsetX % TileWidth);
        }
        else if (offsetX > 0)
        {
            lastCol = Columns - offsetX / TileWidth;
        }

        var endCol = StartCol + ColCount;
        var endRow = StartRow + RowCount;

        var y = TileMapBaseY + tileOffsetY;

        if (IsUWMain(roomId))
        {
            Graphics.DrawBitmap(
                WallsBmp,
                0, 0,
                TileMapWidth, TileMapHeight,
                offsetX, TileMapBaseY + offsetY,
                outerPalette, 0);
        }

        for (var r = firstRow; r < lastRow; r++, y += TileHeight)
        {
            if (r < StartRow || r >= endRow) continue;

            var x = tileOffsetX;

            for (var c = firstCol; c < lastCol; c++, x += TileWidth)
            {
                if (c < StartCol || c >= endCol)
                    continue;

                var tileRef = map.Refs(r, c);
                var srcX = (tileRef & 0x0F) * TileWidth;
                var srcY = ((tileRef & 0xF0) >> 4) * TileHeight;

                var palette = (r is < 4 or >= 18 || c is < 4 or >= 28) ? outerPalette : innerPalette;

                Graphics.DrawTile(TileSheet.Background, srcX, srcY, TileWidth, TileHeight, x, y, palette, 0);
            }
        }

        if (IsUWMain(roomId))
        {
            DrawDoors(roomId, false, offsetX, offsetY);
        }

        Graphics.End();
    }

    private void DrawDoors(int roomId, bool above, int offsetX, int offsetY)
    {
        var outerPalette = RoomAttrs[roomId].GetOuterPalette();
        var baseY = above ? DoorOverlayBaseY : DoorUnderlayBaseY;
        var uwRoomAttr = GetUWRoomAttrs(roomId);

        for (var i = 0; i < 4; i++)
        {
            var doorDir = i.GetOrdDirection();
            var doorType = uwRoomAttr.GetDoor(i);
            var doorState = GetDoorState(roomId, doorDir);
            if (TempShutterDoorDir != 0 && roomId == TempShutterRoomId && doorType == DoorType.Shutter)
            {
                if (doorDir == TempShutterDoorDir)
                {
                    doorState = true;
                }
            }
            if (doorType == DoorType.Shutter && TempShutters && TempShutterRoomId == roomId)
            {
                doorState = true;
            }
            var doorFace = GetDoorStateFace(doorType, doorState);
            Graphics.DrawBitmap(
                DoorsBmp,
                DoorWidth * doorFace,
                doorSrcYs[i] + baseY,
                DoorWidth,
                DoorHeight,
                doorPos[i].X + offsetX,
                doorPos[i].Y + offsetY,
                outerPalette,
                0);
        }
    }

    public bool HasItem(ItemSlot itemSlot) => GetItem(itemSlot) > 0;
    public int GetItem(ItemSlot itemSlot) => Profile.Items[itemSlot];

    public void SetItem(ItemSlot itemSlot, int value)
    {
        Profile.Items[itemSlot] = value;
    }

    private void PostRupeeChange(byte value, ItemSlot itemSlot)
    {
        var curValue = Profile.Items[itemSlot];
        var newValue = Math.Clamp(curValue + value, 0, 255);

        Profile.Items[itemSlot] = newValue;
    }

    public void PostRupeeWin(byte value) => PostRupeeChange(value, ItemSlot.RupeesToAdd);
    public void PostRupeeLoss(byte value) => PostRupeeChange(value, ItemSlot.RupeesToSubtract);

    public void FillHearts(int heartValue)
    {
        var maxHeartValue = Profile.Items[ItemSlot.HeartContainers] << 8;

        Profile.Hearts += heartValue;
        if (Profile.Hearts >= maxHeartValue)
        {
            Profile.Hearts = maxHeartValue - 1;
        }
    }

    public void AddItem(ItemId itemId)
    {
        if ((int)itemId >= (int)ItemId.None) return;

        GlobalFunctions.PlayItemSound(Game, itemId);

        var equip = sItemToEquipment[(int)itemId];
        var slot = equip.Slot;
        var value = equip.Value;

        if (itemId is ItemId.Heart or ItemId.Fairy)
        {
            var heartValue = value << 8;
            FillHearts(heartValue);
            return;
        }
        else if (slot == ItemSlot.Bombs)
        {
            value += (byte)Profile.Items[ItemSlot.Bombs];
            if (value > Profile.Items[ItemSlot.MaxBombs])
            {
                value = (byte)Profile.Items[ItemSlot.MaxBombs];
            }
        }
        else if (slot is ItemSlot.RupeesToAdd or ItemSlot.Keys or ItemSlot.HeartContainers)
        {
            value += (byte)Profile.Items[slot];
            if (value > 255)
            {
                value = 255;
            }
        }
        else if (itemId == ItemId.Compass)
        {
            if (InfoBlock.LevelNumber < 9)
            {
                var bit = 1 << (InfoBlock.LevelNumber - 1);
                value = (byte)(Profile.Items[ItemSlot.Compass] | bit);
                slot = ItemSlot.Compass;
            }
        }
        else if (itemId == ItemId.Map)
        {
            if (InfoBlock.LevelNumber < 9)
            {
                var bit = 1 << (InfoBlock.LevelNumber - 1);
                value = (byte)(Profile.Items[ItemSlot.Map] | bit);
                slot = ItemSlot.Map;
            }
        }
        else if (itemId == ItemId.TriforcePiece)
        {
            var bit = 1 << (InfoBlock.LevelNumber - 1);
            value = (byte)(Profile.Items[ItemSlot.TriforcePieces] | bit);
        }

        Profile.Items[slot] = value;

        if (slot == ItemSlot.Ring)
        {
            SetPlayerColor();
            Graphics.UpdatePalettes();
        }
        if (slot == ItemSlot.HeartContainers)
        {
            FillHearts(0x100);
        }
    }

    public void DecrementItem(ItemSlot itemSlot)
    {
        var val = GetItem(itemSlot);
        if (val != 0)
        {
            Profile.Items[itemSlot] = val - 1;
        }
    }

    public bool HasCurrentMap() => HasCurrentLevelItem(ItemSlot.Map, ItemSlot.Map9);
    public bool HasCurrentCompass() => HasCurrentLevelItem(ItemSlot.Compass, ItemSlot.Compass9);

    private bool HasCurrentLevelItem(ItemSlot itemSlot1To8, ItemSlot itemSlot9)
    {
        if (InfoBlock.LevelNumber == 0) return false;

        if (InfoBlock.LevelNumber < 9)
        {
            var itemValue = Profile.Items[itemSlot1To8];
            var bit = 1 << (InfoBlock.LevelNumber - 1);
            return (itemValue & bit) != 0;
        }

        return Profile.Items[itemSlot9] != 0;
    }

    private DoorType GetDoorType(Direction dir)
    {
        return GetDoorType(CurRoomId, dir);
    }

    public DoorType GetDoorType(int roomId, Direction dir)
    {
        var dirOrd = dir.GetOrdinal();
        var uwRoomAttrs = GetUWRoomAttrs(roomId);
        return uwRoomAttrs.GetDoor(dirOrd);
    }

    private bool GetEffectiveDoorState(int roomId, Direction doorDir)
    {
        // TODO: the original game does it a little different, by looking at $EE.
        return GetDoorState(roomId, doorDir)
            || (GetDoorType(doorDir) == DoorType.Shutter
                && TempShutters && roomId == TempShutterRoomId)
            || (TempShutterDoorDir == doorDir && roomId == TempShutterRoomId);
    }

    private bool GetEffectiveDoorState(Direction doorDir) => GetEffectiveDoorState(CurRoomId, doorDir);
    public UWRoomFlags GetUWRoomFlags(int roomId) => CurUWBlockFlags[roomId];
    public LevelInfoBlock GetLevelInfo() => InfoBlock;
    public bool IsOverworld() => InfoBlock.LevelNumber == 0;
    public bool DoesRoomSupportLadder() => FindSparseFlag(Sparse.Ladder, CurRoomId);
    private TileAction GetTileAction(int tileRef) => TileAttr.GetAction(TileAttrs[tileRef]);
    public bool IsUWMain(int roomId) => !IsOverworld() && (RoomAttrs[roomId].GetUniqueRoomId() < 0x3E);
    public bool IsUWMain() => IsUWMain(CurRoomId);
    private bool IsUWCellar(int roomId) => !IsOverworld() && (RoomAttrs[roomId].GetUniqueRoomId() >= 0x3E);
    public bool IsUWCellar() => IsUWCellar(CurRoomId);
    private bool GotShortcut(int roomId) => Profile.OverworldFlags[roomId].GetShortcutState();
    private bool GotSecret() => Profile.OverworldFlags[CurRoomId].GetSecretState();

    public ReadOnlySpan<byte> GetShortcutRooms()
    {
        var valueArray = SparseRoomAttrs.GetItems<byte>(Sparse.Shortcut);
        // elemSize is at 1, but we don't need it.
        return valueArray[2..valueArray[0]];
    }

    private void TakeShortcut() => Profile.OverworldFlags[CurRoomId].SetShortcutState();
    public void TakeSecret() => Profile.OverworldFlags[CurRoomId].SetSecretState();
    public bool GotItem() => GotItem(CurRoomId);

    public bool GotItem(int roomId)
    {
        return IsOverworld() ? Profile.OverworldFlags[roomId].GetItemState() : CurUWBlockFlags[roomId].GetItemState();
    }

    public void MarkItem()
    {
        if (IsOverworld())
        {
            Profile.OverworldFlags[CurRoomId].SetItemState();
        }
        else
        {
            CurUWBlockFlags[CurRoomId].SetItemState();
        }
    }

    public void LiftItem(ItemId itemId, short timer = 0x80)
    {
        if (!IsPlaying()) return;

        if (itemId is ItemId.None or 0)
        {
            State.Play.LiftItemTimer = 0;
            State.Play.LiftItemId = 0;
            return;
        }

        State.Play.LiftItemTimer = Game.Cheats.SpeedUp ? (byte)1 : timer;
        State.Play.LiftItemId = itemId;

        Game.Link.SetState(PlayerState.Paused);
    }

    public bool IsLiftingItem()
    {
        if (!IsPlaying()) return false;

        return State.Play.LiftItemId != 0;
    }

    public void OpenShutters()
    {
        TempShutters = true;
        TempShutterRoomId = CurRoomId;
        Game.Sound.PlayEffect(SoundEffect.Door);

        for (var i = 0; i < Doors; i++)
        {
            var dir = i.GetOrdDirection();
            var type = GetDoorType(dir);

            if (type == DoorType.Shutter)
            {
                UpdateDoorTileBehavior(i);
            }
        }
    }

    public void IncrementKilledObjectCount(bool allowBombDrop)
    {
        WorldKillCount++;

        if (HelpDropCounter < 0xA)
        {
            HelpDropCounter++;
            if (HelpDropCounter == 0xA)
            {
                if (allowBombDrop) HelpDropValue++;
            }
        }
    }

    // $7B67
    public void ResetKilledObjectCount()
    {
        WorldKillCount = 0;
        HelpDropCounter = 0;
        HelpDropValue = 0;
    }

    public void IncrementRoomKillCount()
    {
        RoomKillCount++;
    }

    public void SetBombItemDrop()
    {
        HelpDropCounter = 0xA;
        HelpDropValue = 0xA;
    }

    public void SetObservedPlayerPos(int x, int y)
    {
        FakePlayerPos.X = x;
        FakePlayerPos.Y = y;
    }

    public void SetPersonWallY(int y)
    {
        State.Play.PersonWallY = y;
    }

    public int GetFadeStep()
    {
        return DarkRoomFadeStep;
    }

    public void BeginFadeIn()
    {
        if (DarkRoomFadeStep > 0)
        {
            BrightenRoom = true;
        }
    }

    public void FadeIn()
    {
        if (DarkRoomFadeStep == 0)
        {
            BrightenRoom = false;
            return;
        }

        var timer = GetObjectTimer(ObjectSlot.FadeTimer);

        if (timer == 0)
        {
            DarkRoomFadeStep--;
            timer = 10; // JOE: TODO: Does this reference still work?

            for (var i = 0; i < 2; i++)
            {
                Graphics.SetPaletteIndexed((Palette)(i + 2), InfoBlock.DarkPalette(DarkRoomFadeStep, i));
            }
            Graphics.UpdatePalettes();
        }
    }

    // JOE: TODO: Research why this is unused.
    private bool UseKey()
    {
        if (HasItem(ItemSlot.MagicKey)) return true;
        if (GetItem(ItemSlot.Keys) == 0) return false;

        DecrementItem(ItemSlot.Keys);
        return true;
    }

    private bool GetDoorState(int roomId, Direction door)
    {
        return CurUWBlockFlags[roomId].GetDoorState(door);
    }

    private void SetDoorState(int roomId, Direction door)
    {
        CurUWBlockFlags[roomId].SetDoorState(door);
    }

    private bool IsRoomInHistory()
    {
        for (var i = 0; i < RoomHistoryLength; i++)
        {
            if (RoomHistory[i] == CurRoomId) return true;
        }
        return false;
    }

    private void AddRoomToHistory()
    {
        var i = 0;

        for (; i < RoomHistoryLength; i++)
        {
            if (RoomHistory[i] == CurRoomId) break;
        }

        if (i == RoomHistoryLength)
        {
            RoomHistory[NextRoomHistorySlot] = (byte)CurRoomId;
            NextRoomHistorySlot++;
            if (NextRoomHistorySlot >= RoomHistoryLength)
            {
                NextRoomHistorySlot = 0;
            }
        }
    }

    private bool FindSparseFlag(Sparse attrId, int roomId)
    {
        return SparseRoomAttrs.FindSparseAttr<SparsePos>(attrId, roomId).HasValue;
    }

    private SparsePos? FindSparsePos(Sparse attrId, int roomId)
    {
        return SparseRoomAttrs.FindSparseAttr<SparsePos>(attrId, roomId);
    }

    private SparsePos2? FindSparsePos2(Sparse attrId, int roomId)
    {
        return SparseRoomAttrs.FindSparseAttr<SparsePos2>(attrId, roomId);
    }

    private SparseRoomItem? FindSparseItem(Sparse attrId, int roomId)
    {
        return SparseRoomAttrs.FindSparseAttr<SparseRoomItem>(attrId, roomId);
    }

    private ReadOnlySpan<ObjectAttr> GetObjectAttrs()
    {
        return ExtraData.GetItems<ObjectAttr>(Extra.ObjAttrs);
    }

    public ObjectAttr GetObjectAttrs(ObjType type)
    {
        return GetObjectAttrs()[(int)type];
    }

    public int GetObjectMaxHP(ObjType type)
    {
        var hpAttrs = ExtraData.GetItems<HPAttr>(Extra.HitPoints);
        var index = (int)type / 2;
        return hpAttrs[index].GetHP((int)type);
    }

    public int GetPlayerDamage(ObjType type)
    {
        var damageAttrs = ExtraData.GetItems<byte>(Extra.PlayerDamage);
        var damageByte = damageAttrs[(int)type];
        return ((damageByte & 0xF) << 8) | (damageByte & 0xF0);
    }

    public void LoadOverworldRoom(int x, int y) => LoadRoom(x + y * 16, CurTileMapIndex);

    private void LoadRoom(int roomId, int tileMapIndex)
    {
        if (IsUWCellar(roomId))
        {
            LoadCellarContext();
            PrevRoomWasCellar = true;
        }
        else if (PrevRoomWasCellar)
        {
            LoadUnderworldContext();
            PrevRoomWasCellar = false;
        }

        CurRoomId = roomId;
        CurTileMapIndex = tileMapIndex;

        LoadMap(roomId, tileMapIndex);

        if (IsOverworld())
        {
            if (GotShortcut(roomId))
            {
                if (FindSparseFlag(Sparse.Shortcut, roomId))
                {
                    ShowShortcutStairs(roomId, tileMapIndex);
                }
            }

            if (!GotItem())
            {
                var roomItem = FindSparseItem(Sparse.Item, roomId);
                if (roomItem != null)
                {
                    var itemObj = new ItemObjActor(Game, roomItem.Value.AsItemId, true, roomItem.Value.x, roomItem.Value.y);
                    Objects[(int)ObjectSlot.Item] = itemObj;
                }
            }
        }
        else
        {
            if (!GotItem())
            {
                var uwRoomAttrs = GetUWRoomAttrs(roomId);
                if (uwRoomAttrs.GetSecret() is not (Secret.FoesItem or Secret.LastBoss))
                {
                    AddUWRoomItem(roomId);
                }
            }
        }
    }

    public void AddUWRoomItem()
    {
        AddUWRoomItem(CurRoomId);
    }

    private void AddUWRoomItem(int roomId)
    {
        var uwRoomAttrs = GetUWRoomAttrs(roomId);
        var itemId = uwRoomAttrs.GetItemId();

        if (itemId != ItemId.None)
        {
            var posIndex = uwRoomAttrs.GetItemPositionIndex();
            var pos = GetRoomItemPosition(InfoBlock.ShortcutPosition[posIndex]);

            if (itemId == ItemId.TriforcePiece)
            {
                pos.X = TriforcePieceX;
            }

            // Arg
            var itemObj = new ItemObjActor(Game, itemId, true, pos.X, pos.Y);
            Objects[(int)ObjectSlot.Item] = itemObj;

            if (uwRoomAttrs.GetSecret() is Secret.FoesItem or Secret.LastBoss)
            {
                Game.Sound.PlayEffect(SoundEffect.RoomItem);
            }
        }
    }

    private void LoadCaveRoom(Cave uniqueRoomId)
    {
        CurTileMapIndex = 0;

        LoadLayout((int)uniqueRoomId, 0, TileScheme.Overworld);
    }

    private void LoadMap(int roomId, int tileMapIndex)
    {
        TileScheme tileScheme;
        var uniqueRoomId = RoomAttrs[roomId].GetUniqueRoomId();

        if (IsOverworld())
        {
            tileScheme = TileScheme.Overworld;
        }
        else if (uniqueRoomId >= 0x3E)
        {
            tileScheme = TileScheme.UnderworldCellar;
            uniqueRoomId -= 0x3E;
        }
        else
        {
            tileScheme = TileScheme.UnderworldMain;
        }

        LoadLayout(uniqueRoomId, tileMapIndex, tileScheme);

        if (tileScheme == TileScheme.UnderworldMain)
        {
            for (var i = 0; i < Doors; i++)
            {
                UpdateDoorTileBehavior(roomId, tileMapIndex, i);
            }
        }
    }

    private void LoadOWMob(ref TileMap map, int row, int col, int mobIndex)
    {
        var primary = PrimaryMobs[mobIndex];

        if (primary == 0xFF)
        {
            var index = mobIndex * 4;
            var secondaries = SecondaryMobs;
            map.Refs(row, col) = secondaries[index + 0];
            map.Refs(row, col + 1) = secondaries[index + 2];
            map.Refs(row + 1, col) = secondaries[index + 1];
            map.Refs(row + 1, col + 1) = secondaries[index + 3];
        }
        else
        {
            map.Refs(row, col) = primary;
            map.Refs(row, col + 1) = (byte)(primary + 2);
            map.Refs(row + 1, col) = (byte)(primary + 1);
            map.Refs(row + 1, col + 1) = (byte)(primary + 3);
        }
    }

    private void LoadUWMob(ref TileMap map, int row, int col, int mobIndex)
    {
        var primary = PrimaryMobs[mobIndex];

        if (primary is < 0x70 or > 0xF2)
        {
            map.Refs(row, col) = primary;
            map.Refs(row, col + 1) = primary;
            map.Refs(row + 1, col) = primary;
            map.Refs(row + 1, col + 1) = primary;
        }
        else
        {
            map.Refs(row, col) = primary;
            map.Refs(row, col + 1) = (byte)(primary + 2);
            map.Refs(row + 1, col) = (byte)(primary + 1);
            map.Refs(row + 1, col + 1) = (byte)(primary + 3);
        }
    }

    private void LoadLayout(int uniqueRoomId, int tileMapIndex, TileScheme tileScheme)
    {
        var maxColumnStartOffset = (ColCount / 2 - 1) * RowCount / 2;

        var columns = RoomCols[uniqueRoomId];
        var map = TileMaps[tileMapIndex];
        var rowEnd = StartRow + RowCount;

        var owLayoutFormat = tileScheme is TileScheme.Overworld or TileScheme.UnderworldCellar;

        LoadMobFunc = tileScheme switch
        {
            TileScheme.Overworld => LoadOWMob,
            TileScheme.UnderworldMain => LoadUWMob,
            TileScheme.UnderworldCellar => LoadOWMob,
            _ => LoadMobFunc
        };

        for (var i = 0; i < ColCount / 2; i++)
        {
            var columnDesc = columns.ColumnDesc[i];
            var tableIndex = (byte)((columnDesc & 0xF0) >> 4);
            var columnIndex = (byte)(columnDesc & 0x0F);

            var table = ColTables.GetItem(tableIndex);
            var k = 0;
            var j = 0;

            for (j = 0; j <= maxColumnStartOffset; j++)
            {
                var t = table[j];

                if ((t & 0x80) != 0)
                {
                    if (k == columnIndex) break;
                    k++;
                }
            }

            if (j > maxColumnStartOffset) throw new Exception();

            var c = StartCol + i * 2;

            for (var r = StartRow; r < rowEnd; j++)
            {
                var t = table[j];
                var tileRef = owLayoutFormat ? (byte)(t & 0x3F) : (byte)(t & 0x7);

                LoadMobFunc(ref map, r, c, tileRef);

                var attr = TileAttrs[tileRef];
                var action = TileAttr.GetAction(attr);
                TileActionDel? actionFunc = null;

                if (action != 0)
                {
                    actionFunc = sActionFuncs[(int)action];
                    actionFunc(r, c, TileInteraction.Load);
                }

                r += 2;

                if (owLayoutFormat)
                {
                    if ((t & 0x40) != 0 && r < rowEnd)
                    {
                        LoadMobFunc(ref map, r, c, tileRef);
                        actionFunc?.Invoke(r, c, TileInteraction.Load);
                        r += 2;
                    }
                }
                else
                {
                    var repeat = (t >> 4) & 0x7;
                    for (var m = 0; m < repeat && r < rowEnd; m++)
                    {
                        LoadMobFunc(ref map, r, c, tileRef);
                        actionFunc?.Invoke(r, c, TileInteraction.Load);
                        r += 2;
                    }
                }
            }
        }

        if (IsUWMain(CurRoomId))
        {
            var uwRoomAttrs = CurrentUWRoomAttrs;
            if (uwRoomAttrs.HasBlock())
            {
                for (var c = StartCol; c < StartCol + ColCount; c += 2)
                {
                    var tileRef = TileMaps[CurTileMapIndex].Refs(UWBlockRow, c);
                    if (tileRef == (byte)BlockObjType.TileBlock)
                    {
                        sActionFuncs[(int)TileAction.Block](UWBlockRow, c, TileInteraction.Load);
                        break;
                    }
                }
            }
        }

        for (var i = 0; i < Rows * Columns; i++)
        {
            var t = map.Refs(i);
            map.Behaviors(i) = TileBehaviors[t];
        }

        PatchTileBehaviors();
    }

    private void PatchTileBehaviors()
    {
        PatchTileBehavior(GhostCount, GhostCells, TileBehavior.Ghost0);
        PatchTileBehavior(ArmosCount, ArmosCells, TileBehavior.Armos0);
    }

    private void PatchTileBehavior(int count, Cell[] cells, TileBehavior baseBehavior)
    {
        for (var i = 0; i < count; i++)
        {
            var row = cells[i].Row;
            var col = cells[i].Col;
            var behavior = (byte)((int)baseBehavior + 15 - i);
            TileMaps[CurTileMapIndex].Behaviors(row, col) = behavior;
            TileMaps[CurTileMapIndex].Behaviors(row, col + 1) = behavior;
            TileMaps[CurTileMapIndex].Behaviors(row + 1, col) = behavior;
            TileMaps[CurTileMapIndex].Behaviors(row + 1, col + 1) = behavior;
        }
    }

    private void UpdateDoorTileBehavior(int doorOrd)
    {
        UpdateDoorTileBehavior(CurRoomId, CurTileMapIndex, doorOrd);
    }

    private void UpdateDoorTileBehavior(int roomId, int tileMapIndex, int doorOrd)
    {
        var map = TileMaps[tileMapIndex];
        var dir = doorOrd.GetOrdDirection();
        var corner = doorCorners[doorOrd];
        var type = GetDoorType(roomId, dir);
        var state = GetEffectiveDoorState(roomId, dir);
        var behavior = (byte)(state ? doorBehaviors[(int)type].Open : doorBehaviors[(int)type].Closed);

        map.Behaviors(corner.Row, corner.Col) = behavior;
        map.Behaviors(corner.Row, corner.Col + 1) = behavior;
        map.Behaviors(corner.Row + 1, corner.Col) = behavior;
        map.Behaviors(corner.Row + 1, corner.Col + 1) = behavior;

        if ((TileBehavior)behavior == TileBehavior.Doorway)
        {
            corner = behindDoorCorners[doorOrd];
            map.Behaviors(corner.Row, corner.Col) = behavior;
            map.Behaviors(corner.Row, corner.Col + 1) = behavior;
            map.Behaviors(corner.Row + 1, corner.Col) = behavior;
            map.Behaviors(corner.Row + 1, corner.Col + 1) = behavior;
        }
    }

    private void GotoPlay(RoomType roomType = RoomType.Regular)
    {
        switch (roomType)
        {
            case RoomType.Regular: CurMode = GameMode.Play; break;
            case RoomType.Cave: CurMode = GameMode.PlayCave; break;
            case RoomType.Cellar: CurMode = GameMode.PlayCellar; break;
            default:
                throw new Exception();
                CurMode = GameMode.Play;
                break;
        }
        CurColorSeqNum = 0;
        TempShutters = false;
        RoomObjCount = 0;
        RoomObj = null;
        RoomKillCount = 0;
        RoomAllDead = false;
        MadeRoomItem = false;
        EnablePersonFireballs = false;
        GhostCount = 0;
        ArmosCount = 0;

        State.Play.Substate = PlayState.Substates.Active;
        State.Play.AnimatingRoomColors = false;
        State.Play.AllowWalkOnWater = false;
        State.Play.UncoveredRecorderSecret = false;
        State.Play.RoomType = roomType;
        State.Play.LiftItemTimer = 0;
        State.Play.LiftItemId = 0;
        State.Play.PersonWallY = 0;

        if (FindSparseFlag(Sparse.Dock, CurRoomId))
        {
            var slot = FindEmptyMonsterSlot();
            var dock = new DockActor(Game, 0, 0);
            SetObject(slot, dock);
        }

        // Set the level's level foreground palette before making objects,
        // so that if we make a boss, we won't override a palette that it might set.
        SetLevelFgPalette();
        Graphics.UpdatePalettes();
        PlayAmbientSounds();

        ClearRoomItemData();
        GlobalFunctions.ClearRoomMonsterData();
        InitObjectTimers();
        InitStunTimers();
        InitPlaceholderTypes();
        MakeObjects(Game.Link.Facing);
        MakeWhirlwind();
        AddRoomToHistory();
        MoveRoomItem();

        if (!IsOverworld())
        {
            CurUWBlockFlags[CurRoomId].SetVisitState();
        }
    }

    private void UpdatePlay()
    {
        if (State.Play.Substate != PlayState.Substates.Active) return;

        if (BrightenRoom)
        {
            FadeIn();
            DecrementObjectTimers();
            DecrementStunTimers();
            return;
        }

        if (Game.Enhancements && Game.Input.IsButtonDown(Button.Select) && Game.Input.IsButtonDown(Button.Start))
        {
            GotoContinueQuestion();
            return;
        }

        if (Submenu != SubmenuState.IdleClosed)
        {
            UpdateSubmenu();
            return;
        }

        if (Pause == 0)
        {
            if (Game.Input.IsButtonPressing(Button.Select))
            {
                if (Game.Enhancements)
                {
                    Menu.SelectNextItem();
                }
                else
                {
                    Pause = PauseState.Paused;
                    Game.Sound.Pause();
                }
                return;
            }

            if (Game.Input.IsButtonPressing(Button.Start))
            {
                Submenu = SubmenuState.StartOpening;
                return;
            }
        }
        else if (Pause == PauseState.Paused)
        {
            if (Game.Input.IsButtonPressing(Button.Select))
            {
                Pause = 0;
                Game.Sound.Unpause();
            }
            return;
        }

        DecrementObjectTimers();
        DecrementStunTimers();

        if (ObjectTimers[(int)ObjectSlot.FluteMusic] != 0) return;

        if (Pause == PauseState.FillingHearts)
        {
            FillHeartsStep();
            return;
        }

        if (State.Play.AnimatingRoomColors)
        {
            UpdateRoomColors();
        }

        if (IsUWMain(CurRoomId))
        {
            CheckBombables();
        }

        UpdateRupees();
        UpdateLiftItem();

        CurObjSlot = (int)ObjectSlot.Player;
        Game.Link.DecInvincibleTimer();
        Game.Link.Update();

        // The player's update might have changed the world's State.
        if (!IsPlaying()) return;

        UpdateObservedPlayerPos();

        for (CurObjSlot = (int)ObjectSlot.MaxObjects - 1; CurObjSlot >= 0; CurObjSlot--)
        {
            var obj = Objects[CurObjSlot];
            if (obj != null && !obj.IsDeleted)
            {
                if (obj.DecoratedUpdate())
                {
                    HandleNormalObjectDeath();
                }
            }
            else if (PlaceholderTypes[CurObjSlot] != 0)
            {
                PutEdgeObject();
            }
        }

        DeleteDeadObjects();

        CheckSecrets();
        CheckShutters();
        UpdateDoors2();
        UpdateStatues();
        MoveRoomItem();
        CheckPowerTriforceFanfare();
        AdjustInventory();
        WarnLowHPIfNeeded();
    }

    private void UpdateSubmenu()
    {
        if (Submenu == SubmenuState.StartOpening)
        {
            Menu.Enable();
            Submenu++;
            StatusBar.EnableFeatures(StatusBarFeatures.Equipment, false);

            if (Game.Cheats.SpeedUp)
            {
                Submenu = SubmenuState.EndOpening;
                SubmenuOffsetY = SubmenuType.Height;
            }
        }
        else if (Submenu == SubmenuState.EndOpening)
        {
            SubmenuOffsetY += SubmenuType.YScrollSpeed;
            if (SubmenuOffsetY >= SubmenuType.Height)
            {
                SubmenuOffsetY = SubmenuType.Height;
                Menu.Activate();
                Submenu++;
            }
        }
        else if (Submenu == SubmenuState.IdleOpen)
        {
            if (Game.Input.IsButtonPressing(Button.Start))
            {
                Menu.Deactivate();
                Submenu++;

                if (Game.Cheats.SpeedUp)
                {
                    Submenu = SubmenuState.StartClose;
                    SubmenuOffsetY = 0;
                }
            }
        }
        else if (Submenu == SubmenuState.StartClose)
        {
            SubmenuOffsetY -= SubmenuType.YScrollSpeed;
            if (SubmenuOffsetY <= 0)
            {
                Menu.Disable();
                Submenu = SubmenuState.IdleClosed;
                StatusBar.EnableFeatures(StatusBarFeatures.Equipment, true);
                SubmenuOffsetY = 0;
            }
        }
        else
        {
            Submenu++;
        }

        if (Submenu != 0)
        {
            Menu.Update();
        }
    }

    private void CheckShutters()
    {
        if (!TriggerShutters) return;

        TriggerShutters = false;
        Direction dirs = 0;

        for (var i = 0; i < 4; i++)
        {
            var dir = i.GetOrdDirection();

            if (GetDoorType(dir) == DoorType.Shutter
                && !GetEffectiveDoorState(dir))
            {
                dirs |= dir;
            }
        }

        if (dirs != 0 && TriggeredDoorCmd == 0)
        {
            TriggeredDoorCmd = 6;
            TriggeredDoorDir |= (Direction)0x10;
        }
    }

    private void UpdateDoors2()
    {
        if (GetMode() == GameMode.EndLevel
            || ObjectTimers[(int)ObjectSlot.Door] != 0
            || TriggeredDoorCmd == 0)
        {
            return;
        }

        if ((TriggeredDoorCmd & 1) == 0)
        {
            TriggeredDoorCmd++;
            ObjectTimers[(int)ObjectSlot.Door] = 8;
            return;
        }

        if ((TriggeredDoorDir & (Direction)0x10) != 0)
        {
            OpenShutters();
        }

        var d = 1;

        for (var i = 0; i < 4; i++, d <<= 1)
        {
            if ((TriggeredDoorDir & (Direction)d) == 0) continue;

            var dir = (Direction)d;
            var type = GetDoorType(dir);

            if (type is DoorType.Bombable or DoorType.Key or DoorType.Key2)
            {
                if (!GetDoorState(CurRoomId, dir))
                {
                    var oppositeDir = dir.GetOppositeDirection();
                    var nextRoomId = GetNextRoomId(CurRoomId, dir);

                    SetDoorState(CurRoomId, dir);
                    SetDoorState(nextRoomId, oppositeDir);
                    if (type != DoorType.Bombable)
                    {
                        Game.Sound.PlayEffect(SoundEffect.Door);
                    }
                    UpdateDoorTileBehavior(i);
                }
            }
        }

        TriggeredDoorCmd = 0;
        TriggeredDoorDir = Direction.None;
    }

    private byte GetNextTeleportingRoomIndex()
    {
        var facing = Game.Link.Facing;
        var growing = facing is Direction.Up or Direction.Right;

        var pieces = GetItem(ItemSlot.TriforcePieces);
        var index = TeleportingRoomIndex;
        var mask = 1 << TeleportingRoomIndex;

        if (pieces == 0) return 0;

        do
        {
            if (growing)
            {
                index = (byte)((index + 1) & 7);
                mask <<= 1;
                if (mask >= 0x100)
                {
                    mask = 1;
                }
            }
            else
            {
                index = (byte)((index - 1) & 7);
                mask >>= 1;
                if (mask == 0)
                {
                    mask = 0x80;
                }
            }
        } while ((pieces & mask) == 0);

        return index;
    }

    private void UpdateRoomColors()
    {
        if (State.Play.Timer == 0)
        {
            State.Play.AnimatingRoomColors = false;
            var posAttr = FindSparsePos(Sparse.Recorder, CurRoomId);
            if (posAttr != null)
            {
                GetRoomCoord(posAttr.Value.pos, out var row, out var col);
                SetMob(row * 2, col * 2, BlockObjType.MobStairs);
                Game.Sound.PlayEffect(SoundEffect.Secret);
            }
            return;
        }

        if ((State.Play.Timer % 8) == 0)
        {
            var colorSeq = ExtraData.ReadLengthPrefixedItem((int)Extra.PondColors);
            if (CurColorSeqNum < colorSeq.Length - 1)
            {
                if (CurColorSeqNum == colorSeq.Length - 2)
                {
                    State.Play.AllowWalkOnWater = true;
                }

                int colorIndex = colorSeq[CurColorSeqNum];
                CurColorSeqNum++;
                Graphics.SetColorIndexed((Palette)3, 3, colorIndex);
                Graphics.UpdatePalettes();
            }
        }

        State.Play.Timer--;
    }

    private void CheckBombables()
    {
        var uwRoomAttrs = CurrentUWRoomAttrs;

        for (var iBomb = ObjectSlot.FirstBomb; iBomb < ObjectSlot.LastBomb; iBomb++)
        {
            var bomb = GetObject<BombActor>(iBomb);
            if (bomb == null || bomb.BombState != BombState.Fading) continue;

            var bombX = bomb.X + 8;
            var bombY = bomb.Y + 8;

            for (var iDoor = 0; iDoor < 4; iDoor++)
            {
                var doorType = uwRoomAttrs.GetDoor(iDoor);
                if (doorType == DoorType.Bombable)
                {
                    var doorDir = iDoor.GetOrdDirection();
                    var doorState = GetDoorState(CurRoomId, doorDir);
                    if (!doorState)
                    {
                        if (Math.Abs(bombX - doorMiddles[iDoor].X) < UWBombRadius
                            && Math.Abs(bombY - doorMiddles[iDoor].Y) < UWBombRadius)
                        {
                            TriggeredDoorCmd = 6;
                            TriggeredDoorDir = doorDir;
                            break;
                        }
                    }
                }
            }
        }
    }

    public bool HasLivingObjects()
    {
        return !RoomAllDead;
    }

    private bool CalcHasLivingObjects()
    {
        foreach (var monster in GetMonsters<Actor>())
        {
            // JOE: TODO: Offload this to the monster's classes.
            var type = monster.ObjType;
            if (type < ObjType.Bubble1
                || (type > ObjType.Bubble3 && type < ObjType.Trap))
            {
                return true;
            }
        }

        return false;
    }

    private void CheckSecrets()
    {
        if (IsOverworld()) return;

        if (!RoomAllDead)
        {
            if (!CalcHasLivingObjects())
            {
                Game.Link.IsParalyzed = false;
                RoomAllDead = true;
            }
        }

        var uwRoomAttrs = CurrentUWRoomAttrs;
        var secret = uwRoomAttrs.GetSecret();

        switch (secret)
        {
            case Secret.Ringleader:
                if (GetObject(ObjectSlot.Monster1) == null || GetObject(ObjectSlot.Monster1) is PersonActor)
                {
                    KillAllObjects();
                }
                break;

            case Secret.LastBoss:
                if (GetItem(ItemSlot.PowerTriforce) != 0)
                    TriggerShutters = true;
                break;

            case Secret.FoesItem:
                // ORIGINAL: BlockDoor and BlockStairs are handled here.
                if (RoomAllDead)
                {
                    if (!MadeRoomItem && !GotItem())
                    {
                        MadeRoomItem = true;
                        AddUWRoomItem(CurRoomId);
                    }
                }
                // fall thru
                goto case Secret.FoesDoor;
            case Secret.FoesDoor:
                if (RoomAllDead)
                    TriggerShutters = true;
                break;
        }
    }

    private void AddUWRoomStairs()
    {
        SetMobXY(0xD0, 0x60, BlockObjType.MobUWStairs);
    }

    public void KillAllObjects()
    {
        foreach (var monster in GetMonsters<Actor>())
        {
            if (monster.ObjType < ObjType.PersonEnd && monster.Decoration == 0)
            {
                monster.Decoration = 0x10;
            }

        }
        // for (var i = (int)ObjectSlot.Monster1; i < (int)ObjectSlot.MonsterEnd; i++)
        // {
        //     var obj = Objects[i];
        //     if (obj != null
        //         && obj.ObjType < ObjType.PersonEnd
        //         && obj.Decoration == 0)
        //     {
        //         obj.Decoration = 0x10;
        //     }
        // }
    }

    private void MoveRoomItem()
    {
        var foe = GetObject(ObjectSlot.Monster1);
        if (foe == null || !foe.CanHoldRoomItem) return;

        var item = GetObject(ObjectSlot.Item);
        if (item == null) return;

        item.X = foe.X;
        item.Y = foe.Y;
    }

    private static ReadOnlySpan<int> FireballLayouts => new[] { 0x24, 0x23 };

    private void UpdateStatues()
    {
        if (IsOverworld()) return;

        var pattern = -1;

        if (EnablePersonFireballs)
        {
            pattern = 2;
        }
        else
        {
            var uwRoomAttrs = CurrentUWRoomAttrs;
            var layoutId = uwRoomAttrs.GetUniqueRoomId();

            for (var i = 0; i < FireballLayouts.Length; i++)
            {
                if (FireballLayouts[i] == layoutId)
                {
                    pattern = i;
                    break;
                }
            }
        }

        if (pattern >= 0)
        {
            Statues.Update(Game, (Statues.PatternType)pattern);
        }
    }

    private void OnLeavePlay()
    {
        if (LastMode == GameMode.Play)
        {
            SaveObjectCount();
        }
    }

    private void ClearLevelData()
    {
        CurColorSeqNum = 0;
        DarkRoomFadeStep = 0;
        CurMazeStep = 0;
        TempShutterRoomId = 0;
        TempShutterDoorDir = 0;
        TempShutterRoomId = 0;
        TempShutters = false;
        PrevRoomWasCellar = false;
        WhirlwindTeleporting = 0;

        RoomKillCount = 0;
        RoomAllDead = false;
        MadeRoomItem = false;
        EnablePersonFireballs = false;
    }

    private static bool IsRecurringFoe(ObjType type)
    {
        return type is < ObjType.OneDodongo or ObjType.RedLamnola or ObjType.BlueLamnola or >= ObjType.Trap;
    }

    private void SaveObjectCount()
    {
        if (IsOverworld())
        {
            var flags = Profile.OverworldFlags[CurRoomId];
            var savedCount = flags.GetObjCount();
            int count;

            if (RoomKillCount >= RoomObjCount)
            {
                count = 7;
            }
            else
            {
                count = (RoomKillCount & 7) + savedCount;
                if (count > 7)
                {
                    count = 7;
                }
            }

            flags.SetObjCount(count);
        }
        else
        {
            var flags = CurUWBlockFlags[CurRoomId];

            if (RoomObjCount != 0)
            {
                if (RoomKillCount == 0 || (RoomObj != null && RoomObj.IsReoccuring))
                {
                    if (RoomKillCount < RoomObjCount)
                    {
                        LevelKillCounts[CurRoomId] += RoomKillCount;
                        var count = LevelKillCounts[CurRoomId] < 3 ? LevelKillCounts[CurRoomId] : 2;
                        flags.SetObjCount((byte)count);
                        return;
                    }
                }
            }

            LevelKillCounts[CurRoomId] = 0xF;
            flags.SetObjCount(3);
        }
    }

    private void CalcObjCountToMake(ref ObjType type, ref int count)
    {
        if (IsOverworld())
        {
            var flags = Profile.OverworldFlags[CurRoomId];

            if (!IsRoomInHistory() && (flags.GetObjCount() == 7))
            {
                flags.SetObjCount(0);
            }
            else
            {
                if (flags.GetObjCount() == 7)
                {
                    type = ObjType.None;
                    count = 0;
                }
                else if (flags.GetObjCount() != 0)
                {
                    var savedCount = flags.GetObjCount();
                    if (count < savedCount)
                    {
                        type = ObjType.None;
                        count = 0;
                    }
                    else
                    {
                        count -= savedCount;
                    }
                }
            }
        }
        else // Is Underworld
        {
            // JOE: TODO: Otherone reads from Profile.OverworldFlags. Feels weird.
            var flags = CurUWBlockFlags[CurRoomId];

            if (IsRoomInHistory() || flags.GetObjCount() != 3)
            {
                if (count < LevelKillCounts[CurRoomId])
                {
                    type = ObjType.None;
                    count = 0;
                }
                else
                {
                    count -= LevelKillCounts[CurRoomId];
                }
            }
            else
            {
                if (IsRecurringFoe(type))
                {
                    flags.SetObjCount(0);
                    LevelKillCounts[CurRoomId] = 0;
                }
                else
                {
                    type = ObjType.None;
                    count = 0;
                }
            }
        }
    }

    private void UpdateObservedPlayerPos()
    {
        // ORIGINAL: This happens after player updates and before player items update.

        if (!GiveFakePlayerPos)
        {
            FakePlayerPos.X = Game.Link.X;
            FakePlayerPos.Y = Game.Link.Y;
        }

        // ORIGINAL: This happens after player items update and before the rest of objects update.

        if (StunTimers[(int)ObjectSlot.ObservedPlayerTimer] == 0)
        {
            StunTimers[(int)ObjectSlot.ObservedPlayerTimer] = Random.Shared.Next(0, 8);

            GiveFakePlayerPos = !GiveFakePlayerPos;
            if (GiveFakePlayerPos)
            {
                if (FakePlayerPos.X == Game.Link.X)
                {
                    FakePlayerPos.X ^= 0xFF;
                    FakePlayerPos.Y ^= 0xFF;
                }
            }
        }
    }

    private void UpdateRupees()
    {
        if ((Game.GetFrameCounter() & 1) != 0) return;

        var rupeesToAdd = Profile.Items[ItemSlot.RupeesToAdd];
        var rupeesToSubtract = Profile.Items[ItemSlot.RupeesToSubtract];

        if (rupeesToAdd > 0 && rupeesToSubtract == 0)
        {
            if (Profile.Items[ItemSlot.Rupees] < 255)
            {
                Profile.Items[ItemSlot.Rupees]++;
            }
            else
            {
                Profile.Items[ItemSlot.RupeesToAdd] = 0;
            }

            Game.Sound.PlayEffect(SoundEffect.Character);
        }
        else if (rupeesToAdd == 0 && rupeesToSubtract > 0)
        {
            if (Profile.Items[ItemSlot.Rupees] > 0)
            {
                Profile.Items[ItemSlot.Rupees]--;
            }
            else
            {
                Profile.Items[ItemSlot.RupeesToSubtract] = 0;
            }

            Game.Sound.PlayEffect(SoundEffect.Character);
        }

        if (Profile.Items[ItemSlot.RupeesToAdd] > 0) Profile.Items[ItemSlot.RupeesToAdd]--;
        if (Profile.Items[ItemSlot.RupeesToSubtract] > 0) Profile.Items[ItemSlot.RupeesToSubtract]--;
    }

    private void UpdateLiftItem()
    {
        if (State.Play.LiftItemId == 0) return;

        ArgumentOutOfRangeException.ThrowIfNegative(State.Play.LiftItemTimer);

        State.Play.LiftItemTimer--;
        if (State.Play.LiftItemTimer == 0)
        {
            State.Play.LiftItemId = 0;
            Game.Link.SetState(PlayerState.Idle);
        }
        else
        {
            Game.Link.SetState(PlayerState.Paused);
        }
    }

    private void DrawPlay()
    {
        if (Submenu != 0)
        {
            DrawSubmenu();
            return;
        }

        using (var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight))
        {
            ClearScreen();
            DrawRoom();
        }

        DrawObjects(out var objOverPlayer);

        if (IsLiftingItem())
        {
            DrawLinkLiftingItem(State.Play.LiftItemId);
        }
        else
        {
            Game.Link.Draw();
        }

        objOverPlayer?.DecoratedDraw();

        if (IsUWMain(CurRoomId))
        {
            DrawDoors(CurRoomId, true, 0, 0);
        }
    }

    private void DrawSubmenu()
    {
        using (var _ = Graphics.SetClip(0, TileMapBaseY + SubmenuOffsetY, TileMapWidth, TileMapHeight - SubmenuOffsetY))
        {
            ClearScreen();
            DrawMap(CurRoomId, CurTileMapIndex, 0, SubmenuOffsetY);
        }

        if (IsUWMain(CurRoomId))
        {
            DrawDoors(CurRoomId, true, 0, SubmenuOffsetY);
        }

        Menu.Draw(SubmenuOffsetY);
    }

    private void DrawObjects(out Actor? objOverPlayer)
    {
        objOverPlayer = null;

        for (var i = ObjectSlot.FirstSlot; i < ObjectSlot.MaxObjects; i++)
        {
            CurObjectSlot = i;

            var obj = Objects[(int)i];
            if (obj != null && !obj.IsDeleted)
            {
                if (!obj.Flags.HasFlag(ActorFlags.DrawAbovePlayer) || objOverPlayer != null)
                {
                    obj.DecoratedDraw();
                }
                else
                {
                    objOverPlayer = obj;
                }
            }
        }
    }

    private void DrawZeldaLiftingTriforce(int x, int y)
    {
        var image = Graphics.GetSpriteImage(TileSheet.Boss, AnimationId.B3_Zelda_Lift);
        image.Draw(TileSheet.Boss, x, y, Palette.Player);

        GlobalFunctions.DrawItem(Game, ItemId.TriforcePiece, x, y - 0x10, 0);
    }

    private void DrawLinkLiftingItem(ItemId itemId)
    {
        var animIndex = itemId == ItemId.TriforcePiece ? AnimationId.LinkLiftHeavy : AnimationId.LinkLiftLight;
        var image = Graphics.GetSpriteImage(TileSheet.PlayerAndItems, animIndex);
        image.Draw(TileSheet.PlayerAndItems, Game.Link.X, Game.Link.Y, Palette.Player);

        GlobalFunctions.DrawItem(Game, itemId, Game.Link.X, Game.Link.Y - 0x10, 0);
    }

    private void MakeObjects(Direction entryDir)
    {
        if (IsUWCellar(CurRoomId))
        {
            MakeCellarObjects();
            return;
        }

        if (State.Play.RoomType == RoomType.Cave)
        {
            MakeCaveObjects();
            return;
        }

        var roomAttr = RoomAttrs[CurRoomId];
        var slot = ObjectSlot.Monster1;
        var objId = (ObjType)roomAttr.MonsterListId;
        var edgeObjects = false;

        if (objId is >= ObjType.Person1 and < ObjType.PersonEnd or ObjType.Grumble)
        {
            MakeUnderworldPerson(objId);
            return;
        }

        if (IsOverworld())
        {
            var owRoomAttrs = CurrentOWRoomAttrs;
            edgeObjects = owRoomAttrs.MonstersEnter();
        }

        var count = roomAttr.GetMonsterCount();

        if (objId is >= ObjType.OneDodongo and < ObjType.Rock)
        {
            count = 1;
        }

        CalcObjCountToMake(ref objId, ref count);
        RoomObjCount = count;

        if (objId > 0 && count > 0)
        {
            var isList = objId >= ObjType.Rock;
            var repeatedIds = new byte[(int)ObjectSlot.MaxMonsters];
            ReadOnlySpan<byte> list;

            if (isList)
            {
                var listId = objId - ObjType.Rock;
                list = ObjLists.GetItem(listId);
            }
            else
            {
                Array.Fill(repeatedIds, (byte)objId, 0, count);
                list = repeatedIds;
            }

            var dirOrd = entryDir.GetOrdinal();
            // var spotSeq = extraData.GetItem<SpotSeq>(Extra.SpawnSpots);
            var spots = ExtraData.ReadLengthPrefixedItem((int)Extra.SpawnSpots);
            var spotsLen = spots.Length / 4;
            var dirSpots = spots[(spotsLen * dirOrd)..]; // JOE: This is very sus.

            var x = 0;
            var y = 0;
            for (var i = 0; i < count; i++, slot++)
            {
                // An earlier objects that's made might make some objects in slots after it.
                // Maybe MakeMonster should take a reference to the current index.
                if (GetObject(slot) != null) continue;

                CurObjSlot = (int)slot;

                var type = (ObjType)list[(int)slot];

                if (edgeObjects
                    && type != ObjType.Zora // TODO: Move this to an attribute on the class?
                    && type != ObjType.Armos
                    && type != ObjType.StandingFire
                    && type != ObjType.Whirlwind
                    )
                {
                    PlaceholderTypes[(int)slot] = (byte)type;
                }
                else if (FindSpawnPos(type, dirSpots, spotsLen, ref x, ref y))
                {
                    var obj = Actor.FromType(type, Game, x, y);
                    Objects[(int)slot] = obj;
                }
            }
        }

        var monster = GetObject(ObjectSlot.Monster1);
        if (monster != null)
        {
            RoomObj = monster;
        }

        if (IsOverworld())
        {
            var owRoomAttr = CurrentOWRoomAttrs;
            if (owRoomAttr.HasZora())
            {
                CurObjSlot = (int)slot;

                var zora = Actor.FromType(ObjType.Zora, Game, 0, 0);
                SetObject(slot, zora);
            }
        }
    }

    private void MakeCellarObjects()
    {
        static ReadOnlySpan<int> StartXs() => new[] { 0x20, 0x60, 0x90, 0xD0 };
        const int startY = 0x9D;

        for (var i = 0; i < 4; i++)
        {
            CurObjSlot = i;

            var keese = Actor.FromType(ObjType.BlueKeese, Game, StartXs()[i], startY);
            SetObject((ObjectSlot)i, keese);
        }
    }

    private void MakeCaveObjects()
    {
        var owRoomAttrs = CurrentOWRoomAttrs;
        var caveIndex = owRoomAttrs.GetCaveId() - FirstCaveIndex;

        var caves = ExtraData.LoadVariableLengthData<CaveSpecListStruct, CaveSpecList>((int)Extra.Caves);

        if (caveIndex >= caves.Count)
        {
            throw new Exception("JOE: This logic was in the original but seems questionable?");
            return;
        }

        var cave = caves[caveIndex];
        var type = (ObjType)((int)ObjType.Cave1 + caveIndex);

        MakePersonRoomObjects(type, cave);
    }

    private void MakeUnderworldPerson(ObjType type)
    {
        // JOE: TODO: Make all of these private and make a MoneyOrLife/etc constructor on CaveSpec.
        var cave = new CaveSpec
        {
            ItemA = (byte)ItemId.None,
            ItemB = (byte)ItemId.None,
            ItemC = (byte)ItemId.None
        };

        var uwRoomAttrs = CurrentUWRoomAttrs;
        var secret = uwRoomAttrs.GetSecret();

        if (type == ObjType.Grumble)
        {
            cave.StringId = (byte)StringId.Grumble;
            cave.DwellerType = ObjType.FriendlyMoblin;
        }
        else if (secret == Secret.MoneyOrLife)
        {
            cave.StringId = (byte)StringId.MoneyOrLife;
            cave.DwellerType = ObjType.OldMan;
            cave.ItemA = (byte)ItemId.HeartContainer;
            cave.PriceA = 1;
            cave.ItemC = (byte)ItemId.Rupee;
            cave.PriceC = 50;
            cave.SetShowNegative();
            cave.SetShowItems();
            cave.SetSpecial();
            cave.SetPickUp();
        }
        else
        {
            var stringIdTables = ExtraData.GetItem<LevelPersonStrings>(Extra.LevelPersonStringIds);

            var levelIndex = InfoBlock.EffectiveLevelNumber - 1;
            int levelTableIndex = levelGroups[levelIndex];
            var stringSlot = type - ObjType.Person1;
            var stringId = (StringId)stringIdTables.GetStringIds(levelTableIndex)[stringSlot];

            cave.DwellerType = ObjType.OldMan;
            cave.StringId = (byte)stringId;

            if (stringId == StringId.MoreBombs)
            {
                cave.ItemB = (byte)ItemId.Rupee;
                cave.PriceB = 100;
                cave.SetShowNegative();
                cave.SetShowItems();
                cave.SetSpecial();
                cave.SetPickUp();
            }
        }

        MakePersonRoomObjects(type, cave);
    }

    // JOE: type is no longer a type I think? It's a cave ID.
    private void MakePersonRoomObjects(ObjType type, CaveSpec spec)
    {
        ReadOnlySpan<int> fireXs = [0x48, 0xA8];

        if (spec.DwellerType != ObjType.None)
        {
            CurObjSlot = 0;
            var person = GlobalFunctions.MakePerson(Game, type, spec, 0x78, 0x80);
            SetObject(0, person);
        }

        for (var i = 0; i < 2; i++)
        {
            CurObjSlot++;
            var fire = new StandingFireActor(Game, fireXs[i], 0x80);
            SetObject((ObjectSlot)CurObjSlot, fire);
        }
    }

    private void MakeWhirlwind()
    {
        ReadOnlySpan<int> TeleportYs = [0x8D, 0xAD, 0x8D, 0x8D, 0xAD, 0x8D, 0xAD, 0x5D];

        if (WhirlwindTeleporting != 0)
        {
            var y = TeleportYs[TeleportingRoomIndex];

            WhirlwindTeleporting = 2;

            var whirlwind = new WhirlwindActor(Game, 0, y);
            SetObject(ObjectSlot.Whirlwind, whirlwind);

            Game.Link.SetState(PlayerState.Paused);
            Game.Link.X = whirlwind.X;
            Game.Link.Y = 0xF8;
        }
    }

    private bool FindSpawnPos(ObjType type, ReadOnlySpan<byte> spots, int len, ref int x, ref int y)
    {
        var objAttrs = GetObjectAttrs();

        var playerX = Game.Link.X;
        var playerY = Game.Link.Y;
        var noWorldCollision = !objAttrs[(int)type].GetWorldCollision();
        var i = 0;
        for (; i < (int)ObjectSlot.MaxObjListSize; i++)
        {
            GetRSpotCoord(spots[_spotIndex], ref x, ref y);
            _spotIndex = (_spotIndex + 1) % len;

            if ((playerX != x || playerY != y)
                && (noWorldCollision || !CollidesWithTileStill(x, y)))
            {
                break;
            }
        }

        if (x == 0 && y == 0) throw new Exception();

        return i != 9;
    }

    private void PutEdgeObject()
    {
        if (StunTimers[(int)ObjectSlot.EdgeObjTimer] != 0) return;

        StunTimers[(int)ObjectSlot.EdgeObjTimer] = Random.Shared.Next(0, 4) + 2;

        var x = EdgeX;
        var y = EdgeY;

        for (; ; )
        {
            y += x switch
            {
                0 => 0x10,
                0xF0 => -0x10,
                _ => 0
            };

            x += y switch
            {
                0x40 => -0x10,
                0xE0 => 0x10,
                _ => 0
            };

            var row = (y / 8) - 8;
            var col = (x / 8);
            var behavior = GetTileBehavior(row, col);

            if (behavior != TileBehavior.Sand && !CollidesTile(behavior)) break;
            if (y == EdgeY && x == EdgeX) break;
        }

        EdgeX = x;
        EdgeY = y;

        if (Math.Abs(Game.Link.X - x) >= 0x22 || Math.Abs(Game.Link.Y - y) >= 0x22)
        {
            // What?
            var obj = Actor.FromType((ObjType)PlaceholderTypes[CurObjSlot], Game, x, y - 3);
            Objects[CurObjSlot] = obj;
            PlaceholderTypes[CurObjSlot] = 0;
            obj.Decoration = 0;
        }
    }

    private void HandleNormalObjectDeath()
    {
        var obj = Objects[CurObjSlot] ?? throw new Exception("Missing object");
        var x = obj.X;
        var y = obj.Y;

        Objects[CurObjSlot] = null;

        // JOE: TODO: Put whatever this is on the object itself.
        if (obj.ObjType is not (ObjType.ChildGel or ObjType.RedKeese or ObjType.DeadDummy))
        {
            var cycle = WorldKillCycle + 1;
            if (cycle == 10)
            {
                cycle = 0;
            }
            WorldKillCycle = (byte)cycle;

            if (obj is not ZoraActor)
            {
                RoomKillCount++;
            }
        }

        TryDroppingItem(obj, x, y);
    }

    private static ReadOnlySpan<int> ClassBases => new[] { 0, 10, 20, 30 };
    private static ReadOnlySpan<int> ClassRates => new[] { 0x50, 0x98, 0x68, 0x68 };
    private static ReadOnlySpan<int> DropItems => new[] {
        0x22, 0x18, 0x22, 0x18, 0x23, 0x18, 0x22, 0x22, 0x18, 0x18, 0x0F, 0x18, 0x22, 0x18, 0x0F, 0x22,
        0x21, 0x18, 0x18, 0x18, 0x22, 0x00, 0x18, 0x21, 0x18, 0x22, 0x00, 0x18, 0x00, 0x22, 0x22, 0x22,
        0x23, 0x18, 0x22, 0x23, 0x22, 0x22, 0x22, 0x18
    };

    private void TryDroppingItem(Actor origType, int x, int y)
    {
        if (CurObjSlot == (int)ObjectSlot.Monster1 && origType is StalfosActor or GibdoActor) return;

        var objClass = origType.Attributes.GetItemDropClass();
        if (objClass == 0) return;
        objClass--;

        ItemId itemId;

        if (WorldKillCount == 0x10)
        {
            itemId = ItemId.Fairy;
            HelpDropCounter = 0;
            HelpDropValue = 0;
        }
        else if (HelpDropCounter >= 0xA)
        {
            itemId = HelpDropValue == 0 ? ItemId.FiveRupees : ItemId.Bomb;
            HelpDropCounter = 0;
            HelpDropValue = 0;
        }
        else
        {
            var r = Random.Shared.GetByte();
            var rate = ClassRates[objClass];

            if (r >= rate) return;

            var classIndex = ClassBases[objClass] + WorldKillCycle;
            itemId = (ItemId)DropItems[classIndex];
        }

        var obj = GlobalFunctions.MakeItem(Game, itemId, x, y, false);
        Objects[CurObjSlot] = obj;
    }

    private void FillHeartsStep()
    {
        Game.Sound.PlayEffect(SoundEffect.Character);

        var maxHeartsValue = Profile.GetMaxHeartsValue();

        FillHearts(6);

        if (Profile.Hearts == maxHeartsValue)
        {
            Pause = 0;
            SwordBlocked = false;
        }
    }

    private void GotoScroll(Direction dir)
    {
        if (dir == Direction.None) throw new ArgumentOutOfRangeException(nameof(dir));

        State.Scroll.CurRoomId = CurRoomId;
        State.Scroll.ScrollDir = dir;
        State.Scroll.Substate = ScrollState.Substates.Start;
        CurMode = GameMode.Scroll;
    }

    private void GotoScroll(Direction dir, int currentRoomId)
    {
        GotoScroll(dir);
        State.Scroll.CurRoomId = currentRoomId;
    }

    private bool CalcMazeStayPut(Direction dir)
    {
        if (!IsOverworld()) return false;

        var stayPut = false;
        var mazeOptional = SparseRoomAttrs.FindSparseAttr<SparseMaze>(Sparse.Maze, CurRoomId);
        if (mazeOptional != null)
        {
            var maze = mazeOptional.Value;
            if (dir != maze.ExitDirection)
            {
                var paths = maze.Paths;
                if (dir == paths[CurMazeStep])
                {
                    CurMazeStep++;
                    if (CurMazeStep == paths.Length)
                    {
                        CurMazeStep = 0;
                        Game.Sound.PlayEffect(SoundEffect.Secret);
                    }
                    else
                    {
                        stayPut = true;
                    }
                }
                else
                {
                    CurMazeStep = 0;
                    stayPut = true;
                }
            }
            else
            {
                CurMazeStep = 0;
            }
        }
        return stayPut;
    }

    private void UpdateScroll()
    {
        sScrollFuncs[(int)State.Scroll.Substate]();
    }

    private void UpdateScroll_Start()
    {
        GetWorldCoord(State.Scroll.CurRoomId, out var roomRow, out var roomCol);

        Actor.MoveSimple(ref roomCol, ref roomRow, State.Scroll.ScrollDir, 1);

        var nextRoomId = CalcMazeStayPut(State.Scroll.ScrollDir) ? State.Scroll.CurRoomId : MakeRoomId(roomRow, roomCol);

        State.Scroll.NextRoomId = nextRoomId;
        State.Scroll.Substate = ScrollState.Substates.AnimatingColors;
    }

    private void UpdateScroll_AnimatingColors()
    {
        if (CurColorSeqNum == 0)
        {
            State.Scroll.Substate = ScrollState.Substates.LoadRoom;
            return;
        }

        if ((Game.GetFrameCounter() & 4) != 0)
        {
            CurColorSeqNum--;

            var colorSeq = ExtraData.ReadLengthPrefixedItem((int)Extra.PondColors);
            int color = colorSeq[CurColorSeqNum];
            Graphics.SetColorIndexed((Palette)3, 3, color);
            Graphics.UpdatePalettes();

            if (CurColorSeqNum == 0)
            {
                State.Scroll.Substate = ScrollState.Substates.LoadRoom;
            }
        }
    }

    private void UpdateScroll_FadeOut()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(State.Scroll.Timer);

        if (State.Scroll.Timer > 0)
        {
            State.Scroll.Timer--;
            return;
        }

        for (var i = 0; i < 2; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i + 2, InfoBlock.DarkPalette(DarkRoomFadeStep, i));
        }
        Graphics.UpdatePalettes();

        DarkRoomFadeStep++;

        if (DarkRoomFadeStep == 4)
        {
            State.Scroll.Substate = ScrollState.Substates.Scroll;
            State.Scroll.Timer = ScrollState.StateTime;
        }
        else
        {
            State.Scroll.Timer = 9;
        }
    }

    private void UpdateScroll_LoadRoom()
    {
        if (State.Scroll.ScrollDir == Direction.Down
            && !IsOverworld()
            && CurRoomId == InfoBlock.StartRoomId)
        {
            GotoLoadLevel(0);
            return;
        }

        State.Scroll.OffsetX = 0;
        State.Scroll.OffsetY = 0;
        State.Scroll.SpeedX = 0;
        State.Scroll.SpeedY = 0;
        State.Scroll.OldMapToNewMapDistX = 0;
        State.Scroll.OldMapToNewMapDistY = 0;

        switch (State.Scroll.ScrollDir)
        {
            case Direction.Left:
                State.Scroll.OffsetX = -TileMapWidth;
                State.Scroll.SpeedX = ScrollSpeed;
                State.Scroll.OldMapToNewMapDistX = TileMapWidth;
                break;

            case Direction.Right:
                State.Scroll.OffsetX = TileMapWidth;
                State.Scroll.SpeedX = -ScrollSpeed;
                State.Scroll.OldMapToNewMapDistX = -TileMapWidth;
                break;

            case Direction.Up:
                State.Scroll.OffsetY = -TileMapHeight;
                State.Scroll.SpeedY = ScrollSpeed;
                State.Scroll.OldMapToNewMapDistY = TileMapHeight;
                break;

            case Direction.Down:
                State.Scroll.OffsetY = TileMapHeight;
                State.Scroll.SpeedY = -ScrollSpeed;
                State.Scroll.OldMapToNewMapDistY = -TileMapHeight;
                break;
        }

        State.Scroll.OldRoomId = CurRoomId;

        var nextRoomId = State.Scroll.NextRoomId;
        var nextTileMapIndex = (CurTileMapIndex + 1) % 2;
        State.Scroll.OldTileMapIndex = CurTileMapIndex;

        TempShutterRoomId = nextRoomId;
        TempShutterDoorDir = State.Scroll.ScrollDir.GetOppositeDirection();

        LoadRoom(nextRoomId, nextTileMapIndex);

        var uwRoomAttrs = GetUWRoomAttrs(nextRoomId);
        if (uwRoomAttrs.IsDark() && DarkRoomFadeStep == 0 && !Profile.PreventDarkRooms())
        {
            State.Scroll.Substate = ScrollState.Substates.FadeOut;
            State.Scroll.Timer = Game.Cheats.SpeedUp ? 1 : 9;
        }
        else
        {
            State.Scroll.Substate = ScrollState.Substates.Scroll;
            State.Scroll.Timer = Game.Cheats.SpeedUp ? 1 : ScrollState.StateTime;
        }
    }

    private void UpdateScroll_Scroll()
    {
        if (State.Scroll.Timer > 0)
        {
            State.Scroll.Timer--;
            return;
        }

        if (State.Scroll.OffsetX == 0 && State.Scroll.OffsetY == 0)
        {
            GotoEnter(State.Scroll.ScrollDir);
            if (IsOverworld() && State.Scroll.NextRoomId == (int)UniqueRoomIds.TopRightOverworldSecret)
            {
                Game.Sound.PlayEffect(SoundEffect.Secret);
            }
            return;
        }

        if (Game.Cheats.SpeedUp)
        {
            // JOE: TODO
        }

        State.Scroll.OffsetX += State.Scroll.SpeedX;
        State.Scroll.OffsetY += State.Scroll.SpeedY;

        var playerLimits = Link.PlayerLimits;

        if (State.Scroll.SpeedX != 0)
        {
            var x = Game.Link.X + State.Scroll.SpeedX;
            if (x < playerLimits[1])
            {
                x = playerLimits[1];
            }
            else if (x > playerLimits[0])
            {
                x = playerLimits[0];
            }
            Game.Link.X = x;
        }
        else
        {
            var y = Game.Link.Y + State.Scroll.SpeedY;
            if (y < playerLimits[3])
            {
                y = playerLimits[3];
            }
            else if (y > playerLimits[2])
            {
                y = playerLimits[2];
            }
            Game.Link.Y = y;
        }

        Game.Link.Animator.Advance();
    }

    private void DrawScroll()
    {
        using (var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight))
        {
            ClearScreen();

            if (State.Scroll.Substate is ScrollState.Substates.Scroll or ScrollState.Substates.FadeOut)
            {
                var oldMapOffsetX = State.Scroll.OffsetX + State.Scroll.OldMapToNewMapDistX;
                var oldMapOffsetY = State.Scroll.OffsetY + State.Scroll.OldMapToNewMapDistY;

                DrawMap(CurRoomId, CurTileMapIndex, State.Scroll.OffsetX, State.Scroll.OffsetY);
                DrawMap(State.Scroll.OldRoomId, State.Scroll.OldTileMapIndex, oldMapOffsetX, oldMapOffsetY);
            }
            else
            {
                DrawMap(CurRoomId, CurTileMapIndex, 0, 0);
            }
        }

        if (IsOverworld())
        {
            Game.Link.Draw();
        }
    }

    private void GotoLeave(Direction dir)
    {
        if (dir == Direction.None) throw new ArgumentOutOfRangeException(nameof(dir));

        State.Leave.CurRoomId = CurRoomId;
        State.Leave.ScrollDir = dir;
        State.Leave.Timer = LeaveState.StateTime;
        CurMode = GameMode.Leave;
    }

    private void GotoLeave(Direction dir, int currentRoomId)
    {
        GotoLeave(dir);
        State.Leave.CurRoomId = currentRoomId;
    }

    private void UpdateLeave()
    {
        var playerLimits = Link.PlayerLimits;
        var dirOrd = Game.Link.Facing.GetOrdinal();
        var coord = Game.Link.Facing.IsVertical() ? Game.Link.Y : Game.Link.X;

        if (coord != playerLimits[dirOrd])
        {
            Game.Link.MoveLinear(State.Leave.ScrollDir, Link.WalkSpeed);
            Game.Link.Animator.Advance();
            return;
        }

        if (State.Leave.Timer == 0)
        {
            Game.Link.Animator.AdvanceFrame();
            GotoScroll(State.Leave.ScrollDir, State.Leave.CurRoomId);
            return;
        }
        State.Leave.Timer--;
    }

    private void DrawLeave()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        DrawRoomNoObjects();
    }

    private void GotoEnter(Direction dir)
    {
        State.Enter.Substate = EnterState.Substates.Start;
        State.Enter.ScrollDir = dir;
        State.Enter.Timer = 0;
        State.Enter.PlayerPriority = SpritePriority.AboveBg;
        State.Enter.PlayerSpeed = Link.WalkSpeed;
        State.Enter.GotoPlay = false;
        CurMode = GameMode.Enter;
    }

    private void MovePlayer(Direction dir, int speed, ref int fraction)
    {
        fraction += speed;
        var carry = fraction >> 8;
        fraction &= 0xFF;

        var x = Game.Link.X;
        var y = Game.Link.Y;
        Actor.MoveSimple(ref x, ref y, dir, carry);

        Game.Link.X = x;
        Game.Link.Y = y;
    }

    private void UpdateEnter()
    {
        sEnterFuncs[(int)State.Enter.Substate]();

        if (State.Enter.GotoPlay)
        {
            var origShutterDoorDir = TempShutterDoorDir;
            TempShutterDoorDir = Direction.None;
            if (IsUWMain(CurRoomId)
                && origShutterDoorDir != Direction.None
                && GetDoorType(CurRoomId, origShutterDoorDir) == DoorType.Shutter)
            {
                Game.Sound.PlayEffect(SoundEffect.Door);
                var doorOrd = origShutterDoorDir.GetOrdinal();
                UpdateDoorTileBehavior(doorOrd);
            }

            StatusBar.EnableFeatures(StatusBarFeatures.All, true);
            if (IsOverworld() && FromUnderground != 0)
            {
                Game.Sound.PlaySong(InfoBlock.SongId, SongStream.MainSong, true);
            }
            GotoPlay();
            return;
        }
        Game.Link.Animator.Advance();
    }

    private void UpdateEnter_Start()
    {
        TriggeredDoorCmd = 0;
        TriggeredDoorDir = Direction.None;

        if (IsOverworld())
        {
            var behavior = GetTileBehaviorXY(Game.Link.X, Game.Link.Y + 3);
            if (behavior == TileBehavior.Cave)
            {
                Game.Link.Y += MobTileHeight;
                Game.Link.Facing = Direction.Down;

                State.Enter.PlayerFraction = 0;
                State.Enter.PlayerSpeed = 0x40;
                State.Enter.PlayerPriority = SpritePriority.BelowBg;
                State.Enter.ScrollDir = Direction.Up;
                State.Enter.TargetX = Game.Link.X;
                State.Enter.TargetY = Game.Link.Y - 0x10;
                State.Enter.Substate = EnterState.Substates.WalkCave;

                Game.Sound.StopAll();
                Game.Sound.PlayEffect(SoundEffect.Stairs);
            }
            else
            {
                State.Enter.Substate = EnterState.Substates.Wait;
                State.Enter.Timer = EnterState.StateTime;
            }
        }
        else if (State.Enter.ScrollDir != Direction.None)
        {
            var uwRoomAttrs = CurrentUWRoomAttrs;
            var oppositeDir = State.Enter.ScrollDir.GetOppositeDirection();
            var door = oppositeDir.GetOrdinal();
            var doorType = uwRoomAttrs.GetDoor(door);
            int distance;

            if (doorType is DoorType.Shutter or DoorType.Bombable)
            {
                distance = MobTileWidth * 2;
            }
            else
            {
                distance = MobTileWidth;
            }

            State.Enter.TargetX = Game.Link.X;
            State.Enter.TargetY = Game.Link.Y;
            Actor.MoveSimple(
                ref State.Enter.TargetX,
                ref State.Enter.TargetY,
                State.Enter.ScrollDir,
                distance);

            if (!uwRoomAttrs.IsDark() && DarkRoomFadeStep > 0)
            {
                State.Enter.Substate = EnterState.Substates.FadeIn;
                State.Enter.Timer = 9;
            }
            else
            {
                State.Enter.Substate = EnterState.Substates.Walk;
            }

            Game.Link.Facing = State.Enter.ScrollDir;
        }
        else
        {
            State.Enter.Substate = EnterState.Substates.Wait;
            State.Enter.Timer = EnterState.StateTime;
        }

        DoorwayDir = IsUWMain(CurRoomId) ? State.Enter.ScrollDir : Direction.None;
    }

    private void UpdateEnter_Wait()
    {
        State.Enter.Timer--;
        if (State.Enter.Timer == 0)
        {
            State.Enter.GotoPlay = true;
        }
    }

    private void UpdateEnter_FadeIn()
    {
        if (DarkRoomFadeStep == 0)
        {
            State.Enter.Substate = EnterState.Substates.Walk;
            return;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(State.Enter.Timer);

        if (State.Enter.Timer > 0)
        {
            State.Enter.Timer--;
            return;
        }

        DarkRoomFadeStep--;
        State.Enter.Timer = 9;

        for (var i = 0; i < 2; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i + 2, InfoBlock.DarkPalette(DarkRoomFadeStep, i));
        }
        Graphics.UpdatePalettes();
    }

    private void UpdateEnter_Walk()
    {
        if (Game.Link.X == State.Enter.TargetX && Game.Link.Y == State.Enter.TargetY)
        {
            State.Enter.GotoPlay = true;
        }
        else
        {
            Game.Link.MoveLinear(State.Enter.ScrollDir, State.Enter.PlayerSpeed);
        }
    }

    private void UpdateEnter_WalkCave()
    {
        if (Game.Link.X == State.Enter.TargetX && Game.Link.Y == State.Enter.TargetY)
        {
            State.Enter.GotoPlay = true;
        }
        else
        {
            MovePlayer(State.Enter.ScrollDir, State.Enter.PlayerSpeed, ref State.Enter.PlayerFraction);
        }
    }

    private void DrawEnter()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);

        if (State.Enter.Substate != EnterState.Substates.Start)
        {
            DrawRoomNoObjects(State.Enter.PlayerPriority);
        }
    }

    public void GotoLoadLevel(int level, bool restartOW = false)
    {
        State.LoadLevel.Level = level;
        State.LoadLevel.Substate = LoadLevelState.Substates.Load;
        State.LoadLevel.Timer = 0;
        State.LoadLevel.RestartOW = restartOW;

        CurMode = GameMode.LoadLevel;
    }

    private void SetPlayerExitPosOW(int roomId)
    {
        var owRoomAttrs = GetOWRoomAttrs(roomId);
        var exitRPos = owRoomAttrs.GetExitPosition();

        var col = exitRPos & 0xF;
        var row = (exitRPos >> 4) + 4;

        Game.Link.X = col * MobTileWidth;
        Game.Link.Y = row * MobTileHeight + 0xD;
    }

    public ReadOnlySpan<byte> GetString(StringId stringId)
    {
        return TextTable.GetItem((int)stringId);
    }

    private void UpdateLoadLevel()
    {
        if (State.LoadLevel.Substate == LoadLevelState.Substates.Load)
        {
            State.LoadLevel.Timer = LoadLevelState.StateTime;
            State.LoadLevel.Substate = LoadLevelState.Substates.Wait;

            int origLevel = InfoBlock.LevelNumber;
            var origRoomId = CurRoomId;

            Game.Sound.StopAll();
            StatusBarVisible = false;
            LoadLevel(State.LoadLevel.Level);

            // Let the Unfurl game mode load the room and reset colors.

            if (State.LoadLevel.Level == 0)
            {
                CurRoomId = SavedOWRoomId;
                SavedOWRoomId = -1;
                FromUnderground = 2;
            }
            else
            {
                CurRoomId = InfoBlock.StartRoomId;
                if (origLevel == 0)
                {
                    SavedOWRoomId = origRoomId;
                }
            }
        }
        else if (State.LoadLevel.Substate == LoadLevelState.Substates.Wait)
        {
            if (State.LoadLevel.Timer == 0)
            {
                GotoUnfurl(State.LoadLevel.RestartOW);
                return;
            }

            State.LoadLevel.Timer--;
        }
    }

    private void DrawLoadLevel()
    {
        using var _ = Graphics.SetClip(0, 0, Global.StdViewWidth, Global.StdViewHeight);
        ClearScreen();
    }

    private void GotoUnfurl(bool restartOW = false)
    {
        State.Unfurl.Substate = UnfurlState.Substates.Start;
        State.Unfurl.Timer = UnfurlState.StateTime;
        State.Unfurl.StepTimer = 0;
        State.Unfurl.Left = 0x80;
        State.Unfurl.Right = 0x80;
        State.Unfurl.RestartOW = restartOW;

        ClearLevelData();

        CurMode = GameMode.Unfurl;
    }

    private void UpdateUnfurl()
    {
        if (State.Unfurl.Substate == UnfurlState.Substates.Start)
        {
            State.Unfurl.Substate = UnfurlState.Substates.Unfurl;
            StatusBarVisible = true;
            StatusBar.EnableFeatures(StatusBarFeatures.All, false);

            if (InfoBlock.LevelNumber == 0 && !State.Unfurl.RestartOW)
            {
                LoadRoom(CurRoomId, 0);
                SetPlayerExitPosOW(CurRoomId);
            }
            else
            {
                LoadRoom(InfoBlock.StartRoomId, 0);
                Game.Link.X = StartX;
                Game.Link.Y = InfoBlock.StartY;
            }

            for (var i = 0; i < LevelInfoBlock.LevelPaletteCount; i++)
            {
                Graphics.SetPaletteIndexed((Palette)i, InfoBlock.GetPalette(i));
            }

            SetPlayerColor();
            Graphics.UpdatePalettes();
            return;
        }

        if (State.Unfurl.Timer > 0)
        {
            State.Unfurl.Timer--;
            return;
        }

        if (State.Unfurl.Left == 0 || Game.Cheats.SpeedUp)
        {
            StatusBar.EnableFeatures(StatusBarFeatures.EquipmentAndMap, true);
            if (!IsOverworld())
            {
                Game.Sound.PlaySong(InfoBlock.SongId, SongStream.MainSong, true);
            }
            GotoEnter(Direction.Up);
            return;
        }

        if (State.Unfurl.StepTimer == 0)
        {
            State.Unfurl.Left -= 8;
            State.Unfurl.Right += 8;
            State.Unfurl.StepTimer = 4;
        }
        else
        {
            State.Unfurl.StepTimer--;
        }
    }

    private void DrawUnfurl()
    {
        if (State.Unfurl.Substate == UnfurlState.Substates.Start) return;

        var width = State.Unfurl.Right - State.Unfurl.Left;

        using (var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight))
        {
            ClearScreen();
        }

        using (var _ = Graphics.SetClip(State.Unfurl.Left, TileMapBaseY, width, TileMapHeight))
        {
            DrawRoomNoObjects(SpritePriority.None);
        }
    }

    public void EndLevel()
    {
        GotoEndLevel();
    }

    private void GotoEndLevel()
    {
        State.EndLevel.Substate = EndLevelState.Substates.Start;
        CurMode = GameMode.EndLevel;
    }

    private void UpdateEndLevel()
    {
        sEndLevelFuncs[(int)State.EndLevel.Substate]();
    }

    private void UpdateEndLevel_Start()
    {
        State.EndLevel.Substate = EndLevelState.Substates.Wait1;
        State.EndLevel.Timer = EndLevelState.Wait1Time;

        State.EndLevel.Left = 0;
        State.EndLevel.Right = TileMapWidth;
        State.EndLevel.StepTimer = 4;

        StatusBar.EnableFeatures(StatusBarFeatures.Equipment, false);
        Game.Sound.PlaySong(SongId.Triforce, SongStream.MainSong, false);
    }

    private void UpdateEndLevel_Wait()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(State.EndLevel.Timer);

        if (State.EndLevel.Timer > 0)
        {
            State.EndLevel.Timer--;
            return;
        }

        if (State.EndLevel.Substate == EndLevelState.Substates.Wait3)
        {
            GotoLoadLevel(0);
        }
        else
        {
            State.EndLevel.Substate += 1;
            if (State.EndLevel.Substate == EndLevelState.Substates.Flash)
            {
                State.EndLevel.Timer = EndLevelState.FlashTime;
            }
        }
    }

    private void UpdateEndLevel_Flash()
    {
        if (State.EndLevel.Timer == 0)
        {
            State.EndLevel.Substate += 1;
            return;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(State.EndLevel.Timer);

        var step = State.EndLevel.Timer & 0x7;
        switch (step)
        {
            case 0: SetFlashPalette(); break;
            case 3: SetLevelPalette(); break;
        }
        State.EndLevel.Timer--;
    }

    private void UpdateEndLevel_FillHearts()
    {
        var maxHeartValue = Profile.GetMaxHeartsValue();

        Game.Sound.PlayEffect(SoundEffect.Character);

        if (Profile.Hearts == maxHeartValue)
        {
            State.EndLevel.Substate += 1;
            State.EndLevel.Timer = EndLevelState.Wait2Time;
        }
        else
        {
            FillHearts(6);
        }
    }

    private void UpdateEndLevel_Furl()
    {
        if (State.EndLevel.Left == WorldMidX)
        {
            State.EndLevel.Substate += 1;
            State.EndLevel.Timer = EndLevelState.Wait3Time;
        }
        else if (State.EndLevel.StepTimer == 0)
        {
            State.EndLevel.Left += 8;
            State.EndLevel.Right -= 8;
            State.EndLevel.StepTimer = 4;
        }
        else
        {
            State.EndLevel.StepTimer--;
        }
    }

    private void DrawEndLevel()
    {
        var left = 0;
        var width = TileMapWidth;

        if (State.EndLevel.Substate >= EndLevelState.Substates.Furl)
        {
            left = State.EndLevel.Left;
            width = State.EndLevel.Right - State.EndLevel.Left;

            using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
            ClearScreen();
        }

        using (var _ = Graphics.SetClip(left, TileMapBaseY, width, TileMapHeight))
        {
            DrawRoomNoObjects(SpritePriority.None);
        }

        DrawLinkLiftingItem(ItemId.TriforcePiece);
    }

    public void WinGame()
    {
        GotoWinGame();
    }

    private void GotoWinGame()
    {
        State.WinGame.Substate = WinGameState.Substates.Start;
        State.WinGame.Timer = 162;
        State.WinGame.Left = 0;
        State.WinGame.Right = TileMapWidth;
        State.WinGame.StepTimer = 0;
        State.WinGame.NpcVisual = WinGameState.NpcVisualState.Stand;

        CurMode = GameMode.WinGame;
    }

    private void UpdateWinGame()
    {
        sWinGameFuncs[(int)State.WinGame.Substate]();
    }

    private static readonly byte[] _winGameStr1 = {
        0x1d, 0x11, 0x0a, 0x17, 0x14, 0x1c, 0x24, 0x15, 0x12, 0x17, 0x14, 0x28, 0x22, 0x18, 0x1e, 0x2a,
        0x1b, 0x8e, 0x64, 0x1d, 0x11, 0x0e, 0x24, 0x11, 0x0e, 0x1b, 0x18, 0x24, 0x18, 0x0f, 0x24, 0x11,
        0x22, 0x1b, 0x1e, 0x15, 0x0e, 0xec
    };

    private void UpdateWinGame_Start()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(State.WinGame.Timer);

        if (State.WinGame.Timer > 0)
        {
            State.WinGame.Timer--;
            return;
        }

        if (State.WinGame.Left == WorldMidX)
        {
            State.WinGame.Substate = WinGameState.Substates.Text1;
            StatusBar.EnableFeatures(StatusBarFeatures.EquipmentAndMap, false);

            // A959

            TextBox1 = new TextBox(Game, _winGameStr1); // FIX
        }
        else if (State.WinGame.StepTimer == 0)
        {
            State.WinGame.Left += 8;
            State.WinGame.Right -= 8;
            State.WinGame.StepTimer = 4;
        }
        else
        {
            State.WinGame.StepTimer--;
        }
    }

    private void UpdateWinGame_Text1()
    {
        TextBox1.Update();
        if (TextBox1.IsDone())
        {
            State.WinGame.Substate = WinGameState.Substates.Stand;
            State.WinGame.Timer = 76;
        }
    }

    private void UpdateWinGame_Stand()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(State.WinGame.Timer);

        State.WinGame.Timer--;
        if (State.WinGame.Timer == 0)
        {
            State.WinGame.Substate = WinGameState.Substates.Hold1;
            State.WinGame.Timer = 64;
        }
    }

    private void UpdateWinGame_Hold1()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(State.WinGame.Timer);

        State.WinGame.NpcVisual = WinGameState.NpcVisualState.Lift;
        State.WinGame.Timer--;
        if (State.WinGame.Timer == 0)
        {
            State.WinGame.Substate = WinGameState.Substates.Colors;
            State.WinGame.Timer = 127;
        }
    }

    private void UpdateWinGame_Colors()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(State.WinGame.Timer);

        State.WinGame.Timer--;
        if (State.WinGame.Timer == 0)
        {
            State.WinGame.Substate = WinGameState.Substates.Hold2;
            State.WinGame.Timer = 131;
            Game.Sound.PlaySong(SongId.Ending, SongStream.MainSong, true);
        }
    }

    private static ReadOnlySpan<byte> WinGameStr2 => new byte[] {
        0x25, 0x25, 0x25, 0x25, 0x25, 0x25, 0x25, 0x25,
        0x0f, 0x12, 0x17, 0x0a, 0x15, 0x15, 0x22, 0x28,
        0xa5, 0x65,
        0x19, 0x0e, 0x0a, 0x0c, 0x0e, 0x24, 0x1b, 0x0e,
        0x1d, 0x1e, 0x1b, 0x17, 0x1c, 0x24, 0x1d, 0x18, 0x24, 0x11, 0x22, 0x1b, 0x1e, 0x15, 0x0e, 0x2c,
        0xa5, 0x65, 0x65, 0x25, 0x25,
        0x1d, 0x11, 0x12, 0x1c, 0x24, 0x0e, 0x17, 0x0d, 0x1c, 0x24, 0x1d, 0x11, 0x0e, 0x24, 0x1c, 0x1d,
        0x18, 0x1b, 0x22, 0x2c, 0xe5
    };

    private void UpdateWinGame_Hold2()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(State.WinGame.Timer);

        State.WinGame.Timer--;
        if (State.WinGame.Timer == 0)
        {
            State.WinGame.Substate = WinGameState.Substates.Text2;
            TextBox2 = new TextBox(Game, WinGameStr2.ToArray(), 8); // TODO
            TextBox2.SetY(WinGameState.TextBox2Top);
        }
    }

    private void UpdateWinGame_Text2()
    {
        if (TextBox2 == null) throw new Exception();

        TextBox2.Update();
        if (TextBox2.IsDone())
        {
            State.WinGame.Substate = WinGameState.Substates.Hold3;
            State.WinGame.Timer = 129;
        }
    }

    private void UpdateWinGame_Hold3()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(State.WinGame.Timer);

        State.WinGame.Timer--;
        if (State.WinGame.Timer == 0)
        {
            State.WinGame.Substate = WinGameState.Substates.NoObjects;
            State.WinGame.Timer = 32;
        }
    }

    private void UpdateWinGame_NoObjects()
    {
        State.WinGame.NpcVisual = WinGameState.NpcVisualState.None;
        State.WinGame.Timer--;
        if (State.WinGame.Timer == 0)
        {
            Credits = new CreditsType();
            State.WinGame.Substate = WinGameState.Substates.Credits;
        }
    }

    private void UpdateWinGame_Credits()
    {
        if (Credits == null) throw new Exception();

        var boxes = new[] { TextBox1, TextBox2 };
        var startYs = new[] { TextBox.StartY, WinGameState.TextBox2Top };

        for (var i = 0; i < boxes.Length; i++)
        {
            var box = boxes[i];
            if (box != null)
            {
                var textToCreditsY = CreditsType.StartY - startYs[i];
                box.SetY(Credits.GetTop() - textToCreditsY);
                var bottom = box.GetY() + box.GetHeight();
                if (bottom <= 0)
                {
                    switch (i)
                    {
                        case 0: TextBox1 = null; break;
                        case 1: TextBox2 = null; break;
                    }
                }
            }
        }

        Credits.Update();
        if (Credits.IsDone())
        {
            if (Game.Input.IsButtonPressing(Button.Start))
            {
                Credits = null;
                Game.Link = null;
                DeleteObjects();
                SubmenuOffsetY = 0;
                StatusBarVisible = false;
                StatusBar.EnableFeatures(StatusBarFeatures.All, true);

                // JOE: TODO: I think this conversion is ok...
                Profile.Quest = 1;
                Profile.Items[ItemSlot.HeartContainers] = PlayerProfile.DefaultHearts;
                Profile.Items[ItemSlot.MaxBombs] = PlayerProfile.DefaultBombs;
                SaveFolder.Save();

                Game.Sound.StopAll();
                GotoFileMenu();
            }
        }
        else
        {
            var statusTop = Credits.GetTop() - CreditsType.StartY;
            var statusBottom = statusTop + StatusBar.StatusBarHeight;
            SubmenuOffsetY = statusBottom > 0 ? statusTop : -StatusBar.StatusBarHeight;
        }
    }

    private void DrawWinGame()
    {
        SKColor backColor;

        Graphics.SetClip(0, 0, Global.StdViewWidth, Global.StdViewHeight);
        if (State.WinGame.Substate == WinGameState.Substates.Colors)
        {
            var sysColors = new[] { 0x0F, 0x2A, 0x16, 0x12 };
            var frame = State.WinGame.Timer & 3;
            var sysColor = sysColors[frame];
            ClearScreen(sysColor);
            backColor = Graphics.GetSystemColor(sysColor);
        }
        else
        {
            ClearScreen();
            backColor = SKColors.Black;
        }
        Graphics.ResetClip();

        StatusBar.Draw(SubmenuOffsetY, backColor);

        if (State.WinGame.Substate == WinGameState.Substates.Start)
        {
            var left = State.WinGame.Left;
            var width = State.WinGame.Right - State.WinGame.Left;

            Graphics.SetClip(left, TileMapBaseY, width, TileMapHeight);
            DrawRoomNoObjects(SpritePriority.None);
            Graphics.ResetClip();

            Game.Link.Draw();
            DrawObjects(out _);
        }
        else
        {
            var zelda = Objects[(int)ObjectSlot.Monster1];

            if (State.WinGame.NpcVisual == WinGameState.NpcVisualState.Stand)
            {
                zelda.Draw();
                Game.Link.Draw();
            }
            else if (State.WinGame.NpcVisual == WinGameState.NpcVisualState.Lift)
            {
                DrawZeldaLiftingTriforce(zelda.X, zelda.Y);
                DrawLinkLiftingItem(ItemId.TriforcePiece);
            }

            Credits?.Draw();
            TextBox1?.Draw();
            TextBox2?.Draw();
        }
    }

    private void GotoStairs(TileBehavior behavior)
    {
        State.Stairs.Substate = StairsState.Substates.Start;
        State.Stairs.TileBehavior = behavior;
        State.Stairs.PlayerPriority = SpritePriority.AboveBg;

        CurMode = GameMode.Stairs;
    }

    private void UpdateStairsState()
    {
        switch (State.Stairs.Substate)
        {
            case StairsState.Substates.Start:
            {
                State.Stairs.PlayerPriority = SpritePriority.BelowBg;

                if (IsOverworld())
                    Game.Sound.StopAll();

                if (State.Stairs.TileBehavior == TileBehavior.Cave)
                {
                    Game.Link.Facing = Direction.Up;

                    State.Stairs.TargetX = Game.Link.X;
                    State.Stairs.TargetY = Game.Link.Y + 0x10;
                    State.Stairs.ScrollDir = Direction.Down;
                    State.Stairs.PlayerSpeed = 0x40;
                    State.Stairs.PlayerFraction = 0;

                    State.Stairs.Substate = StairsState.Substates.WalkCave;
                    Game.Sound.PlayEffect(SoundEffect.Stairs);
                }
                else
                {
                    State.Stairs.Substate = StairsState.Substates.Walk;
                }

                break;
            }
            case StairsState.Substates.Walk when IsOverworld():
            {
                var owRoomAttrs = CurrentOWRoomAttrs;
                var cave = owRoomAttrs.GetCaveId();

                if (cave <= 9)
                {
                    GotoLoadLevel(cave);
                }
                else
                {
                    GotoPlayCave();
                }
                break;
            }
            case StairsState.Substates.Walk:
                GotoPlayCellar();
                break;
            case StairsState.Substates.WalkCave when Game.Link.X == State.Stairs.TargetX
                && Game.Link.Y == State.Stairs.TargetY:
            {
                var owRoomAttrs = CurrentOWRoomAttrs;
                var cave = owRoomAttrs.GetCaveId();

                if (cave <= 9)
                {
                    GotoLoadLevel(cave);
                }
                else
                {
                    GotoPlayCave();
                }
                break;
            }
            case StairsState.Substates.WalkCave:
                MovePlayer(State.Stairs.ScrollDir, State.Stairs.PlayerSpeed, ref State.Stairs.PlayerFraction);
                Game.Link.Animator.Advance();
                break;
        }
    }

    private void DrawStairsState()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        DrawRoomNoObjects(State.Stairs.PlayerPriority);
    }

    private void GotoPlayCellar()
    {
        State.PlayCellar.Substate = PlayCellarState.Substates.Start;
        State.PlayCellar.PlayerPriority = SpritePriority.None;

        CurMode = GameMode.InitPlayCellar;
    }

    private void UpdatePlayCellar()
    {
        sPlayCellarFuncs[(int)State.PlayCellar.Substate]();
    }

    private void UpdatePlayCellar_Start()
    {
        State.PlayCellar.Substate = PlayCellarState.Substates.FadeOut;
        State.PlayCellar.FadeTimer = 11;
        State.PlayCellar.FadeStep = 0;
    }

    private void UpdatePlayCellar_FadeOut()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(State.PlayCellar.FadeTimer);

        if (State.PlayCellar.FadeTimer > 0)
        {
            State.PlayCellar.FadeTimer--;
            return;
        }

        for (var i = 0; i < LevelInfoBlock.FadePals; i++)
        {
            var step = State.PlayCellar.FadeStep;
            Graphics.SetPaletteIndexed((Palette)i + 2, InfoBlock.OutOfCellarPalette(step, i));
        }
        Graphics.UpdatePalettes();
        State.PlayCellar.FadeTimer = 9;
        State.PlayCellar.FadeStep++;

        if (State.PlayCellar.FadeStep == LevelInfoBlock.FadeLength)
        {
            State.PlayCellar.Substate = PlayCellarState.Substates.LoadRoom;
        }
    }

    private void UpdatePlayCellar_LoadRoom()
    {
        var roomId = FindCellarRoomId(CurRoomId, out var isLeft);

        if (roomId >= 0)
        {
            var x = isLeft ? 0x30 : 0xC0;

            LoadRoom(roomId, 0);

            Game.Link.X = x;
            Game.Link.Y = 0x44;
            Game.Link.Facing = Direction.Down;

            State.PlayCellar.TargetY = 0x60;
            State.PlayCellar.Substate = PlayCellarState.Substates.FadeIn;
            State.PlayCellar.FadeTimer = 35;
            State.PlayCellar.FadeStep = 3;
        }
        else
        {
            GotoPlay();
        }
    }

    private void UpdatePlayCellar_FadeIn()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(State.PlayCellar.FadeTimer);

        if (State.PlayCellar.FadeTimer > 0)
        {
            State.PlayCellar.FadeTimer--;
            return;
        }

        for (var i = 0; i < LevelInfoBlock.FadePals; i++)
        {
            var step = State.PlayCellar.FadeStep;
            Graphics.SetPaletteIndexed((Palette)i + 2, InfoBlock.InCellarPalette(step, i));
        }
        Graphics.UpdatePalettes();
        State.PlayCellar.FadeTimer = 9;
        State.PlayCellar.FadeStep--;

        if (State.PlayCellar.FadeStep < 0)
        {
            State.PlayCellar.Substate = PlayCellarState.Substates.Walk;
        }
    }

    private void UpdatePlayCellar_Walk()
    {
        State.PlayCellar.PlayerPriority = SpritePriority.AboveBg;

        if (Game.Link.Y == State.PlayCellar.TargetY)
        {
            FromUnderground = 1;
            GotoPlay(RoomType.Cellar);
        }
        else
        {
            Game.Link.MoveLinear(Direction.Down, Link.WalkSpeed);
            Game.Link.Animator.Advance();
        }
    }

    private void DrawPlayCellar()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        DrawRoomNoObjects(State.PlayCellar.PlayerPriority);
    }

    private void GotoLeaveCellar()
    {
        State.LeaveCellar.Substate = LeaveCellarState.Substates.Start;
        CurMode = GameMode.LeaveCellar;
    }

    private void UpdateLeaveCellar()
    {
        sLeaveCellarFuncs[(int)State.LeaveCellar.Substate]();
    }

    private void UpdateLeaveCellar_Start()
    {
        if (IsOverworld())
        {
            State.LeaveCellar.Substate = LeaveCellarState.Substates.Wait;
            State.LeaveCellar.Timer = 29;
        }
        else
        {
            State.LeaveCellar.Substate = LeaveCellarState.Substates.FadeOut;
            State.LeaveCellar.FadeTimer = 11;
            State.LeaveCellar.FadeStep = 0;
        }
    }

    private void UpdateLeaveCellar_FadeOut()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(State.LeaveCellar.FadeTimer);

        if (State.LeaveCellar.FadeTimer > 0)
        {
            State.LeaveCellar.FadeTimer--;
            return;
        }

        for (var i = 0; i < LevelInfoBlock.FadePals; i++)
        {
            var step = State.LeaveCellar.FadeStep;
            Graphics.SetPaletteIndexed((Palette)i + 2, InfoBlock.InCellarPalette(step, i));
        }
        Graphics.UpdatePalettes();
        State.LeaveCellar.FadeTimer = 9;
        State.LeaveCellar.FadeStep++;

        if (State.LeaveCellar.FadeStep == LevelInfoBlock.FadeLength)
        {
            State.LeaveCellar.Substate = LeaveCellarState.Substates.LoadRoom;
        }
    }

    private void UpdateLeaveCellar_LoadRoom()
    {
        var uwRoomAttrs = CurrentUWRoomAttrs;
        var nextRoomId = Game.Link.X < 0x80
            ? uwRoomAttrs.GetLeftCellarExitRoomId()
            : uwRoomAttrs.GetRightCellarExitRoomId();

        LoadRoom(nextRoomId, 0);

        Game.Link.X = 0x60;
        Game.Link.Y = 0xA0;
        Game.Link.Facing = Direction.Down;

        State.LeaveCellar.Substate = LeaveCellarState.Substates.FadeIn;
        State.LeaveCellar.FadeTimer = 35;
        State.LeaveCellar.FadeStep = 3;
    }

    private void UpdateLeaveCellar_FadeIn()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(State.LeaveCellar.FadeTimer);

        if (State.LeaveCellar.FadeTimer > 0)
        {
            State.LeaveCellar.FadeTimer--;
            return;
        }

        for (var i = 0; i < LevelInfoBlock.FadePals; i++)
        {
            var step = State.LeaveCellar.FadeStep;
            Graphics.SetPaletteIndexed((Palette)i + 2, InfoBlock.OutOfCellarPalette(step, i));
        }
        Graphics.UpdatePalettes();
        State.LeaveCellar.FadeTimer = 9;
        State.LeaveCellar.FadeStep--;

        if (State.LeaveCellar.FadeStep < 0)
        {
            State.LeaveCellar.Substate = LeaveCellarState.Substates.Walk;
        }
    }

    private void UpdateLeaveCellar_Walk()
    {
        GotoEnter(Direction.None);
    }

    private void UpdateLeaveCellar_Wait()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(State.LeaveCellar.FadeTimer);

        if (State.LeaveCellar.Timer > 0)
        {
            State.LeaveCellar.Timer--;
            return;
        }

        State.LeaveCellar.Substate = LeaveCellarState.Substates.LoadOverworldRoom;
    }

    private void UpdateLeaveCellar_LoadOverworldRoom()
    {
        for (var i = 0; i < 2; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i + 2, InfoBlock.GetPalette(i + 2));
        }
        Graphics.UpdatePalettes();

        LoadRoom(CurRoomId, 0);
        SetPlayerExitPosOW(CurRoomId);
        GotoEnter(Direction.None);
        Game.Link.Facing = Direction.Down;
    }

    private void DrawLeaveCellar()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);

        if (State.LeaveCellar.Substate == LeaveCellarState.Substates.Start)
        {
        }
        else if (State.LeaveCellar.Substate is LeaveCellarState.Substates.Wait or LeaveCellarState.Substates.LoadOverworldRoom)
        {
            ClearScreen();
        }
        else
        {
            DrawRoomNoObjects(SpritePriority.None);
        }
    }

    private void GotoPlayCave()
    {
        State.PlayCave.Substate = PlayCaveState.Substates.Start;

        CurMode = GameMode.InitPlayCave;
    }

    private void UpdatePlayCave()
    {
        sPlayCaveFuncs[(int)State.PlayCave.Substate]();
    }

    private void UpdatePlayCave_Start()
    {
        State.PlayCave.Substate = PlayCaveState.Substates.Wait;
        State.PlayCave.Timer = 27;
    }

    private void UpdatePlayCave_Wait()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(State.PlayCave.Timer);

        if (State.PlayCave.Timer > 0)
        {
            State.PlayCave.Timer--;
            return;
        }

        State.PlayCave.Substate = PlayCaveState.Substates.LoadRoom;
    }

    private void UpdatePlayCave_LoadRoom()
    {
        var paletteSet = ExtraData.GetItem<PaletteSet>(Extra.CavePalettes);
        var caveLayout = FindSparseFlag(Sparse.Shortcut, CurRoomId) ? Cave.Shortcut : Cave.Items;

        LoadCaveRoom(caveLayout);

        State.PlayCave.Substate = PlayCaveState.Substates.Walk;
        State.PlayCave.TargetY = 0xD5;

        Game.Link.X = 0x70;
        Game.Link.Y = 0xDD;
        Game.Link.Facing = Direction.Up;

        for (var i = 0; i < 2; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i + 2, paletteSet.GetPalette(i));
        }
        Graphics.UpdatePalettes();
    }

    private void UpdatePlayCave_Walk()
    {
        if (Game.Link.Y == State.PlayCave.TargetY)
        {
            FromUnderground = 1;
            GotoPlay(RoomType.Cave);
        }
        else
        {
            Game.Link.MoveLinear(Direction.Up, Link.WalkSpeed);
            Game.Link.Animator.Advance();
        }
    }

    private void DrawPlayCave()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);

        if (State.PlayCave.Substate is PlayCaveState.Substates.Wait or PlayCaveState.Substates.LoadRoom)
        {
            ClearScreen();
        }
        else if (State.PlayCave.Substate == PlayCaveState.Substates.Walk)
        {
            DrawRoomNoObjects();
        }
    }

    public void GotoDie()
    {
        State.Death.Substate = DeathState.Substates.Start;

        CurMode = GameMode.Death;
    }

    private void UpdateDie()
    {
        // ORIGINAL: Some of these are handled with object timers.
        if (State.Death.Timer > 0)
        {
            State.Death.Timer--;
            // JOE: Original does not return here.
        }

        sDeathFuncs[(int)State.Death.Substate]();
    }

    private void UpdateDie_Start()
    {
        Game.Link.InvincibilityTimer = 0x10;
        State.Death.Timer = 0x20;
        State.Death.Substate = DeathState.Substates.Flash;
        Game.Sound.StopEffects();
        Game.Sound.PlaySong(SongId.Death, SongStream.MainSong, false);
    }

    private void UpdateDie_Flash()
    {
        Game.Link.DecInvincibleTimer();

        if (State.Death.Timer == 0)
        {
            State.Death.Timer = 6;
            State.Death.Substate = DeathState.Substates.Wait1;
        }
    }

    private static readonly byte[][] _deathRedPals = {
        new byte[] {0x0F, 0x17, 0x16, 0x26 },
        new byte[] {0x0F, 0x17, 0x16, 0x26 },
    };

    private void UpdateDie_Wait1()
    {
        // TODO: the last 2 frames make the whole play area use palette 3.

        if (State.Death.Timer == 0)
        {
            SetLevelPalettes(_deathRedPals);

            State.Death.Step = 16;
            State.Death.Timer = 0;
            State.Death.Substate = DeathState.Substates.Turn;
        }
    }

    private void UpdateDie_Turn()
    {
        if (State.Death.Step == 0)
        {
            State.Death.Step = 4;
            State.Death.Timer = 0;
            State.Death.Substate = DeathState.Substates.Fade;
        }
        else
        {
            if (State.Death.Timer == 0)
            {
                State.Death.Timer = 5;
                State.Death.Step--;

                ReadOnlySpan<Direction> dirs = [ Direction.Down, Direction.Left, Direction.Up, Direction.Right ];

                var dir = dirs[State.Death.Step & 3];
                Game.Link.Facing = dir;
            }
        }
    }

    private void UpdateDie_Fade()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(State.Death.Step);

        if (State.Death.Step > 0)
        {
            if (State.Death.Timer == 0)
            {
                State.Death.Timer = 10;
                State.Death.Step--;

                var seq = 3 - State.Death.Step;

                SetLevelPalettes(InfoBlock.DeathPalettes(seq));
            }
            return;
        }

        State.Death.Substate = DeathState.Substates.GrayLink;
    }

    private void UpdateDie_GrayLink()
    {
        // static const byte grayPal[4] = { 0, 0x10, 0x30, 0 };

        Graphics.SetPaletteIndexed(Palette.Player, new byte[] { 0, 0x10, 0x30, 0 });
        Graphics.UpdatePalettes();

        State.Death.Substate = DeathState.Substates.Spark;
        State.Death.Timer = 0x18;
        State.Death.Step = 0;
    }

    private void UpdateDie_Spark()
    {
        if (State.Death.Timer == 0)
        {
            if (State.Death.Step == 0)
            {
                State.Death.Timer = 10;
                Game.Sound.PlayEffect(SoundEffect.Character);
            }
            else if (State.Death.Step == 1)
            {
                State.Death.Timer = 4;
            }
            else
            {
                State.Death.Substate = DeathState.Substates.Wait2;
                State.Death.Timer = 46;
            }
            State.Death.Step++;
        }
    }

    private void UpdateDie_Wait2()
    {
        if (State.Death.Timer == 0)
        {
            State.Death.Substate = DeathState.Substates.GameOver;
            State.Death.Timer = 0x60;
        }
    }

    private void UpdateDie_GameOver()
    {
        if (State.Death.Timer == 0)
        {
            Profile.Deaths++;
            GotoContinueQuestion();
        }
    }

    private void DrawDie()
    {
        if (State.Death.Substate < DeathState.Substates.GameOver)
        {
            using (var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight))
            {
                DrawRoomNoObjects(SpritePriority.None);
            }
            var player = Game.Link;

            if (State.Death.Substate == DeathState.Substates.Spark && State.Death.Step > 0)
            {
                GlobalFunctions.DrawSparkle(player.X, player.Y, Palette.Blue, State.Death.Step - 1);
            }
            else if (State.Death.Substate <= DeathState.Substates.Spark)
            {
                Game.Link.Draw();
            }
            return;
        }

        ReadOnlySpan<byte> gameOver = [ 0x10, 0x0A, 0x16, 0x0E, 0x24, 0x18, 0x1F, 0x0E, 0x1B ];
        GlobalFunctions.DrawString(gameOver, 0x60, 0x90, 0);
    }

    public void GotoContinueQuestion()
    {
        State.Continue.Substate = ContinueState.Substates.Start;
        State.Continue.SelectedIndex = 0;

        CurMode = GameMode.ContinueQuestion;
    }

    private void UpdateContinueQuestion()
    {
        if (State.Continue.Substate == ContinueState.Substates.Start)
        {
            StatusBarVisible = false;
            Game.Sound.PlaySong(SongId.GameOver, SongStream.MainSong, true);
            State.Continue.Substate = ContinueState.Substates.Idle;
        }
        else if (State.Continue.Substate == ContinueState.Substates.Idle)
        {
            if (Game.Input.IsButtonPressing(Button.Select))
            {
                State.Continue.SelectedIndex++;
                if (State.Continue.SelectedIndex == 3)
                    State.Continue.SelectedIndex = 0;
            }
            else if (Game.Input.IsButtonPressing(Button.Start))
            {
                State.Continue.Substate = ContinueState.Substates.Chosen;
                State.Continue.Timer = 0x40;
            }
        }
        else if (State.Continue.Substate == ContinueState.Substates.Chosen)
        {
            if (State.Continue.Timer == 0)
            {
                StatusBarVisible = true;
                Game.Sound.StopAll();

                if (State.Continue.SelectedIndex == 0)
                {
                    // So, that the OW song is played in the Enter mode.
                    FromUnderground = 2;
                    Game.Link = new Link(Game);
                    Profile.Hearts = PlayerProfile.GetMaxHeartsValue(PlayerProfile.DefaultHearts);
                    GotoUnfurl(true);
                }
                else if (State.Continue.SelectedIndex == 1)
                {
                    SaveFolder.Save();
                    GotoFileMenu();
                }
                else if (State.Continue.SelectedIndex == 2)
                {
                    GotoFileMenu();
                }
            }
            else
            {
                State.Continue.Timer--;
            }
        }
    }

    private void DrawContinueQuestion()
    {
        ReadOnlySpan<string> strs = [ "Continue", "Save", "Retry" ];

        ClearScreen();

        var y = 0x50;

        for (var i = 0; i < 3; i++, y += 24)
        {
            var pal = 0;
            if (State.Continue.Substate == ContinueState.Substates.Chosen
                && State.Continue.SelectedIndex == i)
            {
                pal = (Game.GetFrameCounter() / 4) & 1;
            }

            GlobalFunctions.DrawString(strs[i], 0x50, y, (Palette)pal);
        }

        y = 0x50 + (State.Continue.SelectedIndex * 24);
        GlobalFunctions.DrawChar(Char.FullHeart, 0x40, y, Palette.RedFgPalette);
    }

    private void GotoFileMenu()
    {
        GotoFileMenu(SaveFolder.Profiles);
    }

    private void GotoFileMenu(PlayerProfile[] summaries)
    {
        NextGameMenu = new ProfileSelectMenu(Game, summaries);
        CurMode = GameMode.GameMenu;
    }

    private void GotoRegisterMenu(PlayerProfile[] summaries)
    {
        NextGameMenu = new RegisterMenu(Game, summaries);
        CurMode = GameMode.Register;
    }

    private void GotoEliminateMenu(PlayerProfile[] summaries)
    {
        NextGameMenu = new EliminateMenu(Game, summaries);
        CurMode = GameMode.Elimination;
    }

    private void UpdateGameMenu() => GameMenu.Update();
    private void UpdateRegisterMenu() => GameMenu.Update();
    private void UpdateEliminateMenu() => GameMenu.Update();
    private void DrawGameMenu() => GameMenu?.Draw();

    private int FindCellarRoomId(int mainRoomId, out bool isLeft)
    {
        isLeft = false;
        for (var i = 0; i < LevelInfoBlock.LevelCellarCount; i++)
        {
            var cellarRoomId = InfoBlock.CellarRoomIds[i];
            if (cellarRoomId >= 0x80) break;

            var uwRoomAttrs = GetUWRoomAttrs(cellarRoomId);
            if (mainRoomId == uwRoomAttrs.GetLeftCellarExitRoomId())
            {
                isLeft = true;
                return cellarRoomId;
            }

            if (mainRoomId == uwRoomAttrs.GetRightCellarExitRoomId())
            {
                isLeft = false;
                return cellarRoomId;
            }
        }

        return -1;
    }

    private void DrawRoomNoObjects(SpritePriority playerPriority = SpritePriority.AboveBg)
    {
        ClearScreen();

        if (playerPriority == SpritePriority.BelowBg)
        {
            Game.Link.Draw();
        }

        DrawRoom();

        if (playerPriority == SpritePriority.AboveBg)
        {
            Game.Link.Draw();
        }

        if (IsUWMain(CurRoomId))
        {
            DrawDoors(CurRoomId, true, 0, 0);
        }
    }

    private static void NoneTileAction(int row, int col, TileInteraction interaction)
    {
        // Nothing to do. Should never be called.
        // Debugger.Break(); // JOE: TODO: This was called. I burned myself with the red candle.
    }

    private void PushTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load) return;

        var rock = new RockObj(Game, col * TileWidth, TileMapBaseY + row * TileHeight);
        SetBlockObj(rock);
    }

    private void BombTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load) return;

        if (GotSecret())
        {
            SetMob(row, col, BlockObjType.MobCave);
        }
        else
        {
            var rockWall = new RockWallActor(Game, col * TileWidth, TileMapBaseY + row * TileHeight);
            SetBlockObj(rockWall);
        }
    }

    private void BurnTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load) return;

        if (GotSecret())
        {
            SetMob(row, col, BlockObjType.MobStairs);
        }
        else
        {
            var tree = new TreeActor(Game, col * TileWidth, TileMapBaseY + row * TileHeight);
            SetBlockObj(tree);
        }
    }

    private void HeadstoneTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load) return;

        var headstone = new HeadstoneObj(Game, col * TileWidth, TileMapBaseY + row * TileHeight);
        SetBlockObj(headstone);
    }

    private void LadderTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Touch) return;

        Debug.WriteLine("Touch water: {0}, {1}", row, col);
    }

    private void RaftTileAction(int row, int col, TileInteraction interaction)
    {
        // TODO: instantiate the Dock here on Load interaction, and set its position.

        if (interaction != TileInteraction.Cover) return;

        Debug.WriteLine("Cover dock: {0}, {1}", row, col);

        if (GetItem(ItemSlot.Raft) == 0) return;
        if (!FindSparseFlag(Sparse.Dock, CurRoomId)) return;
    }

    private void CaveTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Cover) return;

        if (IsOverworld())
        {
            var behavior = GetTileBehavior(row, col);
            GotoStairs(behavior);
        }

        Debug.WriteLine("Cover cave: {0}, {1}", row, col);
    }

    private void StairsTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Cover) return;

        if (GetMode() == GameMode.Play)
        {
            GotoStairs(TileBehavior.Stairs);
        }

        Debug.WriteLine("Cover stairs: {0}, {1}", row, col);
    }

    public void GhostTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction == TileInteraction.Push) Debug.WriteLine("Push headstone: {0}, {1}", row, col);

        CommonMakeObjectAction(ObjType.FlyingGhini, row, col, interaction, ref GhostCount, GhostCells);
    }

    public void ArmosTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction == TileInteraction.Push) Debug.WriteLine("Push armos: {0}, {1}", row, col);

        CommonMakeObjectAction(ObjType.Armos, row, col, interaction, ref ArmosCount, ArmosCells);
    }

    public void CommonMakeObjectAction(
        ObjType type, int row, int col, TileInteraction interaction, ref int patchCount, Cell[] patchCells)
    {
        if (interaction == TileInteraction.Load)
        {
            if (patchCount < 16)
            {
                patchCells[patchCount] = new Cell((byte)row, (byte)col);
                patchCount++;
            }
        }
        else if (interaction == TileInteraction.Push)
        {
            var map = TileMaps[CurTileMapIndex];
            int behavior = map.Behaviors(row, col);

            if (row > 0 && map.Behaviors(row - 1, col) == behavior)
            {
                row--;
            }
            if (col > 0 && map.Behaviors(row, col - 1) == behavior)
            {
                col--;
            }

            MakeActivatedObject(type, row, col);
        }
    }

    public void MakeActivatedObject(ObjType type, int row, int col)
    {
        row += BaseRows;

        var x = col * TileWidth;
        var y = row * TileHeight;

        for (var i = (int)ObjectSlot.LastMonster; i >= 0; i--)
        {
            var obj = Objects[i];
            if (obj == null || obj.ObjType != type) continue;

            var objCol = obj.X / TileWidth;
            var objRow = obj.Y / TileHeight;

            if (objCol == col && objRow == row) return;
        }

        var freeSlot = FindEmptyMonsterSlot();
        if (freeSlot >= 0)
        {
            var obj = Actor.FromType(type, Game, x, y);
            Objects[(int)freeSlot] = obj;
            obj.ObjTimer = 0x40;
        }
    }

    public void BlockTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load) return;

        var block = new BlockObj(Game, col * TileWidth, TileMapBaseY + row * TileHeight);
        SetBlockObj(block);
    }

    public void DoorTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Push) return;

        // Based on $91D6 and old implementation Player::CheckDoor.

        Debug.WriteLine("Push door: {0}, {1}", row, col);
        var player = Player;

        var doorType = GetDoorType(player.MovingDirection);

        switch (doorType)
        {
            case DoorType.FalseWall:
            case DoorType.FalseWall2:
                if (player.ObjTimer == 0)
                {
                    player.ObjTimer = 0x18;
                }
                else if (player.ObjTimer == 1)
                {
                    LeaveRoom(player.Facing, CurRoomId);
                    player.Stop();
                }
                break;

            case DoorType.Bombable:
                if (GetEffectiveDoorState(player.MovingDirection))
                {
                    LeaveRoom(player.Facing, CurRoomId);
                    player.Stop();
                }
                break;

            case DoorType.Key:
            case DoorType.Key2:
                if (TriggeredDoorDir == Direction.None)
                {
                    if (UseKey())
                    {
                        // $8ADA
                        TriggeredDoorDir = player.MovingDirection;
                        TriggeredDoorCmd = 8;
                    }
                }
                break;
        }
    }
}