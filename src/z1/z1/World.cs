using System.Runtime.InteropServices;

namespace z1;

using System.Diagnostics;
using SkiaSharp;
using z1.Actors;
using z1.UI;

internal enum DoorType { Open, None, FalseWall, FalseWall2, Bombable, Key, Key2, Shutter }
internal enum TileInteraction { Load, Push, Touch, Cover }
internal enum SpritePriority { None, AboveBg, BelowBg }

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
    public TileBehavior AsBehaviors(int row, int col) => (TileBehavior)_tileBehaviors[row * World.Columns + col];
}

internal enum UniqueRoomIds
{
    TopRightOverworldSecret = 0x0F,
}

internal sealed unsafe partial class World
{
    public const int LevelGroups = 3;

    internal enum Cave
    {
        Items = 0x79,
        Shortcut = 0x7A,
    }

    internal enum Secret
    {
        None,
        FoesDoor,
        Ringleader,
        LastBoss,
        BlockDoor,
        BlockStairs,
        MoneyOrLife,
        FoesItem
    }

    internal enum TileScheme
    {
        Overworld,
        UnderworldMain,
        UnderworldCellar,
    }

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
    private const int RoomHistoryLength = 6;
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

    public delegate void LoadMobFunc(ref TileMap map, int row, int col, int mobIndex);

    public readonly OnScreenDisplay OnScreenDisplay = new();

    public LevelDirectory directory;
    public LevelInfoBlock infoBlock;
    public RoomCols[] roomCols = new RoomCols[UniqueRooms];
    public TableResource<byte> colTables;
    public readonly TileMap[] tileMaps = new TileMap[] { new(), new(), new() };
    public RoomAttrs[] roomAttrs = new RoomAttrs[Rooms];
    public int curRoomId;
    public int curTileMapIndex;
    public byte[] tileAttrs = new byte[MobTypes];
    public byte[] tileBehaviors = new byte[TileTypes];
    public TableResource<byte> sparseRoomAttrs;
    public TableResource<byte> extraData;
    public TableResource<byte> objLists;
    public TableResource<byte> textTable;
    public ListResource<byte> primaryMobs;
    public ListResource<byte> secondaryMobs;
    public LoadMobFunc loadMobFunc;

    public int rowCount;
    public int colCount;
    public int startRow;
    public int startCol;
    public int tileTypeCount;
    public int marginRight;
    public int marginLeft;
    public int marginBottom;
    public int marginTop;
    public SKBitmap? wallsBmp;
    public SKBitmap? doorsBmp;

    public GameMode lastMode;
    public GameMode curMode;
    public StatusBar statusBar;
    public SubmenuType menu;
    public Credits credits;
    public TextBox textBox1;
    public TextBox textBox2;
    public Menu gameMenu;
    public Menu nextGameMenu;

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
    public byte Pause;                  // E0
    public byte Submenu;                // E1
    public int SubmenuOffsetY;         // EC
    public bool StatusBarVisible;
    public int[] LevelKillCounts = new int[(int)LevelBlock.Rooms];
    public byte[] RoomHistory = new byte[RoomHistoryLength];

    public Link Player => Game.Link;
    public bool giveFakePlayerPos;
    public int playerPosTimer; // JOE: TODO: Unused on purpose?
    public Point fakePlayerPos;

    public Actor?[] objects = new Actor[(int)ObjectSlot.MaxObjects];
    public Actor?[] objectsToDelete = new Actor[(int)ObjectSlot.MaxObjects];
    public int objectsToDeleteCount;
    public int[] objectTimers = new int[(int)ObjectSlot.MaxObjects];
    public int curObjSlot;
    public ObjectSlot curObjectSlot
    {
        get => (ObjectSlot)curObjSlot;
        set => curObjSlot = (int)value;
    }
    public int longTimer;
    public int[] stunTimers = new int[(int)ObjectSlot.MaxObjects];
    public byte[] placeholderTypes = new byte[(int)ObjectSlot.MaxObjects];

    public Direction doorwayDir;         // 53
    public int triggeredDoorCmd;   // 54
    public Direction triggeredDoorDir;   // 55
    public int fromUnderground;    // 5A
    // JOE: TODO: This doesn't need to be reference counted anymore and should be based on the object table.
    // Though note that ones owned by Link should be excluded.
    public int activeShots;        // 34C
    public bool triggerShutters;    // 4CE
    public bool summonedWhirlwind;  // 508
    public bool powerTriforceFanfare;   // 509
    public int recorderUsed;       // 51B
    public bool candleUsed;         // 513
    public Direction shuttersPassedDirs; // 519
    public bool brightenRoom;       // 51E
    public int profileSlot;
    public PlayerProfile profile = new();
    public UWRoomFlags[] curUWBlockFlags = new UWRoomFlags[] { };
    public int ghostCount;
    public int armosCount;
    public Cell[] ghostCells = Cell.MakeMobPatchCell();
    public Cell[] armosCells = Cell.MakeMobPatchCell();

    private UWRoomAttrs CurrentUWRoomAttrs => GetUWRoomAttrs(curRoomId);
    private OWRoomAttrs CurrentOWRoomAttrs => GetOWRoomAttrs(curRoomId);

    private UWRoomAttrs GetUWRoomAttrs(int roomId) => roomAttrs[roomId];
    private OWRoomAttrs GetOWRoomAttrs(int roomId) => roomAttrs[roomId];

    public Game Game { get; }

    public World(Game game)
    {
        Game = game;
        statusBar = new StatusBar(this);
        menu = new SubmenuType(game);

        lastMode = GameMode.Demo;
        curMode = GameMode.Play;
        EdgeY = 0x40;

        Init();
    }

    private void LoadOpenRoomContext()
    {
        colCount = 32;
        rowCount = 22;
        startRow = 0;
        startCol = 0;
        tileTypeCount = 56;
        marginRight = OWMarginRight;
        marginLeft = OWMarginLeft;
        marginBottom = OWMarginBottom;
        marginTop = OWMarginTop;
    }

    private void LoadClosedRoomContext()
    {
        colCount = 24;
        rowCount = 14;
        startRow = 4;
        startCol = 4;
        tileTypeCount = 9;
        marginRight = UWMarginRight;
        marginLeft = UWMarginLeft;
        marginBottom = UWMarginBottom;
        marginTop = UWMarginTop;
    }

    private void LoadMapResourcesFromDirectory(int uniqueRoomCount)
    {
        roomCols = ListResource<RoomCols>.LoadList(directory.RoomCols.ToString(), uniqueRoomCount).ToArray();
        colTables = TableResource<byte>.Load(directory.ColTables.ToString());
        tileAttrs = ListResource<byte>.LoadList(directory.TileAttrs.ToString(), tileTypeCount).ToArray();

        Graphics.LoadTileSheet(TileSheet.Background, directory.TilesImage.ToString());
    }

    private void LoadOverworldContext()
    {
        LoadOpenRoomContext();
        LoadMapResourcesFromDirectory(124);
        primaryMobs = ListResource<byte>.Load("owPrimaryMobs.list");
        secondaryMobs = ListResource<byte>.Load("owSecondaryMobs.list");
        tileBehaviors = ListResource<byte>.LoadList("owTileBehaviors.dat", TileTypes).ToArray();
    }

    private void LoadUnderworldContext()
    {
        LoadClosedRoomContext();
        LoadMapResourcesFromDirectory(64);
        primaryMobs = ListResource<byte>.Load("uwPrimaryMobs.list");
        tileBehaviors = ListResource<byte>.LoadList("uwTileBehaviors.dat", TileTypes).ToArray();
    }

    private void LoadCellarContext()
    {
        LoadOpenRoomContext();

        roomCols = ListResource<RoomCols>.LoadList("underworldCellarRoomCols.dat", 2).ToArray();
        colTables = TableResource<byte>.Load("underworldCellarCols.tab");

        tileAttrs = ListResource<byte>.LoadList("underworldCellarTileAttrs.dat", tileTypeCount).ToArray();

        Graphics.LoadTileSheet(TileSheet.Background, "underworldTiles.png");

        primaryMobs = ListResource<byte>.Load("uwCellarPrimaryMobs.list");
        secondaryMobs = ListResource<byte>.Load("uwCellarSecondaryMobs.list");
        tileBehaviors = ListResource<byte>.LoadList("uwTileBehaviors.dat", TileTypes).ToArray();
    }

    private void LoadLevel(int level)
    {
        var levelDirName = $"levelDir_{profile.Quest}_{level}.dat";

        directory = ListResource<LevelDirectory>.LoadSingle(levelDirName);
        infoBlock = ListResource<LevelInfoBlock>.LoadSingle(directory.LevelInfoBlock.ToString());

        wallsBmp?.Dispose();
        wallsBmp = null;
        doorsBmp?.Dispose();
        doorsBmp = null;

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
            curUWBlockFlags = null; // JOE: TODO: This seems wrong.
        }
        else
        {
            LoadUnderworldContext();
            wallsBmp = SKBitmap.Decode(directory.Extra2.FullPath());
            doorsBmp = SKBitmap.Decode(directory.Extra3.FullPath());
            curUWBlockFlags = level < 7 ? profile.LevelFlags1 : profile.LevelFlags2;

            for (var i = 0; i < tileMaps.Length; i++)
            {
                for (var x = 0; x < TileMap.Size; x++)
                {
                    tileMaps[i].Refs(x) = (byte)BlockObjType.Tile_WallEdge;
                }
            }
        }

        Graphics.LoadTileSheet(TileSheet.PlayerAndItems, directory.PlayerImage.ToString(), directory.PlayerSheet.ToString());
        Graphics.LoadTileSheet(TileSheet.Npcs, directory.NpcImage.ToString(), directory.NpcSheet.ToString());

        if (!directory.BossImage.IsNull)
        {
            Graphics.LoadTileSheet(TileSheet.Boss, directory.BossImage.ToString(), directory.BossSheet.ToString());
        }

        roomAttrs = ListResource<RoomAttrs>.LoadList(directory.RoomAttrs.ToString(), Rooms).ToArray();
        extraData = TableResource<byte>.Load(directory.LevelInfoEx.ToString());
        objLists = TableResource<byte>.Load(directory.ObjLists.ToString());
        sparseRoomAttrs = TableResource<byte>.Load(directory.Extra1.ToString());

        var facing = Game.Link?.Facing ?? Direction.Up;

        Game.Link = new Link(Game, facing);

        // Replace room attributes, if in second quest.

        if (level == 0 && profile.Quest == 1)
        {
            var pReplacement = sparseRoomAttrs.GetItems<byte>(Sparse.RoomReplacement);
            int replacementCount = pReplacement[0];
            var sparseAttr = MemoryMarshal.Cast<byte, SparseRoomAttr>(pReplacement[2..]); // JOE: Go until replacementCount * sizeof(SparseRoomAttr) ??

            for (var i = 0; i < replacementCount; i++)
            {
                int roomId = sparseAttr[i].roomId;
                roomAttrs[roomId] = sparseAttr[i].attrs;
            }
        }
    }

    private void Init()
    {
        var sysPal = ListResource<int>.LoadList("pal.dat", Global.SysPaletteLength).ToArray();
        Graphics.LoadSystemPalette(sysPal);

        Graphics.LoadTileSheet(TileSheet.Font, "font.png");
        Graphics.LoadTileSheet(TileSheet.PlayerAndItems, "playerItem.png", "playerItemsSheet.tab");

        textTable = TableResource<byte>.Load("text.tab");

        GotoFileMenu();
    }

    public void Start(int slot, PlayerProfile profile)
    {
        this.profile = profile;
        this.profile.Hearts = PlayerProfile.GetMaxHeartsValue(PlayerProfile.DefaultHearts);
        profileSlot = slot;

        GotoLoadLevel(0, true);
    }

    public void Update()
    {
        var mode = GetMode();

        if (lastMode != mode)
        {
            if (IsPlaying(lastMode) && mode != GameMode.WinGame)
            {
                CleanUpRoomItems();
                Graphics.DisableGrayscale();
                if (mode != GameMode.Unfurl)
                {
                    OnLeavePlay();
                    Game.Link?.Stop();
                }
            }

            lastMode = mode;

            gameMenu = nextGameMenu;
            nextGameMenu = null;
        }

        sModeFuncs[(int)curMode]();
    }

    public void Draw()
    {
        if (StatusBarVisible)
        {
            statusBar.Draw(SubmenuOffsetY);
        }

        sDrawFuncs[(int)curMode]();

        OnScreenDisplay.Draw();
    }

    private void DrawRoom()
    {
        DrawMap(curRoomId, curTileMapIndex, 0, 0);
    }

    public void PauseFillHearts()
    {
        Pause = 2;
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
        curRoomId = targetRoomId;
        TakeShortcut();
        LeaveCellar();
    }

    private void Die() => GotoDie();
    public void UnfurlLevel() => GotoUnfurl();
    public void ChooseFile(ProfileSummarySnapshot summaries) => GotoFileMenu(summaries);
    public void RegisterFile(ProfileSummarySnapshot summaries) => GotoRegisterMenu(summaries);
    public void EliminateFile(ProfileSummarySnapshot summaries) => GotoEliminateMenu(summaries);
    private bool IsPlaying() => IsPlaying(curMode);
    private static bool IsPlaying(GameMode mode) => mode is GameMode.Play or GameMode.PlayCave or GameMode.PlayCellar or GameMode.PlayShortcuts;
    private bool IsPlayingCave() => GetMode() == GameMode.PlayCave;

    public GameMode GetMode() =>
        curMode switch
        {
            GameMode.InitPlayCave => GameMode.PlayCave,
            GameMode.InitPlayCellar => GameMode.PlayCellar,
            _ => curMode
        };

    public Point GetObservedPlayerPos()
    {
        return fakePlayerPos;
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
        objectTimers[(int)ObjectSlot.FluteMusic] = 0x98;

        if (IsOverworld())
        {
            if (IsPlaying() && State.Play.roomType == PlayState.RoomType.Regular)
            {
                static ReadOnlySpan<byte> roomIds() => new byte[] { 0x42, 0x06, 0x29, 0x2B, 0x30, 0x3A, 0x3C, 0x58, 0x60, 0x6E, 0x72 };

                var makeWhirlwind = true;

                for (var i = 0; i < roomIds().Length; i++)
                {
                    if (roomIds()[i] == curRoomId)
                    {
                        if ((i == 0 && profile.Quest == 0)
                            || (i != 0 && profile.Quest != 0))
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
        else
        {
            recorderUsed = 1;
        }
    }

    private void SummonWhirlwind()
    {
        if (!summonedWhirlwind
            && WhirlwindTeleporting == 0
            && IsOverworld()
            && IsPlaying()
            && State.Play.roomType == PlayState.RoomType.Regular
            && GetItem(ItemSlot.TriforcePieces) != 0)
        {
            var slot = FindEmptyMonsterSlot();
            if (slot != ObjectSlot.NoneFound)
            {
                static ReadOnlySpan<byte> TeleportRoomIds() => new byte[] { 0x36, 0x3B, 0x73, 0x44, 0x0A, 0x21, 0x41, 0x6C };

                var whirlwind = new WhirlwindActor(Game, 0, Game.Link.Y);
                SetObject(slot, whirlwind);

                summonedWhirlwind = true;
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

        if (!State.Play.uncoveredRecorderSecret && FindSparseFlag(Sparse.Recorder, curRoomId))
        {
            State.Play.uncoveredRecorderSecret = true;
            State.Play.animatingRoomColors = true;
            State.Play.timer = 88;
        }
    }

    private TileBehavior GetTileBehavior(int row, int col)
    {
        return tileMaps[curTileMapIndex].AsBehaviors(row, col);
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

        if (fineCol is < 0 or >= Columns || fineRow is < 0 or >= Rows)
            return;

        SetMob(fineRow, fineCol, mobIndex);
    }

    private void SetMob(int row, int col, BlockObjType mobIndex)
    {
        loadMobFunc(ref tileMaps[curTileMapIndex], row, col, (byte)mobIndex); // JOE: FIXME: BlockObjTypes

        for (var r = row; r < row + 2; r++)
        {
            for (var c = col; c < col + 2; c++)
            {
                var t = tileMaps[curTileMapIndex].Refs(r, c);
                tileMaps[curTileMapIndex].Behaviors(r, c) = tileBehaviors[t];
            }
        }

        // TODO: Will we need to run some function to initialize the map object, like in LoadLayout?
    }

    public Palette GetInnerPalette()
    {
        return roomAttrs[curRoomId].GetInnerPalette();
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
        return new((byte)(cell.Row + BaseRows), cell.Col);
    }

    public T? GetObject<T>(ObjectSlot slot) where T : Actor => GetObject(slot) as T;

    public Actor? GetObject(ObjectSlot slot)
    {
        if (slot == ObjectSlot.Player)
            return Game.Link;

        return objects[(int)slot];
    }

    public IEnumerable<T> GetMonsters<T>(bool skipStart = false) where T : Actor
    {
        var start = skipStart ? ObjectSlot.Monster1 + 1 : ObjectSlot.Monster1;
        var end = skipStart ? ObjectSlot.Monster1 + 9 : ObjectSlot.LastMonster;
        for (var slot = start; slot < end; slot++)
        {
            var obj = Game.World.GetObject(slot);
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

    public void SetObject(ObjectSlot slot, Actor obj)
    {
        SetOnlyObject(slot, obj);
    }

    public ObjectSlot FindEmptyFireSlot()
    {
        for (var i = ObjectSlot.FirstFire; i < ObjectSlot.LastFire; i++)
        {
            if (objects[(int)i] == null)
                return i;
        }
        return ObjectSlot.NoneFound;
    }

    public ref int GetObjectTimer(ObjectSlot slot) => ref objectTimers[(int)slot];
    public void SetObjectTimer(ObjectSlot slot, int value) => objectTimers[(int)slot] = value;
    public int GetStunTimer(ObjectSlot slot) => stunTimers[(int)slot];
    public void SetStunTimer(ObjectSlot slot, int value) => stunTimers[(int)slot] = value;
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
            && infoBlock.LevelNumber == 0
            && curRoomId == 0x1F
            && dir.IsVertical()
            && x == 0x80
            && y < 0x56)
        {
            collision.Collides = false;
        }

        if (Game.Cheats.WalkThroughWalls && isPlayer)
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

            if (curBehavior == TileBehavior.Water && State.Play.allowWalkOnWater)
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

                if (curBehavior == TileBehavior.Water && State.Play.allowWalkOnWater)
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
            if (!GotShortcut(curRoomId))
            {
                if (FindSparseFlag(Sparse.Shortcut, curRoomId))
                {
                    TakeShortcut();
                    ShowShortcutStairs(curRoomId, curTileMapIndex);
                }
            }
        }
        else
        {
            var uwRoomAttrs = CurrentUWRoomAttrs;
            var secret = uwRoomAttrs.GetSecret();

            if (secret == Secret.BlockDoor)
            {
                triggerShutters = true;
            }
            else if (secret == Secret.BlockStairs)
            {
                AddUWRoomStairs();
            }
        }
    }

    public void OnActivatedArmos(int x, int y)
    {
        var pos = FindSparsePos2(Sparse.ArmosStairs, curRoomId);

        if (pos != null && x == pos.Value.x && y == pos.Value.y)
        {
            SetMobXY(x, y, BlockObjType.Mob_Stairs);
            Game.Sound.PlayEffect(SoundEffect.Secret);
        }
        else
        {
            SetMobXY(x, y, BlockObjType.Mob_Ground);
        }

        if (!GotItem())
        {
            var roomItem = FindSparseItem(Sparse.ArmosItem, curRoomId);

            if (roomItem != null && x == roomItem.Value.x && y == roomItem.Value.y)
            {
                var itemObj = Actor.FromType((ObjType)roomItem.Value.itemId, Game, roomItem.Value.x, roomItem.Value.y);
                objects[(int)ObjectSlot.Item] = itemObj;
            }
        }
    }

    private static ReadOnlySpan<byte> _onTouchedPowerTriforcePalette => new byte[] { 0, 0x0F, 0x10, 0x30 };

    public void OnTouchedPowerTriforce()
    {
        powerTriforceFanfare = true;
        Game.Link.SetState(PlayerState.Paused);
        Game.Link.ObjTimer = 0xC0;

        Graphics.SetPaletteIndexed(Palette.LevelFgPalette, _onTouchedPowerTriforcePalette);
        Graphics.UpdatePalettes();
    }

    private void CheckPowerTriforceFanfare()
    {
        if (!powerTriforceFanfare)
            return;

        if (Game.Link.ObjTimer == 0)
        {
            powerTriforceFanfare = false;
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
        if (profile.SelectedItem == 0)
            profile.SelectedItem = ItemSlot.Boomerang;

        for (var i = 0; i < 10; i++)
        {
            if (profile.SelectedItem is ItemSlot.Arrow or ItemSlot.Bow)
            {
                if (profile.Items[ItemSlot.Arrow] != 0
                    && profile.Items[ItemSlot.Bow] != 0)
                {
                    break;
                }
            }
            else
            {
                if (profile.Items[profile.SelectedItem] != 0)
                {
                    break;
                }
            }

            switch ((int)profile.SelectedItem)
            {
                case 0x07: profile.SelectedItem = (ItemSlot)0x0F; break;
                case 0x0F: profile.SelectedItem = (ItemSlot)0x06; break;
                case 0x01: profile.SelectedItem = (ItemSlot)0x1B; break;
                case 0x1B: profile.SelectedItem = (ItemSlot)0x08; break;
                default: profile.SelectedItem--; break;
            }
        }
    }

    private void WarnLowHPIfNeeded()
    {
        if (profile.Hearts >= 0x100)
            return;

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
            if (curUWBlockFlags[infoBlock.BossRoomId].GetObjCount() == 0)
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

    public void ShowShortcutStairs(int roomId, int _tileMapIndex) // JOE: TODO: Is _tileMapIndex not being used a mistake?
    {
        OWRoomAttrs owRoomAttrs = roomAttrs[roomId];
        var index = owRoomAttrs.GetShortcutStairsIndex();
        var pos = infoBlock.ShortcutPosition[index];
        GetRoomCoord(pos, out var row, out var col);
        SetMob(row * 2, col * 2, BlockObjType.Mob_Stairs);
    }

    private void DrawMap(int roomId, int mapIndex, int offsetX, int offsetY)
    {
        Graphics.Begin();

        var outerPalette = roomAttrs[roomId].GetOuterPalette();
        var innerPalette = roomAttrs[roomId].GetInnerPalette();
        var map = tileMaps[mapIndex];

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

        var endCol = startCol + colCount;
        var endRow = startRow + rowCount;

        var y = TileMapBaseY + tileOffsetY;

        if (IsUWMain(roomId))
        {
            Graphics.DrawBitmap(
                wallsBmp,
                0, 0,
                TileMapWidth, TileMapHeight,
                offsetX, TileMapBaseY + offsetY,
                outerPalette, 0);
        }

        for (var r = firstRow; r < lastRow; r++, y += TileHeight)
        {
            if (r < startRow || r >= endRow)
                continue;

            var x = tileOffsetX;

            for (var c = firstCol; c < lastCol; c++, x += TileWidth)
            {
                if (c < startCol || c >= endCol)
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
        var outerPalette = roomAttrs[roomId].GetOuterPalette();
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
                    doorState = true;
            }
            if (doorType == DoorType.Shutter && TempShutters && TempShutterRoomId == roomId)
            {
                doorState = true;
            }
            var doorFace = GetDoorStateFace(doorType, doorState);
            Graphics.DrawBitmap(
                doorsBmp,
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

    public PlayerProfile GetProfile()
    {
        return profile;
    }

    public int GetItem(ItemSlot itemSlot)
    {
        return profile.Items[itemSlot];
    }

    public void SetItem(ItemSlot itemSlot, int value)
    {
        profile.Items[itemSlot] = value;
    }

    private void PostRupeeChange(byte value, ItemSlot itemSlot)
    {
        var curValue = profile.Items[itemSlot];
        var newValue = curValue + value;

        if (newValue < curValue)
            newValue = 255;

        profile.Items[itemSlot] = newValue;
    }

    public void PostRupeeWin(byte value) => PostRupeeChange(value, ItemSlot.RupeesToAdd);
    public void PostRupeeLoss(byte value) => PostRupeeChange(value, ItemSlot.RupeesToSubtract);

    public void FillHearts(int heartValue)
    {
        var maxHeartValue = profile.Items[ItemSlot.HeartContainers] << 8;

        profile.Hearts += heartValue;

        if (profile.Hearts >= maxHeartValue)
            profile.Hearts = maxHeartValue - 1;
    }

    public void AddItem(ItemId itemId)
    {
        if ((int)itemId >= (int)ItemId.None)
            return;

        GlobalFunctions.PlayItemSound(Game, itemId);

        var equip = sItemToEquipment[(int)itemId];
        var slot = equip.Slot;
        var value = equip.Value;

        if (itemId is ItemId.Heart or ItemId.Fairy)
        {
            var heartValue = value << 8;
            FillHearts(heartValue);
        }
        else
        {
            if (slot == ItemSlot.Bombs)
            {
                value += (byte)profile.Items[ItemSlot.Bombs];
                if (value > profile.Items[ItemSlot.MaxBombs])
                    value = (byte)profile.Items[ItemSlot.MaxBombs];
            }
            else if (slot is ItemSlot.RupeesToAdd or ItemSlot.Keys or ItemSlot.HeartContainers)
            {
                value += (byte)profile.Items[slot];
                if (value > 255)
                    value = 255;
            }
            else if (itemId == ItemId.Compass)
            {
                if (infoBlock.LevelNumber < 9)
                {
                    var bit = 1 << (infoBlock.LevelNumber - 1);
                    value = (byte)(profile.Items[ItemSlot.Compass] | bit);
                    slot = ItemSlot.Compass;
                }
            }
            else if (itemId == ItemId.Map)
            {
                if (infoBlock.LevelNumber < 9)
                {
                    var bit = 1 << (infoBlock.LevelNumber - 1);
                    value = (byte)(profile.Items[ItemSlot.Map] | bit);
                    slot = ItemSlot.Map;
                }
            }
            else if (itemId == ItemId.TriforcePiece)
            {
                var bit = 1 << (infoBlock.LevelNumber - 1);
                value = (byte)(profile.Items[ItemSlot.TriforcePieces] | bit);
            }

            profile.Items[slot] = value;

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
    }

    public void DecrementItem(ItemSlot itemSlot)
    {
        var val = GetItem(itemSlot);
        if (val != 0)
            profile.Items[itemSlot] = val - 1;
    }

    public bool HasCurrentMap() => HasCurrentLevelItem(ItemSlot.Map, ItemSlot.Map9);
    public bool HasCurrentCompass() => HasCurrentLevelItem(ItemSlot.Compass, ItemSlot.Compass9);

    private bool HasCurrentLevelItem(ItemSlot itemSlot1To8, ItemSlot itemSlot9)
    {
        if (infoBlock.LevelNumber == 0)
            return false;

        if (infoBlock.LevelNumber < 9)
        {
            var itemValue = profile.Items[itemSlot1To8];
            var bit = 1 << (infoBlock.LevelNumber - 1);
            return (itemValue & bit) != 0;
        }

        return profile.Items[itemSlot9] != 0;
    }

    private DoorType GetDoorType(Direction dir)
    {
        return GetDoorType(curRoomId, dir);
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

    private bool GetEffectiveDoorState(Direction doorDir) => GetEffectiveDoorState(curRoomId, doorDir);
    public UWRoomFlags GetUWRoomFlags(int roomId) => curUWBlockFlags[roomId];
    public LevelInfoBlock GetLevelInfo() => infoBlock;
    public bool IsOverworld() => infoBlock.LevelNumber == 0;
    public bool DoesRoomSupportLadder() => FindSparseFlag(Sparse.Ladder, curRoomId);
    private TileAction GetTileAction(int tileRef) => TileAttr.GetAction(tileAttrs[tileRef]);
    public bool IsUWMain(int roomId) => !IsOverworld() && (roomAttrs[roomId].GetUniqueRoomId() < 0x3E);
    public bool IsUWMain() => IsUWMain(curRoomId);
    private bool IsUWCellar(int roomId) => !IsOverworld() && (roomAttrs[roomId].GetUniqueRoomId() >= 0x3E);
    public bool IsUWCellar() => IsUWCellar(curRoomId);
    private bool GotShortcut(int roomId) => profile.OverworldFlags[roomId].GetShortcutState();
    private bool GotSecret() => profile.OverworldFlags[curRoomId].GetSecretState();

    public ReadOnlySpan<byte> GetShortcutRooms()
    {
        var valueArray = sparseRoomAttrs.GetItems<byte>(Sparse.Shortcut);
        // elemSize is at 1, but we don't need it.
        return valueArray[2..valueArray[0]];
    }

    private void TakeShortcut() => profile.OverworldFlags[curRoomId].SetShortcutState();
    public void TakeSecret() => profile.OverworldFlags[curRoomId].SetSecretState();
    public bool GotItem() => GotItem(curRoomId);

    public bool GotItem(int roomId)
    {
        return IsOverworld() ? profile.OverworldFlags[roomId].GetItemState() : curUWBlockFlags[roomId].GetItemState();
    }

    public void MarkItem()
    {
        if (IsOverworld())
        {
            profile.OverworldFlags[curRoomId].SetItemState();
        }
        else
        {
            curUWBlockFlags[curRoomId].SetItemState();
        }
    }

    public void LiftItem(ItemId itemId, short timer = 0x80)
    {
        if (!IsPlaying())
            return;

        if (itemId is ItemId.None or 0)
        {
            State.Play.liftItemTimer = 0;
            State.Play.liftItemId = 0;
            return;
        }

        State.Play.liftItemTimer = Game.Cheats.SpeedUp ? (byte)1 : timer;
        State.Play.liftItemId = itemId;

        Game.Link.SetState(PlayerState.Paused);
    }

    public bool IsLiftingItem()
    {
        if (!IsPlaying())
            return false;

        return State.Play.liftItemId != 0;
    }

    public void OpenShutters()
    {
        TempShutters = true;
        TempShutterRoomId = curRoomId;
        Game.Sound.PlayEffect(SoundEffect.Door);

        for (var i = 0; i < Doors; i++)
        {
            var dir = i.GetOrdDirection();
            var type = GetDoorType(dir);

            if (type == DoorType.Shutter)
                UpdateDoorTileBehavior(i);
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
                if (allowBombDrop)
                    HelpDropValue++;
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
        fakePlayerPos.X = x;
        fakePlayerPos.Y = y;
    }

    public void SetPersonWallY(int y)
    {
        State.Play.personWallY = y;
    }

    public int GetFadeStep()
    {
        return DarkRoomFadeStep;
    }

    public void BeginFadeIn()
    {
        if (DarkRoomFadeStep > 0)
            brightenRoom = true;
    }

    public void FadeIn()
    {
        if (DarkRoomFadeStep == 0)
        {
            brightenRoom = false;
            return;
        }

        var timer = GetObjectTimer(ObjectSlot.FadeTimer);

        if (timer == 0)
        {
            DarkRoomFadeStep--;
            timer = 10; // JOE: TODO: Does this reference still work?

            for (var i = 0; i < 2; i++)
            {
                Graphics.SetPaletteIndexed((Palette)(i + 2), infoBlock.DarkPalette(DarkRoomFadeStep, i));
            }
            Graphics.UpdatePalettes();
        }
    }

    // JOE: TODO: Research why this is unused.
    private bool UseKey()
    {
        if (GetItem(ItemSlot.MagicKey) != 0)
            return true;

        var keyCount = GetItem(ItemSlot.Keys);

        if (keyCount > 0)
        {
            keyCount--;
            SetItem(ItemSlot.Keys, keyCount);
            return true;
        }

        return false;
    }

    private bool GetDoorState(int roomId, Direction door)
    {
        return curUWBlockFlags[roomId].GetDoorState(door);
    }

    private void SetDoorState(int roomId, Direction door)
    {
        curUWBlockFlags[roomId].SetDoorState(door);
    }

    private bool IsRoomInHistory()
    {
        for (var i = 0; i < RoomHistoryLength; i++)
        {
            if (RoomHistory[i] == curRoomId)
                return true;
        }
        return false;
    }

    private void AddRoomToHistory()
    {
        var i = 0;

        for (; i < RoomHistoryLength; i++)
        {
            if (RoomHistory[i] == curRoomId)
                break;
        }

        if (i == RoomHistoryLength)
        {
            RoomHistory[NextRoomHistorySlot] = (byte)curRoomId;
            NextRoomHistorySlot++;
            if (NextRoomHistorySlot >= RoomHistoryLength)
                NextRoomHistorySlot = 0;
        }
    }

    private bool FindSparseFlag(Sparse attrId, int roomId)
    {
        return sparseRoomAttrs.FindSparseAttr<SparsePos>(attrId, roomId).HasValue;
    }

    private SparsePos? FindSparsePos(Sparse attrId, int roomId)
    {
        return sparseRoomAttrs.FindSparseAttr<SparsePos>(attrId, roomId);
    }

    private SparsePos2? FindSparsePos2(Sparse attrId, int roomId)
    {
        return sparseRoomAttrs.FindSparseAttr<SparsePos2>(attrId, roomId);
    }

    private SparseRoomItem? FindSparseItem(Sparse attrId, int roomId)
    {
        return sparseRoomAttrs.FindSparseAttr<SparseRoomItem>(attrId, roomId);
    }

    private ReadOnlySpan<ObjectAttr> GetObjectAttrs()
    {
        return extraData.GetItems<ObjectAttr>(Extra.ObjAttrs);
    }

    public ObjectAttr GetObjectAttrs(ObjType type)
    {
        return GetObjectAttrs()[(int)type];
    }

    public int GetObjectMaxHP(ObjType type)
    {
        var hpAttrs = extraData.GetItems<HPAttr>(Extra.HitPoints);
        var index = (int)type / 2;
        return hpAttrs[index].GetHP((int)type);
    }

    public int GetPlayerDamage(ObjType type)
    {
        var damageAttrs = extraData.GetItems<byte>(Extra.PlayerDamage);
        var damageByte = damageAttrs[(int)type];
        return ((damageByte & 0xF) << 8) | (damageByte & 0xF0);
    }

    public void LoadOverworldRoom(int x, int y) => LoadRoom(x + y * 16, curTileMapIndex);

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

        curRoomId = roomId;
        curTileMapIndex = tileMapIndex;

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
                    objects[(int)ObjectSlot.Item] = itemObj;
                }
            }
        }
        else
        {
            if (!GotItem())
            {
                var uwRoomAttrs = GetUWRoomAttrs(roomId);

                if (uwRoomAttrs.GetSecret() != Secret.FoesItem
                    && uwRoomAttrs.GetSecret() != Secret.LastBoss)
                {
                    AddUWRoomItem(roomId);
                }
            }
        }
    }

    public void AddUWRoomItem()
    {
        AddUWRoomItem(curRoomId);
    }

    private void AddUWRoomItem(int roomId)
    {
        var uwRoomAttrs = GetUWRoomAttrs(roomId);
        var itemId = uwRoomAttrs.GetItemId();

        if (itemId != ItemId.None)
        {
            var posIndex = uwRoomAttrs.GetItemPositionIndex();
            var pos = GetRoomItemPosition(infoBlock.ShortcutPosition[posIndex]);

            if (itemId == ItemId.TriforcePiece)
                pos.X = TriforcePieceX;

            // Arg
            var itemObj = new ItemObjActor(Game, itemId, true, pos.X, pos.Y);
            objects[(int)ObjectSlot.Item] = itemObj;

            if (uwRoomAttrs.GetSecret() == Secret.FoesItem
                || uwRoomAttrs.GetSecret() == Secret.LastBoss)
            {
                Game.Sound.PlayEffect(SoundEffect.RoomItem);
            }
        }
    }

    private void LoadCaveRoom(Cave uniqueRoomId)
    {
        curTileMapIndex = 0;

        LoadLayout((int)uniqueRoomId, 0, TileScheme.Overworld);
    }

    private void LoadMap(int roomId, int tileMapIndex)
    {
        TileScheme tileScheme;
        var uniqueRoomId = roomAttrs[roomId].GetUniqueRoomId();

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
        var primary = primaryMobs[mobIndex];

        if (primary == 0xFF)
        {
            var index = mobIndex * 4;
            var secondaries = secondaryMobs;
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
        var primary = primaryMobs[mobIndex];

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
        var maxColumnStartOffset = (colCount / 2 - 1) * rowCount / 2;

        var columns = roomCols[uniqueRoomId];
        var map = tileMaps[tileMapIndex];
        var rowEnd = startRow + rowCount;

        var owLayoutFormat = tileScheme is TileScheme.Overworld or TileScheme.UnderworldCellar;

        loadMobFunc = tileScheme switch
        {
            TileScheme.Overworld => LoadOWMob,
            TileScheme.UnderworldMain => LoadUWMob,
            TileScheme.UnderworldCellar => LoadOWMob,
            _ => loadMobFunc
        };

        for (var i = 0; i < colCount / 2; i++)
        {
            var columnDesc = columns.ColumnDesc[i];
            var tableIndex = (byte)((columnDesc & 0xF0) >> 4);
            var columnIndex = (byte)(columnDesc & 0x0F);

            var table = colTables.GetItem(tableIndex);
            var k = 0;
            var j = 0;

            for (j = 0; j <= maxColumnStartOffset; j++)
            {
                var t = table[j];

                if ((t & 0x80) != 0)
                {
                    if (k == columnIndex)
                        break;
                    k++;
                }
            }

            if (j > maxColumnStartOffset) throw new Exception();

            var c = startCol + i * 2;

            for (var r = startRow; r < rowEnd; j++)
            {
                var t = table[j];
                var tileRef = owLayoutFormat ? (byte)(t & 0x3F) : (byte)(t & 0x7);

                loadMobFunc(ref map, r, c, tileRef);

                var attr = tileAttrs[tileRef];
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
                        loadMobFunc(ref map, r, c, tileRef);
                        actionFunc?.Invoke(r, c, TileInteraction.Load);
                        r += 2;
                    }
                }
                else
                {
                    var repeat = (t >> 4) & 0x7;
                    for (var m = 0; m < repeat && r < rowEnd; m++)
                    {
                        loadMobFunc(ref map, r, c, tileRef);
                        actionFunc?.Invoke(r, c, TileInteraction.Load);
                        r += 2;
                    }
                }
            }
        }

        if (IsUWMain(curRoomId))
        {
            var uwRoomAttrs = CurrentUWRoomAttrs;
            if (uwRoomAttrs.HasBlock())
            {
                for (var c = startCol; c < startCol + colCount; c += 2)
                {
                    var tileRef = tileMaps[curTileMapIndex].Refs(UWBlockRow * c);
                    if (tileRef == (byte)BlockObjType.Tile_Block)
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
            map.Behaviors(i) = tileBehaviors[t];
        }

        PatchTileBehaviors();
    }

    private void PatchTileBehaviors()
    {
        PatchTileBehavior(ghostCount, ghostCells, TileBehavior.Ghost0);
        PatchTileBehavior(armosCount, armosCells, TileBehavior.Armos0);
    }

    private void PatchTileBehavior(int count, Cell[] cells, TileBehavior baseBehavior)
    {
        for (var i = 0; i < count; i++)
        {
            var row = cells[i].Row;
            var col = cells[i].Col;
            var behavior = (byte)((int)baseBehavior + 15 - i);
            tileMaps[curTileMapIndex].Behaviors(row, col) = behavior;
            tileMaps[curTileMapIndex].Behaviors(row, col + 1) = behavior;
            tileMaps[curTileMapIndex].Behaviors(row + 1, col) = behavior;
            tileMaps[curTileMapIndex].Behaviors(row + 1, col + 1) = behavior;
        }
    }

    private void UpdateDoorTileBehavior(int doorOrd)
    {
        UpdateDoorTileBehavior(curRoomId, curTileMapIndex, doorOrd);
    }

    private void UpdateDoorTileBehavior(int roomId, int tileMapIndex, int doorOrd)
    {
        var map = tileMaps[tileMapIndex];
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

    private void GotoPlay(PlayState.RoomType roomType = PlayState.RoomType.Regular)
    {
        switch (roomType)
        {
            case PlayState.RoomType.Regular: curMode = GameMode.Play; break;
            case PlayState.RoomType.Cave: curMode = GameMode.PlayCave; break;
            case PlayState.RoomType.Cellar: curMode = GameMode.PlayCellar; break;
            default:
                throw new Exception();
                curMode = GameMode.Play;
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
        ghostCount = 0;
        armosCount = 0;

        State.Play.substate = PlayState.Substates.Active;
        State.Play.animatingRoomColors = false;
        State.Play.allowWalkOnWater = false;
        State.Play.uncoveredRecorderSecret = false;
        State.Play.roomType = roomType;
        State.Play.liftItemTimer = 0;
        State.Play.liftItemId = 0;
        State.Play.personWallY = 0;

        if (FindSparseFlag(Sparse.Dock, curRoomId))
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
            curUWBlockFlags[curRoomId].SetVisitState();
    }

    private void UpdatePlay()
    {
        if (State.Play.substate == PlayState.Substates.Active)
        {
            if (brightenRoom)
            {
                FadeIn();
                DecrementObjectTimers();
                DecrementStunTimers();
                return;
            }

            if (Submenu != 0)
            {
                if (Game.Input.IsButtonPressing(Button.Select))
                {
                    Submenu = 0;
                    SubmenuOffsetY = 0;
                    GotoContinueQuestion();
                }
                else
                {
                    UpdateSubmenu();
                }
                return;
            }

            if (Pause == 0)
            {
                if (Game.Input.IsButtonPressing(Button.Select))
                {
                    if (Game.Enhancements)
                    {
                        menu.SelectNextItem();
                    }
                    else
                    {
                        Pause = 1;
                        Game.Sound.Pause();
                    }
                    return;
                }
                else if (Game.Input.IsButtonPressing(Button.Start))
                {
                    Submenu = 1;
                    return;
                }
            }
            else if (Pause == 1)
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

            if (objectTimers[(int)ObjectSlot.FluteMusic] != 0)
                return;

            if (Pause == 2)
            {
                FillHeartsStep();
                return;
            }

            if (State.Play.animatingRoomColors)
                UpdateRoomColors();

            if (IsUWMain(curRoomId))
                CheckBombables();

            UpdateRupees();
            UpdateLiftItem();

            curObjSlot = (int)ObjectSlot.Player;
            Game.Link.DecInvincibleTimer();
            Game.Link.Update();

            // The player's update might have changed the world's State.
            if (!IsPlaying())
                return;

            UpdateObservedPlayerPos();

            for (curObjSlot = (int)ObjectSlot.MaxObjects - 1; curObjSlot >= 0; curObjSlot--)
            {
                var obj = objects[curObjSlot];
                if (obj != null && !obj.IsDeleted)
                {
                    if (obj.DecoratedUpdate())
                        HandleNormalObjectDeath();
                }
                else if (placeholderTypes[curObjSlot] != 0)
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
    }

    private void UpdateSubmenu()
    {
        if (Submenu == 1)
        {
            menu.Enable();
            Submenu++;
            statusBar.EnableFeatures(StatusBarFeatures.Equipment, false);
        }
        else if (Submenu == 7)
        {
            SubmenuOffsetY += 3;
            if (SubmenuOffsetY >= SubmenuType.Height)
            {
                menu.Activate();
                Submenu++;
            }
        }
        else if (Submenu == 8)
        {
            if (Game.Input.IsButtonPressing(Button.Start))
            {
                menu.Deactivate();
                Submenu++;
            }
        }
        else if (Submenu == 9)
        {
            SubmenuOffsetY -= 3;
            if (SubmenuOffsetY == 0)
            {
                menu.Disable();
                Submenu = 0;
                statusBar.EnableFeatures(StatusBarFeatures.Equipment, true);
            }
        }
        else
        {
            Submenu++;
        }

        if (Submenu != 0)
            menu.Update();
    }

    private void CheckShutters()
    {
        if (triggerShutters)
        {
            triggerShutters = false;

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

            if (dirs != 0 && triggeredDoorCmd == 0)
            {
                triggeredDoorCmd = 6;
                triggeredDoorDir |= (Direction)0x10;
            }
        }
    }

    private void UpdateDoors2()
    {
        if (GetMode() == GameMode.EndLevel
            || objectTimers[(int)ObjectSlot.Door] != 0
            || triggeredDoorCmd == 0)
        {
            return;
        }

        if ((triggeredDoorCmd & 1) == 0)
        {
            triggeredDoorCmd++;
            objectTimers[(int)ObjectSlot.Door] = 8;
        }
        else
        {
            if ((triggeredDoorDir & (Direction)0x10) != 0)
            {
                OpenShutters();
            }

            var d = 1;

            for (var i = 0; i < 4; i++, d <<= 1)
            {
                if ((triggeredDoorDir & (Direction)d) == 0)
                    continue;

                var dir = (Direction)d;
                var type = GetDoorType(dir);

                if (type is DoorType.Bombable or DoorType.Key or DoorType.Key2)
                {
                    if (!GetDoorState(curRoomId, dir))
                    {
                        var oppositeDir = dir.GetOppositeDirection();
                        var nextRoomId = GetNextRoomId(curRoomId, dir);

                        SetDoorState(curRoomId, dir);
                        SetDoorState(nextRoomId, oppositeDir);
                        if (type != DoorType.Bombable)
                            Game.Sound.PlayEffect(SoundEffect.Door);
                        UpdateDoorTileBehavior(i);
                    }
                }
            }

            triggeredDoorCmd = 0;
            triggeredDoorDir = Direction.None;
        }
    }

    private byte GetNextTeleportingRoomIndex()
    {
        var facing = Game.Link.Facing;
        var growing = facing is Direction.Up or Direction.Right;

        var pieces = GetItem(ItemSlot.TriforcePieces);
        var index = TeleportingRoomIndex;
        var mask = 1 << TeleportingRoomIndex;

        if (pieces == 0)
            return 0;

        do
        {
            if (growing)
            {
                index = (byte)((index + 1) & 7);
                mask <<= 1;
                if (mask >= 0x100)
                    mask = 1;
            }
            else
            {
                index = (byte)((index - 1) & 7);
                mask >>= 1;
                if (mask == 0)
                    mask = 0x80;
            }
        } while ((pieces & mask) == 0);

        return index;
    }

    private void UpdateRoomColors()
    {
        if (State.Play.timer == 0)
        {
            State.Play.animatingRoomColors = false;
            var posAttr = FindSparsePos(Sparse.Recorder, curRoomId);
            if (posAttr != null)
            {
                GetRoomCoord(posAttr.Value.pos, out var row, out var col);
                SetMob(row * 2, col * 2, BlockObjType.Mob_Stairs);
                Game.Sound.PlayEffect(SoundEffect.Secret);
            }
            return;
        }

        if ((State.Play.timer % 8) == 0)
        {
            var colorSeq = extraData.ReadLengthPrefixedItem((int)Extra.PondColors);
            if (CurColorSeqNum < colorSeq.Length - 1)
            {
                if (CurColorSeqNum == colorSeq.Length - 2)
                {
                    State.Play.allowWalkOnWater = true;
                }

                int colorIndex = colorSeq[CurColorSeqNum];
                CurColorSeqNum++;
                Graphics.SetColorIndexed((Palette)3, 3, colorIndex);
                Graphics.UpdatePalettes();
            }
        }

        State.Play.timer--;
    }

    private void CheckBombables()
    {
        var uwRoomAttrs = CurrentUWRoomAttrs;

        for (var iBomb = (int)ObjectSlot.FirstBomb; iBomb < (int)ObjectSlot.LastBomb; iBomb++)
        {
            var bomb = objects[iBomb] as BombActor;
            if (bomb == null || bomb.BombState != BombState.Fading) continue;

            var bombX = bomb.X + 8;
            var bombY = bomb.Y + 8;

            for (var iDoor = 0; iDoor < 4; iDoor++)
            {
                var doorType = uwRoomAttrs.GetDoor(iDoor);
                if (doorType == DoorType.Bombable)
                {
                    var doorDir = iDoor.GetOrdDirection();
                    var doorState = GetDoorState(curRoomId, doorDir);
                    if (!doorState)
                    {
                        if (Math.Abs(bombX - doorMiddles[iDoor].X) < UWBombRadius
                            && Math.Abs(bombY - doorMiddles[iDoor].Y) < UWBombRadius)
                        {
                            triggeredDoorCmd = 6;
                            triggeredDoorDir = doorDir;
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
        if (IsOverworld())
            return;

        if (!RoomAllDead)
        {
            if (!CalcHasLivingObjects())
            {
                Game.Link.Paralyzed = false;
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
                    triggerShutters = true;
                break;

            case Secret.FoesItem:
                // ORIGINAL: BlockDoor and BlockStairs are handled here.
                if (RoomAllDead)
                {
                    if (!MadeRoomItem && !GotItem())
                    {
                        MadeRoomItem = true;
                        AddUWRoomItem(curRoomId);
                    }
                }
                // fall thru
                goto case Secret.FoesDoor;
            case Secret.FoesDoor:
                if (RoomAllDead)
                    triggerShutters = true;
                break;
        }
    }

    private void AddUWRoomStairs()
    {
        SetMobXY(0xD0, 0x60, BlockObjType.Mob_UW_Stairs);
    }

    public void KillAllObjects()
    {
        for (var i = (int)ObjectSlot.Monster1; i < (int)ObjectSlot.MonsterEnd; i++)
        {
            var obj = objects[i];
            if (obj != null
                && obj.ObjType < ObjType.PersonEnd
                && obj.Decoration == 0)
            {
                obj.Decoration = 0x10;
            }
        }
    }

    private void MoveRoomItem()
    {
        var foe = GetObject(ObjectSlot.Monster1);
        if (foe == null)
            return;

        var item = GetObject(ObjectSlot.Item);


        if (item != null && foe.CanHoldRoomItem)
        {
            item.X = foe.X;
            item.Y = foe.Y;
        }
    }

    private static ReadOnlySpan<int> _fireballLayouts => new[] { 0x24, 0x23 };

    private void UpdateStatues()
    {
        if (IsOverworld())
            return;

        var pattern = -1;

        if (EnablePersonFireballs)
        {
            pattern = 2;
        }
        else
        {
            var uwRoomAttrs = CurrentUWRoomAttrs;
            var layoutId = uwRoomAttrs.GetUniqueRoomId();

            for (var i = 0; i < _fireballLayouts.Length; i++)
            {
                if (_fireballLayouts[i] == layoutId)
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
        if (lastMode == GameMode.Play)
            SaveObjectCount();
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
            var flags = profile.OverworldFlags[curRoomId];
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
                    count = 7;
            }

            flags.SetObjCount(count);
        }
        else
        {
            var flags = curUWBlockFlags[curRoomId];
            int count;

            if (RoomObjCount != 0)
            {
                if (RoomKillCount == 0 || (RoomObj != null && RoomObj.IsReoccuring))
                {
                    if (RoomKillCount < RoomObjCount)
                    {
                        LevelKillCounts[curRoomId] += RoomKillCount;
                        if (LevelKillCounts[curRoomId] < 3)
                            count = LevelKillCounts[curRoomId];
                        else
                            count = 2;
                        flags.SetObjCount((byte)count);
                        return;
                    }
                }
            }

            LevelKillCounts[curRoomId] = 0xF;
            flags.SetObjCount(3);
        }
    }

    private void CalcObjCountToMake(ref ObjType type, ref int count)
    {
        if (IsOverworld())
        {
            var flags = profile.OverworldFlags[curRoomId];

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
            var flags = curUWBlockFlags[curRoomId];

            if (IsRoomInHistory() || flags.GetObjCount() != 3)
            {
                if (count < LevelKillCounts[curRoomId])
                {
                    type = ObjType.None;
                    count = 0;
                }
                else
                {
                    count -= LevelKillCounts[curRoomId];
                }
            }
            else
            {
                if (IsRecurringFoe(type))
                {
                    flags.SetObjCount(0);
                    LevelKillCounts[curRoomId] = 0;
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

        if (!giveFakePlayerPos)
        {
            fakePlayerPos.X = Game.Link.X;
            fakePlayerPos.Y = Game.Link.Y;
        }

        // ORIGINAL: This happens after player items update and before the rest of objects update.

        if (stunTimers[(int)ObjectSlot.ObservedPlayerTimer] == 0)
        {
            stunTimers[(int)ObjectSlot.ObservedPlayerTimer] = Random.Shared.Next(0, 8);

            giveFakePlayerPos = !giveFakePlayerPos;
            if (giveFakePlayerPos)
            {
                if (fakePlayerPos.X == Game.Link.X)
                {
                    fakePlayerPos.X ^= 0xFF;
                    fakePlayerPos.Y ^= 0xFF;
                }
            }
        }
    }

    private void UpdateRupees()
    {
        if ((Game.GetFrameCounter() & 1) == 0)
        {
            var rupeesToAdd = profile.Items[ItemSlot.RupeesToAdd];
            var rupeesToSubtract = profile.Items[ItemSlot.RupeesToSubtract];

            if (rupeesToAdd > 0 && rupeesToSubtract == 0)
            {
                if (profile.Items[ItemSlot.Rupees] < 255)
                    profile.Items[ItemSlot.Rupees]++;
                else
                    profile.Items[ItemSlot.RupeesToAdd] = 0;

                Game.Sound.PlayEffect(SoundEffect.Character);
            }
            else if (rupeesToAdd == 0 && rupeesToSubtract > 0)
            {
                if (profile.Items[ItemSlot.Rupees] > 0)
                    profile.Items[ItemSlot.Rupees]--;
                else
                    profile.Items[ItemSlot.RupeesToSubtract] = 0;

                Game.Sound.PlayEffect(SoundEffect.Character);
            }

            if (profile.Items[ItemSlot.RupeesToAdd] > 0)
                profile.Items[ItemSlot.RupeesToAdd]--;

            if (profile.Items[ItemSlot.RupeesToSubtract] > 0)
                profile.Items[ItemSlot.RupeesToSubtract]--;
        }
    }

    private void UpdateLiftItem()
    {
        if (State.Play.liftItemId == 0)
            return;

        State.Play.liftItemTimer--;

        if (State.Play.liftItemTimer == 0)
        {
            State.Play.liftItemId = 0;
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
            DrawLinkLiftingItem(State.Play.liftItemId);
        }
        else
        {
            Game.Link.Draw();
        }

        objOverPlayer?.DecoratedDraw();

        if (IsUWMain(curRoomId))
        {
            DrawDoors(curRoomId, true, 0, 0);
        }
    }

    private void DrawSubmenu()
    {
        using (var _ = Graphics.SetClip(0, TileMapBaseY + SubmenuOffsetY, TileMapWidth, TileMapHeight - SubmenuOffsetY))
        {
            ClearScreen();
            DrawMap(curRoomId, curTileMapIndex, 0, SubmenuOffsetY);
        }

        if (IsUWMain(curRoomId))
        {
            DrawDoors(curRoomId, true, 0, SubmenuOffsetY);
        }

        menu.Draw(SubmenuOffsetY);
    }

    private void DrawObjects(out Actor? objOverPlayer)
    {
        objOverPlayer = null;

        for (var i = ObjectSlot.FirstSlot; i < ObjectSlot.MaxObjects; i++)
        {
            curObjectSlot = i;

            var obj = objects[(int)i];
            if (obj != null && !obj.IsDeleted)
            {
                if (!obj.Flags.HasFlag(ActorFlags.DrawAbovePlayer) || objOverPlayer == null)
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
        if (IsUWCellar(curRoomId))
        {
            MakeCellarObjects();
            return;
        }
        else if (State.Play.roomType == PlayState.RoomType.Cave)
        {
            MakeCaveObjects();
            return;
        }

        var roomAttr = roomAttrs[curRoomId];
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
            count = 1;

        CalcObjCountToMake(ref objId, ref count);
        RoomObjCount = count;

        if (objId > 0 && count > 0)
        {
            var isList = objId >= ObjType.Rock;
            var repeatedIds = new byte[(int)ObjectSlot.MaxMonsters];
            ReadOnlySpan<byte> list = null;

            if (isList)
            {
                var listId = objId - ObjType.Rock;
                list = objLists.GetItem(listId);
            }
            else
            {
                Array.Fill(repeatedIds, (byte)objId, 0, count);
                list = repeatedIds;
            }

            var dirOrd = entryDir.GetOrdinal();
            // var spotSeq = extraData.GetItem<SpotSeq>(Extra.SpawnSpots);
            var spots = extraData.ReadLengthPrefixedItem((int)Extra.SpawnSpots);
            var spotsLen = spots.Length / 4;
            var dirSpots = spots[(spotsLen * dirOrd)..]; // JOE: This is very sus.

            var x = 0;
            var y = 0;
            for (var i = 0; i < count; i++, slot++)
            {
                // An earlier objects that's made might make some objects in slots after it.
                // Maybe MakeMonster should take a reference to the current index.
                if (GetObject(slot) != null) continue;

                curObjSlot = (int)slot;

                var type = (ObjType)list[(int)slot];

                if (edgeObjects
                    && type != ObjType.Zora // TODO: Move this to an attribute on the class?
                    && type != ObjType.Armos
                    && type != ObjType.StandingFire
                    && type != ObjType.Whirlwind
                    )
                {
                    placeholderTypes[(int)slot] = (byte)type;
                }
                else if (FindSpawnPos(type, dirSpots, spotsLen, ref x, ref y))
                {
                    var obj = Actor.FromType(type, Game, x, y);
                    objects[(int)slot] = obj;
                }
            }
        }

        var monster = GetObject(ObjectSlot.Monster1);
        if (monster != null)
            RoomObj = monster;

        if (IsOverworld())
        {
            var owRoomAttr = CurrentOWRoomAttrs;
            if (owRoomAttr.HasZora())
            {
                curObjSlot = (int)slot;

                var zora = Actor.FromType(ObjType.Zora, Game, 0, 0);
                SetObject(slot, zora);
                slot++;
            }
        }
    }

    private void MakeCellarObjects()
    {
        static ReadOnlySpan<int> startXs() => new int[] { 0x20, 0x60, 0x90, 0xD0 };
        const int startY = 0x9D;

        for (var i = 0; i < 4; i++)
        {
            curObjSlot = i;

            var keese = Actor.FromType(ObjType.BlueKeese, Game, startXs()[i], startY);
            Game.World.SetObject((ObjectSlot)i, keese);
        }
    }

    private void MakeCaveObjects()
    {
        var owRoomAttrs = CurrentOWRoomAttrs;
        var caveIndex = owRoomAttrs.GetCaveId() - FirstCaveIndex;

        var caves = extraData.LoadVariableLengthData<CaveSpecListStruct, CaveSpecList>((int)Extra.Caves);

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
            var stringIdTables = extraData.GetItem<LevelPersonStrings>(Extra.LevelPersonStringIds);

            var levelIndex = infoBlock.EffectiveLevelNumber - 1;
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
        static ReadOnlySpan<int> fireXs() => new[] { 0x48, 0xA8 };

        if (spec.DwellerType != ObjType.None)
        {
            curObjSlot = 0;
            var person = GlobalFunctions.MakePerson(Game, type, spec, 0x78, 0x80);
            Game.World.SetObject(0, person);
        }

        for (var i = 0; i < 2; i++)
        {
            curObjSlot++;
            var fire = new StandingFireActor(Game, fireXs()[i], 0x80);
            Game.World.SetObject((ObjectSlot)curObjSlot, fire);
        }
    }

    private void MakeWhirlwind()
    {
        static ReadOnlySpan<int> teleportYs() => new[] { 0x8D, 0xAD, 0x8D, 0x8D, 0xAD, 0x8D, 0xAD, 0x5D };

        if (WhirlwindTeleporting != 0)
        {
            var y = teleportYs()[TeleportingRoomIndex];

            WhirlwindTeleporting = 2;

            var whirlwind = new WhirlwindActor(Game, 0, y);
            Game.World.SetObject(ObjectSlot.Whirlwind, whirlwind);

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
        if (stunTimers[(int)ObjectSlot.EdgeObjTimer] != 0)
            return;

        stunTimers[(int)ObjectSlot.EdgeObjTimer] = Random.Shared.Next(0, 4) + 2;

        var x = EdgeX;
        var y = EdgeY;

        for (; ; )
        {
            if (x == 0)
                y += 0x10;
            else if (x == 0xF0)
                y -= 0x10;

            if (y == 0x40)
                x -= 0x10;
            else if (y == 0xE0)
                x += 0x10;

            var row = (y / 8) - 8;
            var col = (x / 8);
            var behavior = GetTileBehavior(row, col);

            if (behavior != TileBehavior.Sand && !CollidesTile(behavior))
                break;
            if (y == EdgeY && x == EdgeX)
                break;
        }

        EdgeX = x;
        EdgeY = y;

        if (Math.Abs(Game.Link.X - x) >= 0x22 || Math.Abs(Game.Link.Y - y) >= 0x22)
        {
            // What?
            var obj = Actor.FromType((ObjType)placeholderTypes[curObjSlot], Game, x, y - 3);
            objects[curObjSlot] = obj;
            placeholderTypes[curObjSlot] = 0;
            obj.Decoration = 0;
        }
    }

    private void HandleNormalObjectDeath()
    {
        var obj = objects[curObjSlot] ?? throw new Exception("Missing object");
        var x = obj.X;
        var y = obj.Y;

        objects[curObjSlot] = null;

        // JOE: TODO: Put whatever this is on the object itself.
        if (obj.ObjType is not (ObjType.ChildGel or ObjType.RedKeese or ObjType.DeadDummy))
        {
            var cycle = WorldKillCycle + 1;
            if (cycle == 10)
                cycle = 0;
            WorldKillCycle = (byte)cycle;

            if (obj is not ZoraActor)
                RoomKillCount++;
        }

        TryDroppingItem(obj, x, y);
    }

    private static ReadOnlySpan<int> _classBases => new[] { 0, 10, 20, 30 };
    private static ReadOnlySpan<int> _classRates => new[] { 0x50, 0x98, 0x68, 0x68 };
    private static ReadOnlySpan<int> _dropItems => new[] {
        0x22, 0x18, 0x22, 0x18, 0x23, 0x18, 0x22, 0x22, 0x18, 0x18, 0x0F, 0x18, 0x22, 0x18, 0x0F, 0x22,
        0x21, 0x18, 0x18, 0x18, 0x22, 0x00, 0x18, 0x21, 0x18, 0x22, 0x00, 0x18, 0x00, 0x22, 0x22, 0x22,
        0x23, 0x18, 0x22, 0x23, 0x22, 0x22, 0x22, 0x18
    };


    private void TryDroppingItem(Actor origType, int x, int y)
    {
        if (curObjSlot == (int)ObjectSlot.Monster1 && origType is StalfosActor or GibdoActor)
            return;

        var objClass = origType.Attributes.GetItemDropClass();
        if (objClass == 0)
            return;
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
            int r = Random.Shared.GetByte();
            var rate = _classRates[objClass];

            if (r >= rate)
                return;

            var classIndex = _classBases[objClass] + WorldKillCycle;
            itemId = (ItemId)_dropItems[classIndex];
        }

        var obj = GlobalFunctions.MakeItem(Game, itemId, x, y, false);
        objects[curObjSlot] = obj;
    }

    private void FillHeartsStep()
    {
        Game.Sound.PlayEffect(SoundEffect.Character);

        var profile = GetProfile();
        var maxHeartsValue = profile.GetMaxHeartsValue();

        FillHearts(6);

        if (profile.Hearts == maxHeartsValue)
        {
            Pause = 0;
            SwordBlocked = false;
        }
    }

    private void GotoScroll(Direction dir)
    {
        if (dir == Direction.None) throw new ArgumentOutOfRangeException(nameof(dir));

        State.Scroll.curRoomId = curRoomId;
        State.Scroll.scrollDir = dir;
        State.Scroll.substate = ScrollState.Substates.Start;
        curMode = GameMode.Scroll;
    }

    private void GotoScroll(Direction dir, int currentRoomId)
    {
        GotoScroll(dir);
        State.Scroll.curRoomId = currentRoomId;
    }

    private bool CalcMazeStayPut(Direction dir)
    {
        if (!IsOverworld())
            return false;

        var stayPut = false;
        var mazeOptional = sparseRoomAttrs.FindSparseAttr<SparseMaze>(Sparse.Maze, curRoomId);
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
                        stayPut = true;
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
        sScrollFuncs[(int)State.Scroll.substate]();
    }

    private void UpdateScroll_Start()
    {
        GetWorldCoord(State.Scroll.curRoomId, out var roomRow, out var roomCol);

        Actor.MoveSimple(ref roomCol, ref roomRow, State.Scroll.scrollDir, 1);

        var nextRoomId = CalcMazeStayPut(State.Scroll.scrollDir) ? State.Scroll.curRoomId : MakeRoomId(roomRow, roomCol);

        State.Scroll.nextRoomId = nextRoomId;
        State.Scroll.substate = ScrollState.Substates.AnimatingColors;
    }

    private void UpdateScroll_AnimatingColors()
    {
        if (CurColorSeqNum == 0)
        {
            State.Scroll.substate = ScrollState.Substates.LoadRoom;
            return;
        }

        if ((Game.GetFrameCounter() & 4) != 0)
        {
            CurColorSeqNum--;

            var colorSeq = extraData.ReadLengthPrefixedItem((int)Extra.PondColors);
            int color = colorSeq[CurColorSeqNum];
            Graphics.SetColorIndexed((Palette)3, 3, color);
            Graphics.UpdatePalettes();

            if (CurColorSeqNum == 0)
                State.Scroll.substate = ScrollState.Substates.LoadRoom;
        }
    }

    private void UpdateScroll_FadeOut()
    {
        if (State.Scroll.timer == 0)
        {
            for (var i = 0; i < 2; i++)
            {
                Graphics.SetPaletteIndexed((Palette)i + 2, infoBlock.DarkPalette(DarkRoomFadeStep, i));
            }
            Graphics.UpdatePalettes();

            DarkRoomFadeStep++;

            if (DarkRoomFadeStep == 4)
            {
                State.Scroll.substate = ScrollState.Substates.Scroll;
                State.Scroll.timer = ScrollState.StateTime;
            }
            else
            {
                State.Scroll.timer = 9;
            }
        }
        else
        {
            State.Scroll.timer--;
        }
    }

    private void UpdateScroll_LoadRoom()
    {
        if (State.Scroll.scrollDir == Direction.Down
            && !IsOverworld()
            && curRoomId == infoBlock.StartRoomId)
        {
            GotoLoadLevel(0);
            return;
        }

        State.Scroll.offsetX = 0;
        State.Scroll.offsetY = 0;
        State.Scroll.speedX = 0;
        State.Scroll.speedY = 0;
        State.Scroll.oldMapToNewMapDistX = 0;
        State.Scroll.oldMapToNewMapDistY = 0;

        switch (State.Scroll.scrollDir)
        {
            case Direction.Left:
                State.Scroll.offsetX = -TileMapWidth;
                State.Scroll.speedX = ScrollSpeed;
                State.Scroll.oldMapToNewMapDistX = TileMapWidth;
                break;

            case Direction.Right:
                State.Scroll.offsetX = TileMapWidth;
                State.Scroll.speedX = -ScrollSpeed;
                State.Scroll.oldMapToNewMapDistX = -TileMapWidth;
                break;

            case Direction.Up:
                State.Scroll.offsetY = -TileMapHeight;
                State.Scroll.speedY = ScrollSpeed;
                State.Scroll.oldMapToNewMapDistY = TileMapHeight;
                break;

            case Direction.Down:
                State.Scroll.offsetY = TileMapHeight;
                State.Scroll.speedY = -ScrollSpeed;
                State.Scroll.oldMapToNewMapDistY = -TileMapHeight;
                break;
        }

        State.Scroll.oldRoomId = curRoomId;

        var nextRoomId = State.Scroll.nextRoomId;
        var nextTileMapIndex = (curTileMapIndex + 1) % 2;
        State.Scroll.oldTileMapIndex = curTileMapIndex;

        TempShutterRoomId = nextRoomId;
        TempShutterDoorDir = State.Scroll.scrollDir.GetOppositeDirection();

        LoadRoom(nextRoomId, nextTileMapIndex);

        var uwRoomAttrs = GetUWRoomAttrs(nextRoomId);
        if (uwRoomAttrs.IsDark() && DarkRoomFadeStep == 0)
        {
            State.Scroll.substate = ScrollState.Substates.FadeOut;
            State.Scroll.timer = Game.Cheats.SpeedUp ? 1 : 9;
        }
        else
        {
            State.Scroll.substate = ScrollState.Substates.Scroll;
            State.Scroll.timer = Game.Cheats.SpeedUp ? 1 : ScrollState.StateTime;
        }
    }

    private void UpdateScroll_Scroll()
    {
        if (State.Scroll.timer > 0)
        {
            State.Scroll.timer--;
            return;
        }

        if (State.Scroll.offsetX == 0 && State.Scroll.offsetY == 0)
        {
            GotoEnter(State.Scroll.scrollDir);
            if (IsOverworld() && State.Scroll.nextRoomId == (int)UniqueRoomIds.TopRightOverworldSecret)
            {
                Game.Sound.PlayEffect(SoundEffect.Secret);
            }
            return;
        }

        if (Game.Cheats.SpeedUp)
        {
            // JOE: TODO
        }

        State.Scroll.offsetX += State.Scroll.speedX;
        State.Scroll.offsetY += State.Scroll.speedY;

        var playerLimits = Link.PlayerLimits;

        if (State.Scroll.speedX != 0)
        {
            var x = Game.Link.X + State.Scroll.speedX;
            if (x < playerLimits[1])
                x = playerLimits[1];
            else if (x > playerLimits[0])
                x = playerLimits[0];
            Game.Link.X = x;
        }
        else
        {
            var y = Game.Link.Y + State.Scroll.speedY;
            if (y < playerLimits[3])
                y = playerLimits[3];
            else if (y > playerLimits[2])
                y = playerLimits[2];
            Game.Link.Y = y;
        }

        Game.Link.Animator.Advance();
    }

    private void DrawScroll()
    {
        using (var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight))
        {
            ClearScreen();

            if (State.Scroll.substate is ScrollState.Substates.Scroll or ScrollState.Substates.FadeOut)
            {
                var oldMapOffsetX = State.Scroll.offsetX + State.Scroll.oldMapToNewMapDistX;
                var oldMapOffsetY = State.Scroll.offsetY + State.Scroll.oldMapToNewMapDistY;

                DrawMap(curRoomId, curTileMapIndex, State.Scroll.offsetX, State.Scroll.offsetY);
                DrawMap(State.Scroll.oldRoomId, State.Scroll.oldTileMapIndex, oldMapOffsetX, oldMapOffsetY);
            }
            else
            {
                DrawMap(curRoomId, curTileMapIndex, 0, 0);
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

        State.Leave.curRoomId = curRoomId;
        State.Leave.scrollDir = dir;
        State.Leave.timer = LeaveState.StateTime;
        curMode = GameMode.Leave;
    }

    private void GotoLeave(Direction dir, int currentRoomId)
    {
        GotoLeave(dir);
        State.Leave.curRoomId = currentRoomId;
    }

    private void UpdateLeave()
    {
        var playerLimits = Link.PlayerLimits;
        var dirOrd = Game.Link.Facing.GetOrdinal();
        var coord = Game.Link.Facing.IsVertical() ? Game.Link.Y : Game.Link.X;

        if (coord != playerLimits[dirOrd])
        {
            Game.Link.MoveLinear(State.Leave.scrollDir, Link.WalkSpeed);
            Game.Link.Animator.Advance();
            return;
        }

        if (State.Leave.timer == 0)
        {
            Game.Link.Animator.AdvanceFrame();
            GotoScroll(State.Leave.scrollDir, State.Leave.curRoomId);
            return;
        }
        State.Leave.timer--;
    }

    private void DrawLeave()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        DrawRoomNoObjects();
    }

    private void GotoEnter(Direction dir)
    {
        State.Enter.substate = EnterState.Substates.Start;
        State.Enter.scrollDir = dir;
        State.Enter.timer = 0;
        State.Enter.playerPriority = SpritePriority.AboveBg;
        State.Enter.playerSpeed = Link.WalkSpeed;
        State.Enter.gotoPlay = false;
        curMode = GameMode.Enter;
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
        sEnterFuncs[(int)State.Enter.substate]();

        if (State.Enter.gotoPlay)
        {
            var origShutterDoorDir = TempShutterDoorDir;
            TempShutterDoorDir = Direction.None;
            if (IsUWMain(curRoomId)
                && origShutterDoorDir != Direction.None
                && GetDoorType(curRoomId, origShutterDoorDir) == DoorType.Shutter)
            {
                Game.Sound.PlayEffect(SoundEffect.Door);
                var doorOrd = origShutterDoorDir.GetOrdinal();
                UpdateDoorTileBehavior(doorOrd);
            }

            statusBar.EnableFeatures(StatusBarFeatures.All, true);
            if (IsOverworld() && fromUnderground != 0)
            {
                Game.Sound.PlaySong(infoBlock.SongId, SongStream.MainSong, true);
            }
            GotoPlay();
            return;
        }
        Game.Link.Animator.Advance();
    }

    private void UpdateEnter_Start()
    {
        triggeredDoorCmd = 0;
        triggeredDoorDir = Direction.None;

        if (IsOverworld())
        {
            var behavior = GetTileBehaviorXY(Game.Link.X, Game.Link.Y + 3);
            if (behavior == TileBehavior.Cave)
            {
                Game.Link.Y += MobTileHeight;
                Game.Link.Facing = Direction.Down;

                State.Enter.playerFraction = 0;
                State.Enter.playerSpeed = 0x40;
                State.Enter.playerPriority = SpritePriority.BelowBg;
                State.Enter.scrollDir = Direction.Up;
                State.Enter.targetX = Game.Link.X;
                State.Enter.targetY = Game.Link.Y - 0x10;
                State.Enter.substate = EnterState.Substates.WalkCave;

                Game.Sound.StopAll();
                Game.Sound.PlayEffect(SoundEffect.Stairs);
            }
            else
            {
                State.Enter.substate = EnterState.Substates.Wait;
                State.Enter.timer = EnterState.StateTime;
            }
        }
        else if (State.Enter.scrollDir != Direction.None)
        {
            var uwRoomAttrs = CurrentUWRoomAttrs;
            var oppositeDir = State.Enter.scrollDir.GetOppositeDirection();
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

            State.Enter.targetX = Game.Link.X;
            State.Enter.targetY = Game.Link.Y;
            Actor.MoveSimple(
                ref State.Enter.targetX,
                ref State.Enter.targetY,
                State.Enter.scrollDir,
                distance);

            if (!uwRoomAttrs.IsDark() && DarkRoomFadeStep > 0)
            {
                State.Enter.substate = EnterState.Substates.FadeIn;
                State.Enter.timer = 9;
            }
            else
            {
                State.Enter.substate = EnterState.Substates.Walk;
            }

            Game.Link.Facing = State.Enter.scrollDir;
        }
        else
        {
            State.Enter.substate = EnterState.Substates.Wait;
            State.Enter.timer = EnterState.StateTime;
        }

        doorwayDir = IsUWMain(curRoomId) ? State.Enter.scrollDir : Direction.None;
    }

    private void UpdateEnter_Wait()
    {
        State.Enter.timer--;
        if (State.Enter.timer == 0)
        {
            State.Enter.gotoPlay = true;
        }
    }

    private void UpdateEnter_FadeIn()
    {
        if (DarkRoomFadeStep == 0)
        {
            State.Enter.substate = EnterState.Substates.Walk;
            return;
        }

        if (State.Enter.timer == 0)
        {
            DarkRoomFadeStep--;
            State.Enter.timer = 9;

            for (var i = 0; i < 2; i++)
            {
                Graphics.SetPaletteIndexed((Palette)i + 2, infoBlock.DarkPalette(DarkRoomFadeStep, i));
            }
            Graphics.UpdatePalettes();
        }
        else
        {
            State.Enter.timer--;
        }
    }

    private void UpdateEnter_Walk()
    {
        if (Game.Link.X == State.Enter.targetX
            && Game.Link.Y == State.Enter.targetY)
        {
            State.Enter.gotoPlay = true;
        }
        else
        {
            Game.Link.MoveLinear(State.Enter.scrollDir, State.Enter.playerSpeed);
        }
    }

    private void UpdateEnter_WalkCave()
    {
        if (Game.Link.X == State.Enter.targetX
            && Game.Link.Y == State.Enter.targetY)
        {
            State.Enter.gotoPlay = true;
        }
        else
        {
            MovePlayer(State.Enter.scrollDir, State.Enter.playerSpeed, ref State.Enter.playerFraction);
        }
    }

    private void DrawEnter()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);

        if (State.Enter.substate != EnterState.Substates.Start)
        {
            DrawRoomNoObjects(State.Enter.playerPriority);
        }
    }

    public void GotoLoadLevel(int level, bool restartOW = false)
    {
        State.LoadLevel.level = level;
        State.LoadLevel.substate = LoadLevelState.Substates.Load;
        State.LoadLevel.timer = 0;
        State.LoadLevel.restartOW = restartOW;

        curMode = GameMode.LoadLevel;
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
        return textTable.GetItem((int)stringId);
    }

    private void UpdateLoadLevel()
    {
        if (State.LoadLevel.substate == LoadLevelState.Substates.Load)
        {
            State.LoadLevel.timer = LoadLevelState.StateTime;
            State.LoadLevel.substate = LoadLevelState.Substates.Wait;

            int origLevel = infoBlock.LevelNumber;
            var origRoomId = curRoomId;

            Game.Sound.StopAll();
            StatusBarVisible = false;
            LoadLevel(State.LoadLevel.level);

            // Let the Unfurl game mode load the room and reset colors.

            if (State.LoadLevel.level == 0)
            {
                curRoomId = SavedOWRoomId;
                SavedOWRoomId = -1;
                fromUnderground = 2;
            }
            else
            {
                curRoomId = infoBlock.StartRoomId;
                if (origLevel == 0)
                    SavedOWRoomId = origRoomId;
            }
        }
        else if (State.LoadLevel.substate == LoadLevelState.Substates.Wait)
        {
            if (State.LoadLevel.timer == 0)
            {
                GotoUnfurl(State.LoadLevel.restartOW);
                return;
            }

            State.LoadLevel.timer--;
        }
    }

    private void DrawLoadLevel()
    {
        using var _ = Graphics.SetClip(0, 0, Global.StdViewWidth, Global.StdViewHeight);
        ClearScreen();
    }

    private void GotoUnfurl(bool restartOW = false)
    {
        State.Unfurl.substate = UnfurlState.Substates.Start;
        State.Unfurl.timer = UnfurlState.StateTime;
        State.Unfurl.stepTimer = 0;
        State.Unfurl.left = 0x80;
        State.Unfurl.right = 0x80;
        State.Unfurl.restartOW = restartOW;

        ClearLevelData();

        curMode = GameMode.Unfurl;
    }

    private void UpdateUnfurl()
    {
        if (State.Unfurl.substate == UnfurlState.Substates.Start)
        {
            State.Unfurl.substate = UnfurlState.Substates.Unfurl;
            StatusBarVisible = true;
            statusBar.EnableFeatures(StatusBarFeatures.All, false);

            if (infoBlock.LevelNumber == 0 && !State.Unfurl.restartOW)
            {
                LoadRoom(curRoomId, 0);
                SetPlayerExitPosOW(curRoomId);
            }
            else
            {
                LoadRoom(infoBlock.StartRoomId, 0);
                Game.Link.X = StartX;
                Game.Link.Y = infoBlock.StartY;
            }

            for (var i = 0; i < LevelInfoBlock.LevelPaletteCount; i++)
            {
                Graphics.SetPaletteIndexed((Palette)i, infoBlock.GetPalette(i));
            }

            SetPlayerColor();
            Graphics.UpdatePalettes();
            return;
        }

        if (State.Unfurl.timer > 0)
        {
            State.Unfurl.timer--;
            return;
        }

        if (State.Unfurl.left == 0 || Game.Cheats.SpeedUp)
        {
            statusBar.EnableFeatures(StatusBarFeatures.EquipmentAndMap, true);
            if (!IsOverworld())
            {
                Game.Sound.PlaySong(infoBlock.SongId, SongStream.MainSong, true);
            }
            GotoEnter(Direction.Up);
            return;
        }

        if (State.Unfurl.stepTimer == 0)
        {
            State.Unfurl.left -= 8;
            State.Unfurl.right += 8;
            State.Unfurl.stepTimer = 4;
        }
        else
        {
            State.Unfurl.stepTimer--;
        }
    }

    private void DrawUnfurl()
    {
        if (State.Unfurl.substate == UnfurlState.Substates.Start)
            return;

        var width = State.Unfurl.right - State.Unfurl.left;

        using (var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight))
        {
            ClearScreen();
        }

        using (var _ = Graphics.SetClip(State.Unfurl.left, TileMapBaseY, width, TileMapHeight))
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
        State.EndLevel.substate = EndLevelState.Substates.Start;
        curMode = GameMode.EndLevel;
    }

    private void UpdateEndLevel()
    {
        sEndLevelFuncs[(int)State.EndLevel.substate]();
    }

    private void UpdateEndLevel_Start()
    {
        State.EndLevel.substate = EndLevelState.Substates.Wait1;
        State.EndLevel.timer = EndLevelState.Wait1Time;

        State.EndLevel.left = 0;
        State.EndLevel.right = TileMapWidth;
        State.EndLevel.stepTimer = 4;

        statusBar.EnableFeatures(StatusBarFeatures.Equipment, false);
        Game.Sound.PlaySong(SongId.Triforce, SongStream.MainSong, false);
    }

    private void UpdateEndLevel_Wait()
    {
        if (State.EndLevel.timer == 0)
        {
            if (State.EndLevel.substate == EndLevelState.Substates.Wait3)
                GotoLoadLevel(0);
            else
            {
                State.EndLevel.substate += 1;
                if (State.EndLevel.substate == EndLevelState.Substates.Flash)
                    State.EndLevel.timer = EndLevelState.FlashTime;
            }
        }
        else
            State.EndLevel.timer--;
    }

    private void UpdateEndLevel_Flash()
    {
        if (State.EndLevel.timer == 0)
        {
            State.EndLevel.substate += 1;
        }
        else
        {
            var step = State.EndLevel.timer & 0x7;
            if (step == 0)
                SetFlashPalette();
            else if (step == 3)
                SetLevelPalette();
            State.EndLevel.timer--;
        }
    }

    private void UpdateEndLevel_FillHearts()
    {
        var maxHeartValue = profile.GetMaxHeartsValue();

        Game.Sound.PlayEffect(SoundEffect.Character);

        if (profile.Hearts == maxHeartValue)
        {
            State.EndLevel.substate += 1;
            State.EndLevel.timer = EndLevelState.Wait2Time;
        }
        else
        {
            FillHearts(6);
        }
    }

    private void UpdateEndLevel_Furl()
    {
        if (State.EndLevel.left == WorldMidX)
        {
            State.EndLevel.substate += 1;
            State.EndLevel.timer = EndLevelState.Wait3Time;
        }
        else if (State.EndLevel.stepTimer == 0)
        {
            State.EndLevel.left += 8;
            State.EndLevel.right -= 8;
            State.EndLevel.stepTimer = 4;
        }
        else
        {
            State.EndLevel.stepTimer--;
        }
    }

    private void DrawEndLevel()
    {
        var left = 0;
        var width = TileMapWidth;

        if (State.EndLevel.substate >= EndLevelState.Substates.Furl)
        {
            left = State.EndLevel.left;
            width = State.EndLevel.right - State.EndLevel.left;

            using (var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight))
            {
                ClearScreen();
            }
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
        State.WinGame.substate = WinGameState.Substates.Start;
        State.WinGame.timer = 162;
        State.WinGame.left = 0;
        State.WinGame.right = TileMapWidth;
        State.WinGame.stepTimer = 0;
        State.WinGame.npcVisual = WinGameState.NpcVisual.Npc_Stand;

        curMode = GameMode.WinGame;
    }

    private void UpdateWinGame()
    {
        sWinGameFuncs[(int)State.WinGame.substate]();
    }

    private static readonly byte[] winGameStr1 = new byte[] {
        0x1d, 0x11, 0x0a, 0x17, 0x14, 0x1c, 0x24, 0x15, 0x12, 0x17, 0x14, 0x28, 0x22, 0x18, 0x1e, 0x2a,
        0x1b, 0x8e, 0x64, 0x1d, 0x11, 0x0e, 0x24, 0x11, 0x0e, 0x1b, 0x18, 0x24, 0x18, 0x0f, 0x24, 0x11,
        0x22, 0x1b, 0x1e, 0x15, 0x0e, 0xec
    };

    private void UpdateWinGame_Start()
    {
        if (State.WinGame.timer > 0)
        {
            State.WinGame.timer--;
        }
        else if (State.WinGame.left == WorldMidX)
        {
            State.WinGame.substate = WinGameState.Substates.Text1;
            statusBar.EnableFeatures(StatusBarFeatures.EquipmentAndMap, false);

            // A959

            textBox1 = new TextBox(Game, winGameStr1); // FIX
        }
        else if (State.WinGame.stepTimer == 0)
        {
            State.WinGame.left += 8;
            State.WinGame.right -= 8;
            State.WinGame.stepTimer = 4;
        }
        else
        {
            State.WinGame.stepTimer--;
        }
    }

    private void UpdateWinGame_Text1()
    {
        textBox1.Update();
        if (textBox1.IsDone())
        {
            State.WinGame.substate = WinGameState.Substates.Stand;
            State.WinGame.timer = 76;
        }
    }

    private void UpdateWinGame_Stand()
    {
        State.WinGame.timer--;
        if (State.WinGame.timer == 0)
        {
            State.WinGame.substate = WinGameState.Substates.Hold1;
            State.WinGame.timer = 64;
        }
    }

    private void UpdateWinGame_Hold1()
    {
        State.WinGame.npcVisual = WinGameState.NpcVisual.Npc_Lift;
        State.WinGame.timer--;
        if (State.WinGame.timer == 0)
        {
            State.WinGame.substate = WinGameState.Substates.Colors;
            State.WinGame.timer = 127;
        }
    }

    private void UpdateWinGame_Colors()
    {
        State.WinGame.timer--;
        if (State.WinGame.timer == 0)
        {
            State.WinGame.substate = WinGameState.Substates.Hold2;
            State.WinGame.timer = 131;
            Game.Sound.PlaySong(SongId.Ending, SongStream.MainSong, true);
        }
    }

    private static ReadOnlySpan<byte> winGameStr2 => new byte[] {
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
        State.WinGame.timer--;
        if (State.WinGame.timer == 0)
        {
            State.WinGame.substate = WinGameState.Substates.Text2;
            textBox2 = new TextBox(Game, winGameStr2.ToArray(), 8); // TODO
            textBox2.SetY(WinGameState.TextBox2Top);
        }
    }

    private void UpdateWinGame_Text2()
    {
        textBox2.Update();
        if (textBox2.IsDone())
        {
            State.WinGame.substate = WinGameState.Substates.Hold3;
            State.WinGame.timer = 129;
        }
    }

    private void UpdateWinGame_Hold3()
    {
        State.WinGame.timer--;
        if (State.WinGame.timer == 0)
        {
            State.WinGame.substate = WinGameState.Substates.NoObjects;
            State.WinGame.timer = 32;
        }
    }

    private void UpdateWinGame_NoObjects()
    {
        State.WinGame.npcVisual = WinGameState.NpcVisual.Npc_None;
        State.WinGame.timer--;
        if (State.WinGame.timer == 0)
        {
            credits = new Credits();
            State.WinGame.substate = WinGameState.Substates.Credits;
        }
    }

    private void UpdateWinGame_Credits()
    {
        // TODO TextBox** boxes[] = { &textBox1, &textBox2 };
        // TODO int startYs[] = { TextBox::StartY, WinGameState.Substates.TextBox2Top };
        // TODO
        // TODO for (int i = 0; i < _countof(boxes); i++)
        // TODO {
        // TODO     TextBox* box = *boxes[i];
        // TODO     if (box != null)
        // TODO     {
        // TODO         int textToCreditsY = Credits::StartY - startYs[i];
        // TODO         box.SetY(credits.GetTop() - textToCreditsY);
        // TODO         int bottom = box.Y + box.GetHeight();
        // TODO         if (bottom <= 0)
        // TODO         {
        // TODO             delete box;
        // TODO             *boxes[i] = null;
        // TODO         }
        // TODO     }
        // TODO }
        // TODO
        // TODO credits.Update();
        // TODO if (credits.IsDone())
        // TODO {
        // TODO     if (Game.Input.IsButtonPressing(Button.Start))
        // TODO     {
        // TODO         delete credits;
        // TODO         credits = null;
        // TODO         delete player;
        // TODO         player = null;
        // TODO         DeleteObjects();
        // TODO         SubmenuOffsetY = 0;
        // TODO         statusBarVisible = false;
        // TODO         statusBar.EnableFeatures(StatusBar::Feature_All, true);
        // TODO
        // TODO         byte name[MaxNameLength];
        // TODO         byte nameLength = profile.NameLength;
        // TODO         byte deaths = profile.Deaths;
        // TODO         memcpy(name, profile.Name, nameLength);
        // TODO         memset(&profile, 0, sizeof profile);
        // TODO         memcpy(profile.Name, name, nameLength);
        // TODO         profile.NameLength = nameLength;
        // TODO         profile.Deaths = deaths;
        // TODO         profile.Quest = 1;
        // TODO         profile.Items[ItemSlot.HeartContainers] = DefaultHearts;
        // TODO         profile.Items[ItemSlot.MaxBombs] = DefaultBombs;
        // TODO         SaveFolder::WriteProfile(profileSlot, profile);
        // TODO
        // TODO         Game.Sound.StopAll();
        // TODO         GotoFileMenu();
        // TODO     }
        // TODO }
        // TODO else
        // TODO {
        // TODO     int statusTop = credits.GetTop() - Credits::StartY;
        // TODO     int statusBottom = statusTop + StatusBar::StatusBarHeight;
        // TODO     if (statusBottom > 0)
        // TODO         SubmenuOffsetY = statusTop;
        // TODO     else
        // TODO         SubmenuOffsetY = -StatusBar::StatusBarHeight;
        // TODO }
    }

    private void DrawWinGame()
    {
        // TODO ALLEGRO_COLOR backColor;
        // TODO
        // TODO Graphics.SetClip(0, 0, StdViewWidth, StdViewHeight);
        // TODO if (State.WinGame.substate == WinGameState.Substates.Colors)
        // TODO {
        // TODO     int sysColors[] = { 0x0F, 0x2A, 0x16, 0x12 };
        // TODO     int frame = State.WinGame.timer & 3;
        // TODO     int sysColor = sysColors[frame];
        // TODO     ClearScreen(sysColor);
        // TODO     backColor = Graphics.GetSystemColor(sysColor);
        // TODO }
        // TODO else
        // TODO {
        // TODO     ClearScreen();
        // TODO     backColor = al_map_rgb(0, 0, 0);
        // TODO }
        // TODO Graphics.ResetClip();
        // TODO
        // TODO statusBar.Draw(SubmenuOffsetY, backColor);
        // TODO
        // TODO if (State.WinGame.substate == WinGameState.Substates.Start)
        // TODO {
        // TODO     int left = State.WinGame.left;
        // TODO     int width = State.WinGame.right - State.WinGame.left;
        // TODO
        // TODO     Graphics.SetClip(left, TileMapBaseY, width, TileMapHeight);
        // TODO     DrawRoomNoObjects(SpritePriority.None);
        // TODO     Graphics.ResetClip();
        // TODO
        // TODO     Game.Link.Draw();
        // TODO     DrawObjects();
        // TODO }
        // TODO else
        // TODO {
        // TODO     var zelda = objects[ObjectSlot.Monster1];
        // TODO
        // TODO     if (State.WinGame.npcVisual == WinGameState.NpcVisual.Npc_Stand)
        // TODO     {
        // TODO         zelda.Draw();
        // TODO         Game.Link.Draw();
        // TODO     }
        // TODO     else if (State.WinGame.npcVisual == WinGameState.NpcVisual.Npc_Lift)
        // TODO     {
        // TODO         DrawZeldaLiftingTriforce(zelda.X, zelda.Y);
        // TODO         DrawLinkLiftingItem(ItemId.TriforcePiece);
        // TODO     }
        // TODO
        // TODO     if (credits != null)
        // TODO         credits.Draw();
        // TODO     if (textBox1 != null)
        // TODO         textBox1.Draw();
        // TODO     if (textBox2 != null)
        // TODO         textBox2.Draw();
        // TODO }
    }

    private void GotoStairs(TileBehavior behavior)
    {
        State.Stairs.substate = StairsState.Substates.Start;
        State.Stairs.tileBehavior = behavior;
        State.Stairs.playerPriority = SpritePriority.AboveBg;

        curMode = GameMode.Stairs;
    }

    private void UpdateStairsState()
    {
        if (State.Stairs.substate == StairsState.Substates.Start)
        {
            State.Stairs.playerPriority = SpritePriority.BelowBg;

            if (IsOverworld())
                Game.Sound.StopAll();

            if (State.Stairs.tileBehavior == TileBehavior.Cave)
            {
                Game.Link.Facing = Direction.Up;

                State.Stairs.targetX = Game.Link.X;
                State.Stairs.targetY = Game.Link.Y + 0x10;
                State.Stairs.scrollDir = Direction.Down;
                State.Stairs.playerSpeed = 0x40;
                State.Stairs.playerFraction = 0;

                State.Stairs.substate = StairsState.Substates.WalkCave;
                Game.Sound.PlayEffect(SoundEffect.Stairs);
            }
            else
            {
                State.Stairs.substate = StairsState.Substates.Walk;
            }
        }
        else if (State.Stairs.substate == StairsState.Substates.Walk)
        {
            if (IsOverworld())
            {
                var owRoomAttrs = CurrentOWRoomAttrs;
                var cave = owRoomAttrs.GetCaveId();

                if (cave <= 9)
                    GotoLoadLevel(cave);
                else
                    GotoPlayCave();
            }
            else
            {
                GotoPlayCellar();
            }
        }
        else if (State.Stairs.substate == StairsState.Substates.WalkCave)
        {
            if (Game.Link.X == State.Stairs.targetX
               && Game.Link.Y == State.Stairs.targetY)
            {
                var owRoomAttrs = CurrentOWRoomAttrs;
                var cave = owRoomAttrs.GetCaveId();

                if (cave <= 9)
                    GotoLoadLevel(cave);
                else
                    GotoPlayCave();
            }
            else
            {
                MovePlayer(State.Stairs.scrollDir, State.Stairs.playerSpeed, ref State.Stairs.playerFraction);
                Game.Link.Animator.Advance();
            }
        }
    }

    private void DrawStairsState()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        DrawRoomNoObjects(State.Stairs.playerPriority);
    }

    private void GotoPlayCellar()
    {
        State.PlayCellar.substate = PlayCellarState.Substates.Start;
        State.PlayCellar.playerPriority = SpritePriority.None;

        curMode = GameMode.InitPlayCellar;
    }

    private void UpdatePlayCellar()
    {
        sPlayCellarFuncs[(int)State.PlayCellar.substate]();
    }

    private void UpdatePlayCellar_Start()
    {
        State.PlayCellar.substate = PlayCellarState.Substates.FadeOut;
        State.PlayCellar.fadeTimer = 11;
        State.PlayCellar.fadeStep = 0;
    }

    private void UpdatePlayCellar_FadeOut()
    {
        if (State.PlayCellar.fadeTimer == 0)
        {
            for (var i = 0; i < LevelInfoBlock.FadePals; i++)
            {
                var step = State.PlayCellar.fadeStep;
                Graphics.SetPaletteIndexed((Palette)i + 2, infoBlock.OutOfCellarPalette(step, i));
            }
            Graphics.UpdatePalettes();
            State.PlayCellar.fadeTimer = 9;
            State.PlayCellar.fadeStep++;

            if (State.PlayCellar.fadeStep == LevelInfoBlock.FadeLength)
                State.PlayCellar.substate = PlayCellarState.Substates.LoadRoom;
        }
        else
        {
            State.PlayCellar.fadeTimer--;
        }
    }

    private void UpdatePlayCellar_LoadRoom()
    {
        var roomId = FindCellarRoomId(curRoomId, out var isLeft);

        if (roomId >= 0)
        {
            var x = isLeft ? 0x30 : 0xC0;

            LoadRoom(roomId, 0);

            Game.Link.X = x;
            Game.Link.Y = 0x44;
            Game.Link.Facing = Direction.Down;

            State.PlayCellar.targetY = 0x60;
            State.PlayCellar.substate = PlayCellarState.Substates.FadeIn;
            State.PlayCellar.fadeTimer = 35;
            State.PlayCellar.fadeStep = 3;
        }
        else
        {
            GotoPlay();
        }
    }

    private void UpdatePlayCellar_FadeIn()
    {
        if (State.PlayCellar.fadeTimer == 0)
        {
            for (var i = 0; i < LevelInfoBlock.FadePals; i++)
            {
                var step = State.PlayCellar.fadeStep;
                Graphics.SetPaletteIndexed((Palette)i + 2, infoBlock.InCellarPalette(step, i));
            }
            Graphics.UpdatePalettes();
            State.PlayCellar.fadeTimer = 9;
            State.PlayCellar.fadeStep--;

            if (State.PlayCellar.fadeStep < 0)
                State.PlayCellar.substate = PlayCellarState.Substates.Walk;
        }
        else
        {
            State.PlayCellar.fadeTimer--;
        }
    }

    private void UpdatePlayCellar_Walk()
    {
        State.PlayCellar.playerPriority = SpritePriority.AboveBg;

        if (Game.Link.Y == State.PlayCellar.targetY)
        {
            fromUnderground = 1;
            GotoPlay(PlayState.RoomType.Cellar);
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
        DrawRoomNoObjects(State.PlayCellar.playerPriority);
    }

    private void GotoLeaveCellar()
    {
        State.LeaveCellar.substate = LeaveCellarState.Substates.Start;

        curMode = GameMode.LeaveCellar;
    }

    private void UpdateLeaveCellar()
    {
        sLeaveCellarFuncs[(int)State.LeaveCellar.substate]();
    }

    private void UpdateLeaveCellar_Start()
    {
        if (IsOverworld())
        {
            State.LeaveCellar.substate = LeaveCellarState.Substates.Wait;
            State.LeaveCellar.timer = 29;
        }
        else
        {
            State.LeaveCellar.substate = LeaveCellarState.Substates.FadeOut;
            State.LeaveCellar.fadeTimer = 11;
            State.LeaveCellar.fadeStep = 0;
        }
    }

    private void UpdateLeaveCellar_FadeOut()
    {
        if (State.LeaveCellar.fadeTimer == 0)
        {
            for (var i = 0; i < LevelInfoBlock.FadePals; i++)
            {
                var step = State.LeaveCellar.fadeStep;
                Graphics.SetPaletteIndexed((Palette)i + 2, infoBlock.InCellarPalette(step, i));
            }
            Graphics.UpdatePalettes();
            State.LeaveCellar.fadeTimer = 9;
            State.LeaveCellar.fadeStep++;

            if (State.LeaveCellar.fadeStep == LevelInfoBlock.FadeLength)
                State.LeaveCellar.substate = LeaveCellarState.Substates.LoadRoom;
        }
        else
        {
            State.LeaveCellar.fadeTimer--;
        }
    }

    private void UpdateLeaveCellar_LoadRoom()
    {
        var uwRoomAttrs = CurrentUWRoomAttrs;
        int nextRoomId;

        if (Game.Link.X < 0x80)
            nextRoomId = uwRoomAttrs.GetLeftCellarExitRoomId();
        else
            nextRoomId = uwRoomAttrs.GetRightCellarExitRoomId();

        LoadRoom(nextRoomId, 0);

        Game.Link.X = 0x60;
        Game.Link.Y = 0xA0;
        Game.Link.Facing = Direction.Down;

        State.LeaveCellar.substate = LeaveCellarState.Substates.FadeIn;
        State.LeaveCellar.fadeTimer = 35;
        State.LeaveCellar.fadeStep = 3;
    }

    private void UpdateLeaveCellar_FadeIn()
    {
        if (State.LeaveCellar.fadeTimer == 0)
        {
            for (var i = 0; i < LevelInfoBlock.FadePals; i++)
            {
                var step = State.LeaveCellar.fadeStep;
                Graphics.SetPaletteIndexed((Palette)i + 2, infoBlock.OutOfCellarPalette(step, i));
            }
            Graphics.UpdatePalettes();
            State.LeaveCellar.fadeTimer = 9;
            State.LeaveCellar.fadeStep--;

            if (State.LeaveCellar.fadeStep < 0)
                State.LeaveCellar.substate = LeaveCellarState.Substates.Walk;
        }
        else
        {
            State.LeaveCellar.fadeTimer--;
        }
    }

    private void UpdateLeaveCellar_Walk()
    {
        GotoEnter(Direction.None);
    }

    private void UpdateLeaveCellar_Wait()
    {
        if (State.LeaveCellar.timer == 0)
        {
            State.LeaveCellar.substate = LeaveCellarState.Substates.LoadOverworldRoom;
        }
        else
        {
            State.LeaveCellar.timer--;
        }
    }

    private void UpdateLeaveCellar_LoadOverworldRoom()
    {
        for (var i = 0; i < 2; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i + 2, infoBlock.GetPalette(i + 2));
        }
        Graphics.UpdatePalettes();

        LoadRoom(curRoomId, 0);
        SetPlayerExitPosOW(curRoomId);
        GotoEnter(Direction.None);
        Game.Link.Facing = Direction.Down;
    }

    private void DrawLeaveCellar()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);

        if (State.LeaveCellar.substate == LeaveCellarState.Substates.Start)
        {
        }
        else if (State.LeaveCellar.substate is LeaveCellarState.Substates.Wait or LeaveCellarState.Substates.LoadOverworldRoom)
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
        State.PlayCave.substate = PlayCaveState.Substates.Start;

        curMode = GameMode.InitPlayCave;
    }

    private void UpdatePlayCave()
    {
        sPlayCaveFuncs[(int)State.PlayCave.substate]();
    }

    private void UpdatePlayCave_Start()
    {
        State.PlayCave.substate = PlayCaveState.Substates.Wait;
        State.PlayCave.timer = 27;
    }

    private void UpdatePlayCave_Wait()
    {
        if (State.PlayCave.timer == 0)
            State.PlayCave.substate = PlayCaveState.Substates.LoadRoom;
        else
            State.PlayCave.timer--;
    }

    private void UpdatePlayCave_LoadRoom()
    {
        var paletteSet = extraData.GetItem<PaletteSet>(Extra.CavePalettes);
        Cave caveLayout;

        if (FindSparseFlag(Sparse.Shortcut, curRoomId))
            caveLayout = Cave.Shortcut;
        else
            caveLayout = Cave.Items;

        LoadCaveRoom(caveLayout);

        State.PlayCave.substate = PlayCaveState.Substates.Walk;
        State.PlayCave.targetY = 0xD5;

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
        if (Game.Link.Y == State.PlayCave.targetY)
        {
            fromUnderground = 1;
            GotoPlay(PlayState.RoomType.Cave);
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

        if (State.PlayCave.substate is PlayCaveState.Substates.Wait or PlayCaveState.Substates.LoadRoom)
        {
            ClearScreen();
        }
        else if (State.PlayCave.substate == PlayCaveState.Substates.Walk)
        {
            DrawRoomNoObjects();
        }
    }

    public void GotoDie()
    {
        State.Death.Substate = DeathState.Substates.Start;

        curMode = GameMode.Death;
    }

    private void UpdateDie()
    {
        // ORIGINAL: Some of these are handled with object timers.
        if (State.Death.Timer > 0)
            State.Death.Timer--;

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

    private static readonly byte[][] deathRedPals = new[]
    {
        new byte[] {0x0F, 0x17, 0x16, 0x26 },
        new byte[] {0x0F, 0x17, 0x16, 0x26 },
    };

    private void UpdateDie_Wait1()
    {
        // TODO: the last 2 frames make the whole play area use palette 3.

        if (State.Death.Timer == 0)
        {
            SetLevelPalettes(deathRedPals);

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

                var dirs = new[]
                {
                    Direction.Down,
                    Direction.Left,
                    Direction.Up,
                    Direction.Right
                };

                var dir = dirs[State.Death.Step & 3];
                Game.Link.Facing = dir;
            }
        }
    }

    private void UpdateDie_Fade()
    {
        if (State.Death.Step == 0)
        {
            State.Death.Substate = DeathState.Substates.GrayLink;
        }
        else
        {
            if (State.Death.Timer == 0)
            {
                State.Death.Timer = 10;
                State.Death.Step--;

                var seq = 3 - State.Death.Step;

                SetLevelPalettes(infoBlock.DeathPalettes(seq));
            }
        }
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
            profile.Deaths++;
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
        }
        else
        {
            var gameOver = new byte[] { 0x10, 0x0A, 0x16, 0x0E, 0x24, 0x18, 0x1F, 0x0E, 0x1B };
            GlobalFunctions.DrawString(gameOver, 0x60, 0x90, 0);
        }
    }

    private void GotoContinueQuestion()
    {
        State.Continue.Substate = ContinueState.Substates.Start;
        State.Continue.SelectedIndex = 0;

        curMode = GameMode.ContinueQuestion;
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
                    fromUnderground = 2;
                    Game.Link = new Link(Game);
                    profile.Hearts = PlayerProfile.GetMaxHeartsValue(PlayerProfile.DefaultHearts);
                    GotoUnfurl(true);
                }
                else if (State.Continue.SelectedIndex == 1)
                {
                    SaveFolder.WriteProfile(profileSlot, profile);
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
        var strs = new[] { "Continue", "Save", "Retry" };

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
        var summaries = SaveFolder.ReadSummaries();
        GotoFileMenu(summaries);
    }

    private void GotoFileMenu(ProfileSummarySnapshot summaries)
    {
        nextGameMenu = new GameMenu(Game, summaries);
        curMode = GameMode.GameMenu;
    }

    private void GotoRegisterMenu(ProfileSummarySnapshot summaries)
    {
        nextGameMenu = new RegisterMenu(Game, summaries);
        curMode = GameMode.Register;
    }

    private void GotoEliminateMenu(ProfileSummarySnapshot summaries)
    {
        nextGameMenu = new EliminateMenu(Game, summaries);
        curMode = GameMode.Elimination;
    }

    private void UpdateGameMenu() => gameMenu.Update();
    private void UpdateRegisterMenu() => gameMenu.Update();
    private void UpdateEliminateMenu() => gameMenu.Update();
    private void DrawGameMenu() => gameMenu?.Draw();

    private int FindCellarRoomId(int mainRoomId, out bool isLeft)
    {
        isLeft = false;
        for (var i = 0; i < LevelInfoBlock.LevelCellarCount; i++)
        {
            var cellarRoomId = infoBlock.CellarRoomIds[i];

            if (cellarRoomId >= 0x80)
                break;

            var uwRoomAttrs = GetUWRoomAttrs(cellarRoomId);
            if (mainRoomId == uwRoomAttrs.GetLeftCellarExitRoomId())
            {
                isLeft = true;
                return cellarRoomId;
            }
            else if (mainRoomId == uwRoomAttrs.GetRightCellarExitRoomId())
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
            Game.Link.Draw();

        DrawRoom();

        if (playerPriority == SpritePriority.AboveBg)
            Game.Link.Draw();

        if (IsUWMain(curRoomId))
            DrawDoors(curRoomId, true, 0, 0);
    }

    private void NoneTileAction(int row, int col, TileInteraction interaction)
    {
        // Nothing to do. Should never be called.
    }

    private void PushTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load)
            return;

        var rock = new RockObj(Game, col * TileWidth, TileMapBaseY + row * TileHeight);
        SetBlockObj(rock);
    }

    private void BombTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load)
            return;

        if (GotSecret())
        {
            SetMob(row, col, BlockObjType.Mob_Cave);
        }
        else
        {
            var rockWall = new RockWallActor(Game, col * TileWidth, TileMapBaseY + row * TileHeight);
            SetBlockObj(rockWall);
        }
    }

    private void BurnTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load)
            return;

        if (GotSecret())
        {
            SetMob(row, col, BlockObjType.Mob_Stairs);
        }
        else
        {
            var tree = new TreeActor(Game, col * TileWidth, TileMapBaseY + row * TileHeight);
            SetBlockObj(tree);
        }
    }

    private void HeadstoneTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load)
            return;

        var headstone = new HeadstoneObj(Game, col * TileWidth, TileMapBaseY + row * TileHeight);
        SetBlockObj(headstone);
    }

    private void LadderTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Touch)
            return;

        Debug.WriteLine("Touch water: {0}, {1}", row, col);
    }

    private void RaftTileAction(int row, int col, TileInteraction interaction)
    {
        // TODO: instantiate the Dock here on Load interaction, and set its position.

        if (interaction != TileInteraction.Cover)
            return;

        Debug.WriteLine("Cover dock: {0}, {1}", row, col);

        if (Game.World.GetItem(ItemSlot.Raft) == 0)
            return;
        if (!FindSparseFlag(Sparse.Dock, curRoomId))
            return;
    }

    private void CaveTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Cover)
            return;

        if (IsOverworld())
        {
            var behavior = GetTileBehavior(row, col);
            GotoStairs(behavior);
        }

        Debug.WriteLine("Cover cave: {0}, {1}", row, col);
    }

    private void StairsTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Cover)
            return;

        if (GetMode() == GameMode.Play)
            GotoStairs(TileBehavior.Stairs);

        Debug.WriteLine("Cover stairs: {0}, {1}", row, col);
    }

    public void GhostTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction == TileInteraction.Push) Debug.WriteLine("Push headstone: {0}, {1}", row, col);

        CommonMakeObjectAction(ObjType.FlyingGhini, row, col, interaction, ref ghostCount, ghostCells);
    }

    public void ArmosTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction == TileInteraction.Push) Debug.WriteLine("Push armos: {0}, {1}", row, col);

        CommonMakeObjectAction(ObjType.Armos, row, col, interaction, ref armosCount, armosCells);
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
            var map = tileMaps[curTileMapIndex];
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
            var obj = objects[i];
            if (obj == null || obj.ObjType != type)
                continue;

            var objCol = obj.X / TileWidth;
            var objRow = obj.Y / TileHeight;

            if (objCol == col && objRow == row)
            {
                return;
            }
        }

        var freeSlot = FindEmptyMonsterSlot();
        if (freeSlot >= 0)
        {
            var obj = Actor.FromType(type, Game, x, y);
            objects[(int)freeSlot] = obj;
            obj.ObjTimer = 0x40;
        }
    }

    public void BlockTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load)
            return;

        var block = new BlockObj(Game, col * TileWidth, TileMapBaseY + row * TileHeight);
        SetBlockObj(block);
    }

    public void DoorTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Push)
            return;

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
                    LeaveRoom(player.Facing, curRoomId);
                    player.Stop();
                }
                break;

            case DoorType.Bombable:
                if (GetEffectiveDoorState(player.MovingDirection))
                {
                    LeaveRoom(player.Facing, curRoomId);
                    player.Stop();
                }
                break;

            case DoorType.Key:
            case DoorType.Key2:
                if (triggeredDoorDir == Direction.None)
                {
                    var canOpen = false;

                    if (Game.World.GetItem(ItemSlot.MagicKey) != 0)
                    {
                        canOpen = true;
                    }
                    else if (Game.World.GetItem(ItemSlot.Keys) != 0)
                    {
                        canOpen = true;
                        Game.World.DecrementItem(ItemSlot.Keys);
                    }

                    if (canOpen)
                    {
                        // $8ADA
                        triggeredDoorDir = player.MovingDirection;
                        triggeredDoorCmd = 8;
                    }
                }
                break;
        }
    }
}