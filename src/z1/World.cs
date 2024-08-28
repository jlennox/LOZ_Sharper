using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;
using z1.Actors;
using z1.IO;
using z1.UI;

namespace z1;

internal enum DoorType { Open, None, FalseWall, FalseWall2, Bombable, Key, Key2, Shutter }
internal enum TileInteraction { Load, Push, Touch, Cover }
internal enum SpritePriority { None, AboveBg, BelowBg }
internal enum SubmenuState { IdleClosed, StartOpening, EndOpening = 7, IdleOpen, StartClose }

[Flags]
internal enum Direction
{
    None = 0,
    Right = 1,
    Left = 2,
    Down = 4,
    Up = 8,
    DirectionMask = 0x0F,
    ShoveMask = 0x80, // JOE: TODO: Not sure what this is.
    FullMask = 0xFF,
    VerticalMask = Down | Up,
    HorizontalMask = Left | Right,
    OppositeVerticals = VerticalMask,
    OppositeHorizontals = HorizontalMask,
}

internal enum GameMode
{
    Demo,
    GameMenu,
    LoadLevel,
    Unfurl,
    Enter,
    Play,
    Leave,
    Scroll,
    ContinueQuestion,
    PlayCellar,
    LeaveCellar,
    PlayCave,
    PlayShortcuts,
    UnknownD__,
    Register,
    Elimination,
    Stairs,
    Death,
    EndLevel,
    WinGame,

    InitPlayCellar,
    InitPlayCave,

    Max,
}

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
        row = Math.Max(0, Math.Min(row, World.Rows - 1));
        col = Math.Max(0, Math.Min(col, World.Columns - 1));

        return (TileBehavior)_tileBehaviors[row * World.Columns + col];
    }
}

internal sealed unsafe partial class World
{
    public const int LevelGroups = 3;

    public const int MobColumns = 16;
    public const int Rows = 22;
    public const int Columns = 32;
    public const int MobTileWidth = 16;
    public const int MobTileHeight = 16;
    public const int TileWidth = 8;
    public const int TileHeight = 8;
    public const int TileMapWidth = Columns * TileWidth;

    public const int WorldLimitTop = TileMapBaseY;
    public const int WorldLimitBottom = WorldLimitTop + TileMapHeight;
    public const int WorldMidX = WorldLimitLeft + TileMapWidth / 2;
    private const int WorldLimitLeft = 0;
    private const int WorldLimitRight = TileMapWidth;
    public const int WorldWidth = 16;
    public const int WorldHeight = 8;

    private const int BaseRows = 8;
    private const int TileMapHeight = Rows * TileHeight;
    private const int TileMapBaseY = 64;
    private const int Doors = 4;

    private const int StartX = 0x78;
    private const int FirstCaveIndex = 0x10;
    private const int TriforcePieceX = 0x78;

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

    internal enum Secret { None, FoesDoor, Ringleader, LastBoss, BlockDoor, BlockStairs, MoneyOrLife, FoesItem }

    private delegate void LoadMobDelegate(ref TileMap map, int row, int col, int mobIndex);
    private enum PauseState { Unpaused, Paused, FillingHearts }
    private enum Cave { Items = 0x79, Shortcut = 0x7A, }
    private enum TileScheme { Overworld, UnderworldMain, UnderworldCellar }
    private enum UniqueRoomIds { TopRightOverworldSecret = 0x0F }

    private static readonly DebugLog _traceLog = new(nameof(World), DebugLogDestination.DebugBuildsOnly);
    private static readonly DebugLog _log = new(nameof(World), DebugLogDestination.DebugBuildsOnly);

    public Game Game { get; }
    public Link Player => Game.Link;
    public int CurRoomId;
    public Point CurrentRoom => new(CurRoomId % WorldWidth, CurRoomId / WorldWidth);
    public SubmenuType Menu;
    public int RoomObjCount;           // 34E
    public Actor? RoomObj;              // 35F
    public bool EnablePersonFireballs;
    public bool SwordBlocked;           // 52E
    public byte WhirlwindTeleporting;   // 522
    public int CurObjSlot;
    public ObjectSlot CurObjectSlot // JOE: TODO: I hate this :)
    {
        get => (ObjectSlot)CurObjSlot;
        set => CurObjSlot = (int)value;
    }
    public Direction DoorwayDir;         // 53
    public int FromUnderground;    // 5A
    public int ActiveShots;        // 34C
    public int RecorderUsed;       // 51B
    public bool CandleUsed;         // 513
    public PlayerProfile? Profile { get; private set; }
    public PlayerProfile GetProfile() => Profile ?? throw new Exception("Profile missing.");

    private LevelDirectory _directory;
    private LevelInfoBlock _infoBlock;
    private RoomCols[] _roomCols = new RoomCols[UniqueRooms];
    private TableResource<byte> _colTables;
    private readonly TileMap[] _tileMaps = [new(), new(), new()];
    private RoomAttrs[] _roomAttrs = new RoomAttrs[Rooms];
    private int _curTileMapIndex;
    private byte[] _tileAttrs = new byte[MobTypes];
    private byte[] _tileBehaviors = new byte[TileTypes];
    private TableResource<byte> _sparseRoomAttrs;
    private TableResource<byte> _extraData;
    private TableResource<byte> _objLists;
    private TableResource<byte> _textTable;
    private ListResource<byte> _primaryMobs;
    private ListResource<byte> _secondaryMobs;
    private LoadMobDelegate _loadMobFunc;

    private int _rowCount;
    private int _colCount;
    private int _startRow;
    private int _startCol;
    private int _tileTypeCount;
    // JOE: TODO: Move to rect.
    public int MarginRight;
    public int MarginLeft;
    public int MarginBottom;
    public int MarginTop;
    private SKBitmap? _wallsBmp;
    private SKBitmap? _doorsBmp;

    private GameMode _lastMode;
    private GameMode _curMode;
    private StatusBar _statusBar;
    private CreditsType? _credits;
    private TextBox? _textBox1;
    private TextBox? _textBox2;
    private Menu? _gameMenu;
    private Menu? _nextGameMenu;

    private readonly WorldState _state = new();
    private int _curColorSeqNum;
    private int _darkRoomFadeStep;
    private int _curMazeStep;
    private int _spotIndex;
    private int _tempShutterRoomId;
    private Direction _tempShutterDoorDir;
    private bool _tempShutters;
    private bool _prevRoomWasCellar;
    private int _savedOWRoomId;
    private int _edgeX;
    private int _edgeY;
    private int _nextRoomHistorySlot;    // 620

    private byte _worldKillCycle;         // 52A
    private byte _worldKillCount;         // 627
    private byte _helpDropCounter;        // 50
    private byte _helpDropValue;          // 51
    private int _roomKillCount;          // 34F
    private bool _roomAllDead;            // 34D
    private bool _madeRoomItem;
    private byte _teleportingRoomIndex;   // 523
    private PauseState _pause;                  // E0
    private SubmenuState _submenu;                // E1
    private int _submenuOffsetY;         // EC
    private bool _statusBarVisible;
    private readonly int[] _levelKillCounts = new int[(int)LevelBlock.Rooms];
    private readonly byte[] _roomHistory = new byte[RoomHistoryLength];

    private bool _giveFakePlayerPos;
    private Point _fakePlayerPos;

    private readonly Actor?[] _objects = new Actor[(int)ObjectSlot.MaxObjects];
    private readonly Queue<Actor> _objectsToDelete = new();
    private readonly int[] _objectTimers = new int[(int)ObjectSlot.MaxObjects];
    private int _longTimer;
    private readonly int[] _stunTimers = new int[(int)ObjectSlot.MaxObjects];
    private readonly byte[] _placeholderTypes = new byte[(int)ObjectSlot.MaxObjects];

    private int _triggeredDoorCmd;   // 54
    private Direction _triggeredDoorDir;   // 55

    // JOE: TODO: ActiveShots doesn't need to be reference counted anymore and should be based on the object table.
    // Though note that ones owned by Link should be excluded.
    private bool _triggerShutters;    // 4CE
    private bool _summonedWhirlwind;  // 508
    private bool _powerTriforceFanfare;   // 509
    // private Direction _shuttersPassedDirs; // 519 // JOE: NOTE: Delete this, it's unused.
    private bool _brightenRoom;       // 51E
    private RoomMap _currentRoomMap = RoomMap.Overworld;
    private int _ghostCount;
    private int _armosCount;
    private readonly Cell[] _ghostCells = Cell.MakeMobPatchCell();
    private readonly Cell[] _armosCells = Cell.MakeMobPatchCell();

    private UWRoomAttrs CurrentUWRoomAttrs => GetUWRoomAttrs(CurRoomId);
    private OWRoomAttrs CurrentOWRoomAttrs => GetOWRoomAttrs(CurRoomId);

    private UWRoomAttrs GetUWRoomAttrs(int roomId) => _roomAttrs[roomId];
    private OWRoomAttrs GetOWRoomAttrs(int roomId) => _roomAttrs[roomId];

    public World(Game game)
    {
        Game = game;
        _statusBar = new StatusBar(this);
        Menu = new SubmenuType(game);

        _lastMode = GameMode.Demo;
        _curMode = GameMode.Play;
        _edgeY = 0x40;

        Init();
    }

    private void LoadOpenRoomContext()
    {
        _colCount = 32;
        _rowCount = 22;
        _startRow = 0;
        _startCol = 0;
        _tileTypeCount = 56;
        MarginRight = OWMarginRight;
        MarginLeft = OWMarginLeft;
        MarginBottom = OWMarginBottom;
        MarginTop = OWMarginTop;
    }

    private void LoadClosedRoomContext()
    {
        _colCount = 24;
        _rowCount = 14;
        _startRow = 4;
        _startCol = 4;
        _tileTypeCount = 9;
        MarginRight = UWMarginRight;
        MarginLeft = UWMarginLeft;
        MarginBottom = UWMarginBottom;
        MarginTop = UWMarginTop;
    }

    private void LoadMapResourcesFromDirectory(int uniqueRoomCount)
    {
        _roomCols = ListResource<RoomCols>.LoadList(_directory.RoomCols.ToAsset(), uniqueRoomCount).ToArray();
        _colTables = TableResource<byte>.Load(_directory.ColTables.ToAsset());
        _tileAttrs = ListResource<byte>.LoadList(_directory.TileAttrs.ToAsset(), _tileTypeCount).ToArray();

        Graphics.LoadTileSheet(TileSheet.Background, _directory.TilesImage.ToAsset());
    }

    private void LoadOverworldContext()
    {
        _prevRoomWasCellar = false;
        LoadOpenRoomContext();
        LoadMapResourcesFromDirectory(124);
        _primaryMobs = ListResource<byte>.Load(new Asset("owPrimaryMobs.list"));
        _secondaryMobs = ListResource<byte>.Load(new Asset("owSecondaryMobs.list"));
        _tileBehaviors = ListResource<byte>.LoadList(new Asset("owTileBehaviors.dat"), TileTypes).ToArray();
    }

    private void LoadUnderworldContext()
    {
        _prevRoomWasCellar = false;
        LoadClosedRoomContext();
        LoadMapResourcesFromDirectory(64);
        _primaryMobs = ListResource<byte>.Load(new Asset("uwPrimaryMobs.list"));
        _tileBehaviors = ListResource<byte>.LoadList(new Asset("uwTileBehaviors.dat"), TileTypes).ToArray();
    }

    private void LoadCellarContext()
    {
        _prevRoomWasCellar = true;
        LoadOpenRoomContext();

        _roomCols = ListResource<RoomCols>.LoadList("underworldCellarRoomCols.dat", 2).ToArray();
        _colTables = TableResource<byte>.Load("underworldCellarCols.tab");

        _tileAttrs = ListResource<byte>.LoadList("underworldCellarTileAttrs.dat", _tileTypeCount).ToArray();

        Graphics.LoadTileSheet(TileSheet.Background, new Asset("underworldTiles.png"));

        _primaryMobs = ListResource<byte>.Load("uwCellarPrimaryMobs.list");
        _secondaryMobs = ListResource<byte>.Load("uwCellarSecondaryMobs.list");
        _tileBehaviors = ListResource<byte>.LoadList("uwTileBehaviors.dat", TileTypes).ToArray();
    }

    private void LoadLevel(int level)
    {
        var profile = GetProfile();
        var levelDirName = $"levelDir_{profile.Quest}_{level}.dat";

        _directory = ListResource<LevelDirectory>.LoadSingle(levelDirName);
        _infoBlock = ListResource<LevelInfoBlock>.LoadSingle(_directory.LevelInfoBlock.ToString());

        _wallsBmp?.Dispose();
        _wallsBmp = null;
        _doorsBmp?.Dispose();
        _doorsBmp = null;

        _tempShutterRoomId = 0;
        _tempShutterDoorDir = 0;
        _tempShutters = false;
        _darkRoomFadeStep = 0;
        Array.Clear(_levelKillCounts);
        Array.Clear(_roomHistory);
        WhirlwindTeleporting = 0;

        if (level == 0)
        {
            LoadOverworldContext();
            _currentRoomMap = RoomMap.Overworld;
        }
        else
        {
            LoadUnderworldContext();
            _wallsBmp = _directory.Extra2.ToAsset().DecodeSKBitmapTileData();
            _doorsBmp = _directory.Extra3.ToAsset().DecodeSKBitmapTileData();
            _currentRoomMap = level < 7 ? RoomMap.UnderworldA : RoomMap.UnderworldB;

            foreach (var tileMap in _tileMaps)
            {
                for (var x = 0; x < TileMap.Size; x++)
                {
                    tileMap.Refs(x) = (byte)BlockObjType.TileWallEdge;
                }
            }
        }

        Graphics.LoadTileSheet(TileSheet.PlayerAndItems, _directory.PlayerImage.ToAsset(), _directory.PlayerSheet.ToAsset());
        Graphics.LoadTileSheet(TileSheet.Npcs, _directory.NpcImage.ToAsset(), _directory.NpcSheet.ToAsset());

        if (!_directory.BossImage.IsNull)
        {
            Graphics.LoadTileSheet(TileSheet.Boss, _directory.BossImage.ToAsset(), _directory.BossSheet.ToAsset());
        }

        _roomAttrs = ListResource<RoomAttrs>.LoadList(_directory.RoomAttrs.ToAsset(), Rooms).ToArray();
        _extraData = TableResource<byte>.Load(_directory.LevelInfoEx.ToAsset());
        _objLists = TableResource<byte>.Load(_directory.ObjLists.ToAsset());
        _sparseRoomAttrs = TableResource<byte>.Load(_directory.Extra1.ToAsset());

        var facing = Game.Link?.Facing ?? Direction.Up;

        Game.Link = new Link(Game, facing);

        // Replace room attributes, if in second quest.

        if (level == 0 && profile.Quest == 1)
        {
            var pReplacement = _sparseRoomAttrs.GetItems<byte>(Sparse.RoomReplacement);
            int replacementCount = pReplacement[0];
            var sparseAttr = MemoryMarshal.Cast<byte, SparseRoomAttr>(pReplacement[2..]); // JOE: Go until replacementCount * sizeof(SparseRoomAttr) ??

            for (var i = 0; i < replacementCount; i++)
            {
                int roomId = sparseAttr[i].roomId;
                _roomAttrs[roomId] = sparseAttr[i].attrs;
            }
        }
    }

    private void Init()
    {
        var sysPal = ListResource<int>.LoadList(new Asset("pal.dat"), Global.SysPaletteLength).ToArray();
        Graphics.LoadSystemPalette(sysPal);

        Graphics.LoadTileSheet(TileSheet.Font, new Asset("font.png"));
        Graphics.LoadTileSheet(TileSheet.PlayerAndItems, new Asset("playerItem.png"), new Asset("playerItemsSheet.tab"));

        _textTable = TableResource<byte>.Load(new Asset("text.tab"));

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

        if (_lastMode != mode)
        {
            if (IsPlaying(_lastMode) && mode != GameMode.WinGame)
            {
                CleanUpRoomItems();
                Graphics.DisableGrayscale();
                if (mode != GameMode.Unfurl)
                {
                    OnLeavePlay();
                    Game.Link?.Stop();
                }
            }

            _lastMode = mode;

            _gameMenu = _nextGameMenu;
            _nextGameMenu = null;
        }

        GetUpdateFunction(_curMode)!();
    }

    public void Draw()
    {
        if (_statusBarVisible)
        {
            _statusBar.Draw(_submenuOffsetY);
        }

        GetDrawFunction(_curMode)!();
    }

    private bool IsButtonPressing(GameButton button) => Game.Input.IsButtonPressing(button);
    private bool IsAnyButtonPressing(GameButton a, GameButton b) => Game.Input.IsAnyButtonPressing(a, b);
    private void DrawRoom() => DrawMap(CurRoomId, _curTileMapIndex, 0, 0);
    public void PauseFillHearts() => _pause = PauseState.FillingHearts;
    public void LeaveRoom(Direction dir, int roomId) => GotoLeave(dir, roomId);
    public void LeaveCellar() => GotoLeaveCellar();

    public void LeaveCellarByShortcut(int targetRoomId)
    {
        CurRoomId = targetRoomId;
        TakeShortcut();
        LeaveCellar();
    }

    public void UnfurlLevel() => GotoUnfurl();
    private bool IsPlaying() => IsPlaying(_curMode);
    private static bool IsPlaying(GameMode mode) => mode is GameMode.Play or GameMode.PlayCave or GameMode.PlayCellar or GameMode.PlayShortcuts;
    private bool IsPlayingCave() => GetMode() == GameMode.PlayCave;

    public GameMode GetMode() => _curMode switch
    {
        GameMode.InitPlayCave => GameMode.PlayCave,
        GameMode.InitPlayCellar => GameMode.PlayCellar,
        _ => _curMode
    };

    public Point GetObservedPlayerPos() => _fakePlayerPos;
    public LadderActor? GetLadder() => GetObject<LadderActor>(ObjectSlot.Ladder);
    public void SetLadder(LadderActor? ladder) => SetOnlyObject(ObjectSlot.Ladder, ladder);

    public void UseRecorder()
    {
        Game.Sound.PushSong(SongId.Recorder);
        _objectTimers[(int)ObjectSlot.FluteMusic] = 0x98;

        if (!IsOverworld())
        {
            RecorderUsed = 1;
            return;
        }

        if (IsPlaying() && _state.Play.RoomType == RoomType.Regular)
        {
            ReadOnlySpan<byte> roomIds = [0x42, 0x06, 0x29, 0x2B, 0x30, 0x3A, 0x3C, 0x58, 0x60, 0x6E, 0x72];

            var i = roomIds.IndexOf((byte)CurRoomId);
            // The first one is level 7 entrance, the others are second quest only.
            var foundSecret = i switch
            {
                0 => Profile.Quest == 0,
                > 1 => Profile.Quest != 0,
                _ => false
            };

            _traceLog.Write($"UseRecorder: {CurRoomId:X2}, i:{i}, foundSecret:{foundSecret}");

            if (foundSecret)
            {
                MakeFluteSecret();
            }
            else
            {
                SummonWhirlwind();
            }
        }
    }

    private void SummonWhirlwind()
    {
        if (!_summonedWhirlwind
            && WhirlwindTeleporting == 0
            && IsOverworld()
            && IsPlaying()
            && _state.Play.RoomType == RoomType.Regular
            && GetItem(ItemSlot.TriforcePieces) != 0)
        {
            var slot = FindEmptyMonsterSlot();
            if (slot != ObjectSlot.NoneFound)
            {
                ReadOnlySpan<byte> teleportRoomIds = [0x36, 0x3B, 0x73, 0x44, 0x0A, 0x21, 0x41, 0x6C];

                var whirlwind = new WhirlwindActor(Game, 0, Game.Link.Y);
                SetObject(slot, whirlwind);

                _summonedWhirlwind = true;
                _teleportingRoomIndex = GetNextTeleportingRoomIndex();
                whirlwind.SetTeleportPrevRoomId(teleportRoomIds[_teleportingRoomIndex]);
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

        if (!_state.Play.UncoveredRecorderSecret && FindSparseFlag(Sparse.Recorder, CurRoomId))
        {
            _state.Play.UncoveredRecorderSecret = true;
            _state.Play.AnimatingRoomColors = true;
            _state.Play.Timer = 88;
        }
    }

    private TileBehavior GetTileBehavior(int row, int col)
    {
        return _tileMaps[_curTileMapIndex].AsBehaviors(row, col);
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
        _loadMobFunc(ref _tileMaps[_curTileMapIndex], row, col, (byte)mobIndex); // JOE: FIXME: BlockObjTypes

        for (var r = row; r < row + 2; r++)
        {
            for (var c = col; c < col + 2; c++)
            {
                var t = _tileMaps[_curTileMapIndex].Refs(r, c);
                _tileMaps[_curTileMapIndex].Behaviors(r, c) = _tileBehaviors[t];
            }
        }

        // TODO: Will we need to run some function to initialize the map object, like in LoadLayout?
    }

    public Palette GetInnerPalette()
    {
        return _roomAttrs[CurRoomId].GetInnerPalette();
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
        return slot == ObjectSlot.Player ? Game.Link : _objects[(int)slot];
    }

    public IEnumerable<T> GetObjects<T>() where T : Actor
    {
        for (var slot = ObjectSlot.Monster1; slot < ObjectSlot.MaxObjects; slot++)
        {
            var obj = GetObject(slot);
            if (obj is T actor) yield return actor;
        }
    }

    public IEnumerable<T> GetMonsters<T>(bool skipStart = false) where T : Actor
    {
        var start = skipStart ? ObjectSlot.Monster1 + 1 : ObjectSlot.Monster1;
        var end = skipStart ? ObjectSlot.Monster1 + 9 : ObjectSlot.MonsterEnd;
        for (var slot = start; slot < end; slot++)
        {
            var obj = GetObject(slot);
            if (obj is T monster) yield return monster;
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

    public void ClearObject(ObjectSlot slot)
    {
        _traceLog.Write($"ClearObject({slot})");
        SetOnlyObject(slot, null);
    }

    public void SetObject(ObjectSlot slot, Actor obj)
    {
        _traceLog.Write($"SetObject({slot}, {obj?.ObjType}); {obj?.X:X2},{obj?.Y:X2}");
        SetOnlyObject(slot, obj);
    }

    public ObjectSlot FindEmptyFireSlot()
    {
        for (var i = ObjectSlot.FirstFire; i < ObjectSlot.LastFire; i++)
        {
            if (_objects[(int)i] == null) return i;
        }
        return ObjectSlot.NoneFound;
    }

    public ref int GetObjectTimer(ObjectSlot slot) => ref _objectTimers[(int)slot];
    public void SetObjectTimer(ObjectSlot slot, int value) => _objectTimers[(int)slot] = value;
    public int GetStunTimer(ObjectSlot slot) => _stunTimers[(int)slot];
    public void SetStunTimer(ObjectSlot slot, int value) => _stunTimers[(int)slot] = value;
    public void PushTile(int row, int col) => InteractTile(row, col, TileInteraction.Push);
    private void TouchTile(int row, int col) => InteractTile(row, col, TileInteraction.Touch);
    public void CoverTile(int row, int col) => InteractTile(row, col, TileInteraction.Cover);

    private void InteractTile(int row, int col, TileInteraction interaction)
    {
        if (row < 0 || col < 0 || row >= Rows || col >= Columns) return;

        var behavior = GetTileBehavior(row, col);
        var behaviorFunc = BehaviorFuncs[(int)behavior];
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
            && _infoBlock.LevelNumber == 0
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
            // JOE: FIXME: Arg. This is a bug in the original C++ but the original C++ is a proper translation
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

        // Upcast to an int, otherwise `fineCol2` being 0xFF will cause a non-terminating loop.
        for (var c = (int)fineCol1; c <= fineCol2; c++)
        {
            var curBehavior = GetTileBehavior(fineRow, c);
            if (curBehavior == TileBehavior.Water && _state.Play.AllowWalkOnWater)
            {
                curBehavior = TileBehavior.GenericWalkable;
            }

            if (curBehavior > behavior)
            {
                behavior = curBehavior;
                hitFineCol = (byte)c;
            }
        }

        return new TileCollision(CollidesTile(behavior), behavior, hitFineCol, fineRow);
    }

    public TileCollision PlayerCoversTile(int x, int y)
    {
        y += 3;

        var behavior = TileBehavior.FirstWalkable;
        var fineRow1 = (y - TileMapBaseY) / 8;
        var fineRow2 = (y + 15 - TileMapBaseY) / 8;
        var fineCol1 = x / 8;
        var fineCol2 = (x + 15) / 8;
        var hitFineCol = fineCol1;
        var hitFineRow = fineRow1;

        for (var r = fineRow1; r <= fineRow2; r++)
        {
            for (var c = fineCol1; c <= fineCol2; c++)
            {
                var curBehavior = GetTileBehavior(r, c);
                if (curBehavior == TileBehavior.Water && _state.Play.AllowWalkOnWater)
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
                    ShowShortcutStairs(CurRoomId, _curTileMapIndex);
                }
            }
        }
        else
        {
            var uwRoomAttrs = CurrentUWRoomAttrs;
            var secret = uwRoomAttrs.GetSecret();

            if (secret == Secret.BlockDoor)
            {
                _triggerShutters = true;
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
                var itemObj = new ItemObjActor(Game, roomItem.Value.AsItemId, true, roomItem.Value.x, roomItem.Value.y);
                SetOnlyObject(ObjectSlot.Item, itemObj);
            }
        }
    }

    public void OnTouchedPowerTriforce()
    {
        _powerTriforceFanfare = true;
        Game.Link.SetState(PlayerState.Paused);
        Game.Link.ObjTimer = 0xC0;

        ReadOnlySpan<byte> palette = [0, 0x0F, 0x10, 0x30];
        Graphics.SetPaletteIndexed(Palette.LevelFgPalette, palette);
        Graphics.UpdatePalettes();
    }

    private void CheckPowerTriforceFanfare()
    {
        if (!_powerTriforceFanfare) return;

        if (Game.Link.ObjTimer == 0)
        {
            _powerTriforceFanfare = false;
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
        var profile = GetProfile();
        if (profile.SelectedItem == 0)
        {
            profile.SelectedItem = ItemSlot.Boomerang;
        }

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

            // JOE: TODO: Make this normal enum constants.
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
        if (GetProfile().Hearts >= 0x100) return;

        Game.Sound.PlayEffect(SoundEffect.LowHp);
    }

    private RoomFlags GetCurrentRoomFlags() => GetRoomFlags(CurRoomId);
    public RoomFlags GetRoomFlags(int roomId) => Profile.GetRoomFlags(_currentRoomMap, roomId);

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
            if (GetRoomFlags(_infoBlock.BossRoomId).ObjectCount == 0)
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
        var pos = _infoBlock.ShortcutPosition[index];
        GetRoomCoord(pos, out var row, out var col);
        SetMob(row * 2, col * 2, BlockObjType.MobStairs);
    }

    private void DrawMap(int roomId, int mapIndex, int offsetX, int offsetY)
    {
        Graphics.Begin();

        var outerPalette = _roomAttrs[roomId].GetOuterPalette();
        var innerPalette = _roomAttrs[roomId].GetInnerPalette();
        var map = _tileMaps[mapIndex];

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

        var endCol = _startCol + _colCount;
        var endRow = _startRow + _rowCount;

        var y = TileMapBaseY + tileOffsetY;

        if (IsUWMain(roomId))
        {
            Graphics.DrawBitmap(
                _wallsBmp,
                0, 0,
                TileMapWidth, TileMapHeight,
                offsetX, TileMapBaseY + offsetY,
                outerPalette, 0);
        }

        for (var r = firstRow; r < lastRow; r++, y += TileHeight)
        {
            if (r < _startRow || r >= endRow) continue;

            var x = tileOffsetX;
            for (var c = firstCol; c < lastCol; c++, x += TileWidth)
            {
                if (c < _startCol || c >= endCol) continue;

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
        var outerPalette = _roomAttrs[roomId].GetOuterPalette();
        var baseY = above ? DoorOverlayBaseY : DoorUnderlayBaseY;
        var uwRoomAttr = GetUWRoomAttrs(roomId);

        for (var i = 0; i < 4; i++)
        {
            var doorDir = i.GetOrdDirection();
            var doorType = uwRoomAttr.GetDoor(i);
            var doorState = GetDoorState(roomId, doorDir);
            if (_tempShutterDoorDir != 0 && roomId == _tempShutterRoomId && doorType == DoorType.Shutter)
            {
                if (doorDir == _tempShutterDoorDir)
                {
                    doorState = true;
                }
            }
            if (doorType == DoorType.Shutter && _tempShutters && _tempShutterRoomId == roomId)
            {
                doorState = true;
            }
            var doorFace = GetDoorStateFace(doorType, doorState);
            Graphics.DrawBitmap(
                _doorsBmp,
                DoorWidth * doorFace,
                _doorSrcYs[i] + baseY,
                DoorWidth,
                DoorHeight,
                _doorPos[i].X + offsetX,
                _doorPos[i].Y + offsetY,
                outerPalette,
                0);
        }
    }

    public bool HasItem(ItemSlot itemSlot) => GetItem(itemSlot) > 0;
    public int GetItem(ItemSlot itemSlot) => GetProfile().Items[itemSlot];
    public void SetItem(ItemSlot itemSlot, int value) => GetProfile().Items[itemSlot] = value;

    private void PostRupeeChange(byte value, ItemSlot itemSlot)
    {
        var profile = GetProfile();
        var curValue = profile.Items[itemSlot];
        var newValue = Math.Clamp(curValue + value, 0, 255);

        switch (itemSlot)
        {
            case ItemSlot.RupeesToAdd: profile.Statistics.RupeesCollected += value; break;
            case ItemSlot.RupeesToSubtract: profile.Statistics.RupeesSpent += value; break;
        }

        profile.Items[itemSlot] = newValue;
    }

    public void PostRupeeWin(byte value) => PostRupeeChange(value, ItemSlot.RupeesToAdd);
    public void PostRupeeLoss(byte value) => PostRupeeChange(value, ItemSlot.RupeesToSubtract);

    public void FillHearts(int heartValue)
    {
        var profile = GetProfile();
        var maxHeartValue = profile.Items[ItemSlot.HeartContainers] << 8;

        profile.Hearts += heartValue;
        if (profile.Hearts >= maxHeartValue)
        {
            profile.Hearts = maxHeartValue - 1;
        }
    }

    public void AddItem(ItemId itemId)
    {
        if ((int)itemId >= (int)ItemId.None) return;

        GlobalFunctions.PlayItemSound(Game, itemId);

        var equip = _itemToEquipment[(int)itemId];
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
            if (_infoBlock.LevelNumber < 9)
            {
                var bit = 1 << (_infoBlock.LevelNumber - 1);
                value = (byte)(Profile.Items[ItemSlot.Compass] | bit);
                slot = ItemSlot.Compass;
            }
        }
        else if (itemId == ItemId.Map)
        {
            if (_infoBlock.LevelNumber < 9)
            {
                var bit = 1 << (_infoBlock.LevelNumber - 1);
                value = (byte)(Profile.Items[ItemSlot.Map] | bit);
                slot = ItemSlot.Map;
            }
        }
        else if (itemId == ItemId.TriforcePiece)
        {
            var bit = 1 << (_infoBlock.LevelNumber - 1);
            value = (byte)(Profile.Items[ItemSlot.TriforcePieces] | bit);
        }

        Profile.Items[slot] = value;

        if (slot == ItemSlot.Ring)
        {
            Profile.SetPlayerColor();
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
        if (_infoBlock.LevelNumber == 0) return false;

        if (_infoBlock.LevelNumber < 9)
        {
            var itemValue = Profile.Items[itemSlot1To8];
            var bit = 1 << (_infoBlock.LevelNumber - 1);
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
                && _tempShutters && roomId == _tempShutterRoomId)
            || (_tempShutterDoorDir == doorDir && roomId == _tempShutterRoomId);
    }

    private bool GetEffectiveDoorState(Direction doorDir) => GetEffectiveDoorState(CurRoomId, doorDir);
    public LevelInfoBlock GetLevelInfo() => _infoBlock;
    public bool IsOverworld() => _infoBlock.LevelNumber == 0;
    public bool DoesRoomSupportLadder() => FindSparseFlag(Sparse.Ladder, CurRoomId);
    private TileAction GetTileAction(int tileRef) => TileAttr.GetAction(_tileAttrs[tileRef]);
    public bool IsUWMain(int roomId) => !IsOverworld() && (_roomAttrs[roomId].GetUniqueRoomId() < 0x3E);
    public bool IsUWMain() => IsUWMain(CurRoomId);
    private bool IsUWCellar(int roomId) => !IsOverworld() && (_roomAttrs[roomId].GetUniqueRoomId() >= 0x3E);
    public bool IsUWCellar() => IsUWCellar(CurRoomId);
    private bool GotShortcut(int roomId) => GetRoomFlags(roomId).ShortcutState;
    private bool GotSecret() => GetRoomFlags(CurRoomId).SecretState;

    public ObjectSlot DebugSpawnItem(ItemId itemId)
    {
        if (TryFindEmptyMonsterSlot(out var slot))
        {
            _objects[(int)slot] = GlobalFunctions.MakeItem(
                Game, itemId, Game.Link.X, Game.Link.Y - TileHeight, false);
            return slot;
        }

        return ObjectSlot.NoneFound;
    }

    public void DebugClearHistory()
    {
        Array.Clear(_levelKillCounts);
        Array.Clear(_roomHistory);
    }

    public ReadOnlySpan<byte> GetShortcutRooms()
    {
        var valueArray = _sparseRoomAttrs.GetItems<byte>(Sparse.Shortcut);
        // elemSize is at 1, but we don't need it.
        return valueArray[2..(2 + valueArray[0])];
    }

    private void TakeShortcut() => GetRoomFlags(CurRoomId).ShortcutState = true;
    public void TakeSecret() => GetRoomFlags(CurRoomId).SecretState = true;
    public bool GotItem() => GotItem(CurRoomId);
    public bool GotItem(int roomId) => GetRoomFlags(roomId).ItemState;
    public void MarkItem() => GetRoomFlags(CurRoomId).ItemState = true;
    private bool GetDoorState(int roomId, Direction door) => GetRoomFlags(roomId).GetDoorState(door);
    private void SetDoorState(int roomId, Direction door) => GetRoomFlags(roomId).SetDoorState(door);

    public void LiftItem(ItemId itemId, short timer = 0x80)
    {
        if (!IsPlaying()) return;

        if (itemId is ItemId.None or 0)
        {
            _state.Play.LiftItemTimer = 0;
            _state.Play.LiftItemId = 0;
            return;
        }

        _state.Play.LiftItemTimer = Game.Cheats.SpeedUp ? (byte)1 : timer;
        _state.Play.LiftItemId = itemId;

        Game.Link.SetState(PlayerState.Paused);
    }

    public bool IsLiftingItem()
    {
        if (!IsPlaying()) return false;

        return _state.Play.LiftItemId != 0;
    }

    public void OpenShutters()
    {
        _tempShutters = true;
        _tempShutterRoomId = CurRoomId;
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
        _worldKillCount++;

        if (_helpDropCounter < 0xA)
        {
            _helpDropCounter++;
            if (_helpDropCounter == 0xA)
            {
                if (allowBombDrop) _helpDropValue++;
            }
        }
    }

    // $7B67
    public void ResetKilledObjectCount()
    {
        _worldKillCount = 0;
        _helpDropCounter = 0;
        _helpDropValue = 0;
    }

    public void IncrementRoomKillCount()
    {
        _roomKillCount++;
    }

    public void SetBombItemDrop()
    {
        _helpDropCounter = 0xA;
        _helpDropValue = 0xA;
    }

    public void SetObservedPlayerPos(int x, int y)
    {
        _fakePlayerPos = new Point(x, y);
    }

    public void SetPersonWallY(int y)
    {
        _state.Play.PersonWallY = y;
    }

    public int GetFadeStep()
    {
        return _darkRoomFadeStep;
    }

    public void BeginFadeIn()
    {
        if (_darkRoomFadeStep > 0)
        {
            _brightenRoom = true;
        }
    }

    public void FadeIn()
    {
        if (_darkRoomFadeStep == 0)
        {
            _brightenRoom = false;
            return;
        }

        var timer = GetObjectTimer(ObjectSlot.FadeTimer);

        if (timer == 0)
        {
            _darkRoomFadeStep--;

            for (var i = 0; i < 2; i++)
            {
                Graphics.SetPaletteIndexed((Palette)(i + 2), _infoBlock.DarkPalette(_darkRoomFadeStep, i));
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

    private bool IsRoomInHistory()
    {
        for (var i = 0; i < _roomHistory.Length; i++)
        {
            if (_roomHistory[i] == CurRoomId) return true;
        }
        return false;
    }

    private void AddRoomToHistory()
    {
        var i = 0;

        for (; i < _roomHistory.Length; i++)
        {
            if (_roomHistory[i] == CurRoomId) break;
        }

        if (i == _roomHistory.Length)
        {
            _roomHistory[_nextRoomHistorySlot] = (byte)CurRoomId;
            _nextRoomHistorySlot++;
            if (_nextRoomHistorySlot >= _roomHistory.Length)
            {
                _nextRoomHistorySlot = 0;
            }
        }
    }

    private bool FindSparseFlag(Sparse attrId, int roomId) => _sparseRoomAttrs.FindSparseAttr<SparsePos>(attrId, roomId).HasValue;
    private SparsePos? FindSparsePos(Sparse attrId, int roomId) => _sparseRoomAttrs.FindSparseAttr<SparsePos>(attrId, roomId);
    private SparsePos2? FindSparsePos2(Sparse attrId, int roomId) => _sparseRoomAttrs.FindSparseAttr<SparsePos2>(attrId, roomId);
    private SparseRoomItem? FindSparseItem(Sparse attrId, int roomId) => _sparseRoomAttrs.FindSparseAttr<SparseRoomItem>(attrId, roomId);
    private ReadOnlySpan<ObjectAttr> GetObjectAttrs() => _extraData.GetItems<ObjectAttr>(Extra.ObjAttrs);
    public ObjectAttr GetObjectAttrs(ObjType type) => GetObjectAttrs()[(int)type];

    public int GetObjectMaxHP(ObjType type)
    {
        var hpAttrs = _extraData.GetItems<HPAttr>(Extra.HitPoints);
        var index = (int)type / 2;
        return hpAttrs[index].GetHP((int)type);
    }

    public int GetPlayerDamage(ObjType type)
    {
        var damageAttrs = _extraData.GetItems<byte>(Extra.PlayerDamage);
        var damageByte = damageAttrs[(int)type];
        return ((damageByte & 0xF) << 8) | (damageByte & 0xF0);
    }

    public void LoadOverworldRoom(int x, int y) => LoadRoom(x + y * 16, _curTileMapIndex);

    private void LoadRoom(int roomId, int tileMapIndex)
    {
        if (IsUWCellar(roomId))
        {
            LoadCellarContext();
        }
        else if (_prevRoomWasCellar)
        {
            LoadUnderworldContext();
        }

        CurRoomId = roomId;
        _curTileMapIndex = tileMapIndex;

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
                    SetOnlyObject(ObjectSlot.Item, itemObj);
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
            var pos = GetRoomItemPosition(_infoBlock.ShortcutPosition[posIndex]);

            if (itemId == ItemId.TriforcePiece)
            {
                pos.X = TriforcePieceX;
            }

            var itemObj = new ItemObjActor(Game, itemId, true, pos.X, pos.Y);
            SetOnlyObject(ObjectSlot.Item, itemObj);

            if (uwRoomAttrs.GetSecret() is Secret.FoesItem or Secret.LastBoss)
            {
                Game.Sound.PlayEffect(SoundEffect.RoomItem);
            }
        }
    }

    private void LoadCaveRoom(Cave uniqueRoomId)
    {
        _curTileMapIndex = 0;

        LoadLayout((int)uniqueRoomId, 0, TileScheme.Overworld);
    }

    private void LoadMap(int roomId, int tileMapIndex)
    {
        TileScheme tileScheme;
        var uniqueRoomId = _roomAttrs[roomId].GetUniqueRoomId();

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
        var primary = _primaryMobs[mobIndex];

        if (primary == 0xFF)
        {
            var index = mobIndex * 4;
            var secondaries = _secondaryMobs;
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
        var primary = _primaryMobs[mobIndex];

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
        var logfn = _traceLog.CreateFunctionLog();
        logfn.Write($"({uniqueRoomId}, {tileMapIndex}, {tileScheme})");

        var maxColumnStartOffset = (_colCount / 2 - 1) * _rowCount / 2;

        var columns = _roomCols[uniqueRoomId];
        var map = _tileMaps[tileMapIndex];
        var rowEnd = _startRow + _rowCount;

        var owLayoutFormat = tileScheme is TileScheme.Overworld or TileScheme.UnderworldCellar;

        _loadMobFunc = tileScheme switch
        {
            TileScheme.Overworld => LoadOWMob,
            TileScheme.UnderworldMain => LoadUWMob,
            TileScheme.UnderworldCellar => LoadOWMob,
            _ => _loadMobFunc
        };

        var owRoomAttrs = CurrentOWRoomAttrs;
        var roomAttrs = CurrentOWRoomAttrs.Attrs;
        logfn.Write($"owRoomAttrs:{roomAttrs.A:X2},{roomAttrs.D:X2},{roomAttrs.C:X2},{roomAttrs.D:X2}");

        for (var i = 0; i < _colCount / 2; i++)
        {
            var columnDesc = columns.ColumnDesc[i];
            var tableIndex = (byte)((columnDesc & 0xF0) >> 4);
            var columnIndex = (byte)(columnDesc & 0x0F);

            var table = _colTables.GetItem(tableIndex);
            var k = 0;
            var columnStart = 0;

            for (columnStart = 0; columnStart <= maxColumnStartOffset; columnStart++)
            {
                var t = table[columnStart];

                if ((t & 0x80) != 0)
                {
                    if (k == columnIndex) break;
                    k++;
                }
            }

            if (columnStart > maxColumnStartOffset) throw new Exception();

            var c = _startCol + i * 2;

            for (var r = _startRow; r < rowEnd; columnStart++)
            {
                var t = table[columnStart];
                var tileRef = owLayoutFormat ? (byte)(t & 0x3F) : (byte)(t & 0x7);

                _loadMobFunc(ref map, r, c, tileRef);

                var attr = _tileAttrs[tileRef];
                var action = owRoomAttrs.IsInQuest(Profile.Quest) ? TileAttr.GetAction(attr) : TileAction.None;
                TileActionDel? actionFunc = null;

                if (action != TileAction.None)
                {
                    logfn.Write($"tileRef:{tileRef}, attr:{attr:X2}, action:{action}, pos:{r:X2},{c:X2}");
                    actionFunc = ActionFuncs[(int)action];
                    actionFunc(r, c, TileInteraction.Load);
                }

                r += 2;

                if (owLayoutFormat)
                {
                    if ((t & 0x40) != 0 && r < rowEnd)
                    {
                        _loadMobFunc(ref map, r, c, tileRef);
                        actionFunc?.Invoke(r, c, TileInteraction.Load);
                        r += 2;
                    }
                }
                else
                {
                    var repeat = (t >> 4) & 0x7;
                    for (var m = 0; m < repeat && r < rowEnd; m++)
                    {
                        _loadMobFunc(ref map, r, c, tileRef);
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
                for (var c = _startCol; c < _startCol + _colCount; c += 2)
                {
                    var tileRef = _tileMaps[_curTileMapIndex].Refs(UWBlockRow, c);
                    if (tileRef == (byte)BlockObjType.TileBlock)
                    {
                        ActionFuncs[(int)TileAction.Block](UWBlockRow, c, TileInteraction.Load);
                        break;
                    }
                }
            }
        }

        for (var i = 0; i < Rows * Columns; i++)
        {
            var t = map.Refs(i);
            map.Behaviors(i) = _tileBehaviors[t];
        }

        PatchTileBehaviors();
    }

    private void PatchTileBehaviors()
    {
        PatchTileBehavior(_ghostCount, _ghostCells, TileBehavior.Ghost0);
        PatchTileBehavior(_armosCount, _armosCells, TileBehavior.Armos0);
    }

    private void PatchTileBehavior(int count, Cell[] cells, TileBehavior baseBehavior)
    {
        for (var i = 0; i < count; i++)
        {
            var row = cells[i].Row;
            var col = cells[i].Col;
            var behavior = (byte)((int)baseBehavior + 15 - i);
            _tileMaps[_curTileMapIndex].Behaviors(row, col) = behavior;
            _tileMaps[_curTileMapIndex].Behaviors(row, col + 1) = behavior;
            _tileMaps[_curTileMapIndex].Behaviors(row + 1, col) = behavior;
            _tileMaps[_curTileMapIndex].Behaviors(row + 1, col + 1) = behavior;
        }
    }

    private void UpdateDoorTileBehavior(int doorOrd)
    {
        UpdateDoorTileBehavior(CurRoomId, _curTileMapIndex, doorOrd);
    }

    private void UpdateDoorTileBehavior(int roomId, int tileMapIndex, int doorOrd)
    {
        var map = _tileMaps[tileMapIndex];
        var dir = doorOrd.GetOrdDirection();
        var corner = _doorCorners[doorOrd];
        var type = GetDoorType(roomId, dir);
        var state = GetEffectiveDoorState(roomId, dir);
        var behavior = (byte)(state ? _doorBehaviors[(int)type].Open : _doorBehaviors[(int)type].Closed);

        map.Behaviors(corner.Row, corner.Col) = behavior;
        map.Behaviors(corner.Row, corner.Col + 1) = behavior;
        map.Behaviors(corner.Row + 1, corner.Col) = behavior;
        map.Behaviors(corner.Row + 1, corner.Col + 1) = behavior;

        if ((TileBehavior)behavior == TileBehavior.Doorway)
        {
            corner = _behindDoorCorners[doorOrd];
            map.Behaviors(corner.Row, corner.Col) = behavior;
            map.Behaviors(corner.Row, corner.Col + 1) = behavior;
            map.Behaviors(corner.Row + 1, corner.Col) = behavior;
            map.Behaviors(corner.Row + 1, corner.Col + 1) = behavior;
        }
    }

    private void GotoPlay(RoomType roomType = RoomType.Regular)
    {
        _curMode = roomType switch
        {
            RoomType.Regular => GameMode.Play,
            RoomType.Cave => GameMode.PlayCave,
            RoomType.Cellar => GameMode.PlayCellar,
            _ => throw new ArgumentOutOfRangeException(nameof(roomType), roomType, "Unknown room type.")
        };

        _curColorSeqNum = 0;
        _tempShutters = false;
        RoomObjCount = 0;
        RoomObj = null;
        _roomKillCount = 0;
        _roomAllDead = false;
        _madeRoomItem = false;
        EnablePersonFireballs = false;
        _ghostCount = 0;
        _armosCount = 0;
        ActiveShots = 0;

        _state.Play.Substate = PlayState.Substates.Active;
        _state.Play.AnimatingRoomColors = false;
        _state.Play.AllowWalkOnWater = false;
        _state.Play.UncoveredRecorderSecret = false;
        _state.Play.RoomType = roomType;
        _state.Play.LiftItemTimer = 0;
        _state.Play.LiftItemId = 0;
        _state.Play.PersonWallY = 0;

        if (IsOverworld() && FindSparseFlag(Sparse.Dock, CurRoomId))
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
            GetCurrentRoomFlags().VisitState = true;
        }
    }

    private void Pause()
    {
        _pause = PauseState.Paused;
        Game.Sound.Pause();
    }

    private void Unpause()
    {
        _pause = 0;
        Game.Sound.Unpause();
    }

    private void UpdatePlay()
    {
        if (_state.Play.Substate != PlayState.Substates.Active) return;

        if (_brightenRoom)
        {
            FadeIn();
            DecrementObjectTimers();
            DecrementStunTimers();
            return;
        }

        if (Game.Enhancements.ImprovedMenus && Game.Input.AreBothButtonsDown(GameButton.Select, GameButton.Start))
        {
            GotoContinueQuestion();
            return;
        }

        if (_submenu != SubmenuState.IdleClosed)
        {
            UpdateSubmenu();
            return;
        }

        if (_pause == PauseState.Unpaused)
        {
            if (Game.Enhancements.ImprovedMenus)
            {
                if (IsButtonPressing(GameButton.ItemNext)) Menu.SelectNextItem();
                if (IsButtonPressing(GameButton.ItemPrevious)) Menu.SelectPreviousItem();
            }

            if (IsAnyButtonPressing(GameButton.Select, GameButton.Pause))
            {
                Pause();
                return;
            }

            if (IsButtonPressing(GameButton.Start))
            {
                _submenu = SubmenuState.StartOpening;
                return;
            }
        }
        else if (_pause == PauseState.Paused)
        {
            if (IsAnyButtonPressing(GameButton.Select, GameButton.Pause))
            {
                Unpause();
            }
            return;
        }

        DecrementObjectTimers();
        DecrementStunTimers();

        if (_objectTimers[(int)ObjectSlot.FluteMusic] != 0) return;

        if (_pause == PauseState.FillingHearts)
        {
            FillHeartsStep();
            return;
        }

        if (_state.Play.AnimatingRoomColors)
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
            var obj = _objects[CurObjSlot];
            if (obj != null && !obj.IsDeleted)
            {
                if (obj.DecoratedUpdate())
                {
                    HandleNormalObjectDeath();
                }
            }
            else if (_placeholderTypes[CurObjSlot] != 0)
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
        switch (_submenu)
        {
            case SubmenuState.StartOpening:
                Menu.Enable();
                _submenu++;
                _statusBar.EnableFeatures(StatusBarFeatures.Equipment, false);

                if (Game.Cheats.SpeedUp)
                {
                    _submenu = SubmenuState.EndOpening;
                    _submenuOffsetY = SubmenuType.Height;
                }
                break;

            case SubmenuState.EndOpening:
                _submenuOffsetY += SubmenuType.YScrollSpeed;
                if (_submenuOffsetY >= SubmenuType.Height)
                {
                    _submenuOffsetY = SubmenuType.Height;
                    Menu.Activate();
                    _submenu++;
                }
                break;

            case SubmenuState.IdleOpen:
                if (IsButtonPressing(GameButton.Start))
                {
                    Menu.Deactivate();
                    _submenu++;

                    if (Game.Cheats.SpeedUp)
                    {
                        _submenu = SubmenuState.StartClose;
                        _submenuOffsetY = 0;
                    }
                }
                break;

            case SubmenuState.StartClose:
                _submenuOffsetY -= SubmenuType.YScrollSpeed;
                if (_submenuOffsetY <= 0)
                {
                    Menu.Disable();
                    _submenu = SubmenuState.IdleClosed;
                    _statusBar.EnableFeatures(StatusBarFeatures.Equipment, true);
                    _submenuOffsetY = 0;
                }
                break;

            default:
                _submenu++;
                break;
        }

        if (_submenu != 0)
        {
            Menu.Update();
        }
    }

    private void CheckShutters()
    {
        if (!_triggerShutters) return;

        _triggerShutters = false;
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

        if (dirs != 0 && _triggeredDoorCmd == 0)
        {
            _triggeredDoorCmd = 6;
            _triggeredDoorDir |= (Direction)0x10;
        }
    }

    private void UpdateDoors2()
    {
        if (GetMode() == GameMode.EndLevel
            || _objectTimers[(int)ObjectSlot.Door] != 0
            || _triggeredDoorCmd == 0)
        {
            return;
        }

        if ((_triggeredDoorCmd & 1) == 0)
        {
            _triggeredDoorCmd++;
            _objectTimers[(int)ObjectSlot.Door] = 8;
            return;
        }

        if ((_triggeredDoorDir & (Direction)0x10) != 0)
        {
            OpenShutters();
        }

        var d = 1;

        for (var i = 0; i < 4; i++, d <<= 1)
        {
            if ((_triggeredDoorDir & (Direction)d) == 0) continue;

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

        _triggeredDoorCmd = 0;
        _triggeredDoorDir = Direction.None;
    }

    private byte GetNextTeleportingRoomIndex()
    {
        var facing = Game.Link.Facing;
        var growing = facing is Direction.Up or Direction.Right;

        var pieces = GetItem(ItemSlot.TriforcePieces);
        var index = _teleportingRoomIndex;
        var mask = 1 << _teleportingRoomIndex;

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
        if (_state.Play.Timer == 0)
        {
            _state.Play.AnimatingRoomColors = false;
            var posAttr = FindSparsePos(Sparse.Recorder, CurRoomId);
            if (posAttr != null)
            {
                GetRoomCoord(posAttr.Value.pos, out var row, out var col);
                SetMob(row * 2, col * 2, BlockObjType.MobStairs);
                Game.Sound.PlayEffect(SoundEffect.Secret);
            }
            return;
        }

        if ((_state.Play.Timer % 8) == 0)
        {
            var colorSeq = _extraData.ReadLengthPrefixedItem((int)Extra.PondColors);
            if (_curColorSeqNum < colorSeq.Length - 1)
            {
                if (_curColorSeqNum == colorSeq.Length - 2)
                {
                    _state.Play.AllowWalkOnWater = true;
                }

                int colorIndex = colorSeq[_curColorSeqNum];
                _curColorSeqNum++;
                Graphics.SetColorIndexed((Palette)3, 3, colorIndex);
                Graphics.UpdatePalettes();
            }
        }

        _state.Play.Timer--;
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
                if (doorType != DoorType.Bombable) continue;

                var doorDir = iDoor.GetOrdDirection();
                var doorState = GetDoorState(CurRoomId, doorDir);
                if (doorState) continue;

                var doorMiddle = _doorMiddles[iDoor];
                if (Math.Abs(bombX - doorMiddle.X) < UWBombRadius
                    && Math.Abs(bombY - doorMiddle.Y) < UWBombRadius)
                {
                    _triggeredDoorCmd = 6;
                    _triggeredDoorDir = doorDir;
                    GetProfile().Statistics.UWWallsBombed++;
                    break;
                }
            }
        }
    }

    public bool HasLivingObjects()
    {
        return !_roomAllDead;
    }

    private bool CalcHasLivingObjects()
    {
        foreach (var monster in GetMonsters<Actor>())
        {
            if (monster.CountsAsLiving) return true;
        }

        return false;
    }

    private void CheckSecrets()
    {
        if (IsOverworld()) return;

        if (!_roomAllDead)
        {
            if (!CalcHasLivingObjects())
            {
                Game.Link.ClearParalized();
                _roomAllDead = true;
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
                if (GetItem(ItemSlot.PowerTriforce) != 0) _triggerShutters = true;
                break;

            case Secret.FoesItem:
                // ORIGINAL: BlockDoor and BlockStairs are handled here.
                if (_roomAllDead)
                {
                    if (!_madeRoomItem && !GotItem())
                    {
                        _madeRoomItem = true;
                        AddUWRoomItem(CurRoomId);
                    }
                }
                // fall thru
                goto case Secret.FoesDoor;
            case Secret.FoesDoor:
                if (_roomAllDead) _triggerShutters = true;
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
                // JOE: TODO: This is too cryptic. Have a "Kill" on Actor? Have "Decoration" be an enum as well?
                monster.Decoration = 0x10;
            }
        }
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
            ReadOnlySpan<int> fireballLayouts = [0x24, 0x23];

            var uwRoomAttrs = CurrentUWRoomAttrs;
            var layoutId = uwRoomAttrs.GetUniqueRoomId();

            for (var i = 0; i < fireballLayouts.Length; i++)
            {
                if (fireballLayouts[i] == layoutId)
                {
                    pattern = i;
                    break;
                }
            }
        }

        if (pattern >= 0)
        {
            Statues.Update(Game, pattern);
        }
    }

    private void OnLeavePlay()
    {
        if (_lastMode == GameMode.Play)
        {
            SaveObjectCount();
        }
    }

    private void ClearLevelData()
    {
        _curColorSeqNum = 0;
        _darkRoomFadeStep = 0;
        _curMazeStep = 0;
        _tempShutterRoomId = 0;
        _tempShutterDoorDir = 0;
        _tempShutterRoomId = 0;
        _tempShutters = false;
        WhirlwindTeleporting = 0;
        ActiveShots = 0;

        _roomKillCount = 0;
        _roomAllDead = false;
        _madeRoomItem = false;
        EnablePersonFireballs = false;
    }

    private static bool IsRecurringFoe(ObjType type)
    {
        return type is < ObjType.OneDodongo or ObjType.RedLamnola or ObjType.BlueLamnola or >= ObjType.Trap;
    }

    private void SaveObjectCount()
    {
        var flags = GetCurrentRoomFlags();

        if (IsOverworld())
        {
            var savedCount = flags.ObjectCount;
            int count;

            if (_roomKillCount >= RoomObjCount)
            {
                count = 7;
            }
            else
            {
                count = (_roomKillCount & 7) + savedCount;
                if (count > 7)
                {
                    count = 7;
                }
            }

            flags.ObjectCount = count;
        }
        else
        {
            if (RoomObjCount != 0)
            {
                if (_roomKillCount == 0 || (RoomObj != null && RoomObj.IsReoccuring))
                {
                    if (_roomKillCount < RoomObjCount)
                    {
                        _levelKillCounts[CurRoomId] += _roomKillCount;
                        var count = _levelKillCounts[CurRoomId] < 3 ? _levelKillCounts[CurRoomId] : 2;
                        flags.ObjectCount = (byte)count;
                        return;
                    }
                }
            }

            _levelKillCounts[CurRoomId] = 0xF;
            flags.ObjectCount = 3;
        }
    }

    private void CalcObjCountToMake(ref ObjType type, ref int count)
    {
        var flags = GetCurrentRoomFlags();

        if (IsOverworld())
        {
            if (!IsRoomInHistory() && (flags.ObjectCount == 7))
            {
                flags.ObjectCount = 0;
                return;
            }

            if (flags.ObjectCount == 7)
            {
                type = ObjType.None;
                count = 0;
            }
            else if (flags.ObjectCount != 0)
            {
                var savedCount = flags.ObjectCount;
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
        else // Is Underworld
        {
            if (IsRoomInHistory() || flags.ObjectCount != 3)
            {
                if (count < _levelKillCounts[CurRoomId])
                {
                    type = ObjType.None;
                    count = 0;
                }
                else
                {
                    count -= _levelKillCounts[CurRoomId];
                }
                return;
            }

            if (IsRecurringFoe(type))
            {
                flags.ObjectCount = 0;
                _levelKillCounts[CurRoomId] = 0;
            }
            else
            {
                type = ObjType.None;
                count = 0;
            }
        }
    }

    private void UpdateObservedPlayerPos()
    {
        // ORIGINAL: This happens after player updates and before player items update.

        if (!_giveFakePlayerPos)
        {
            _fakePlayerPos.X = Game.Link.X;
            _fakePlayerPos.Y = Game.Link.Y;
        }

        // ORIGINAL: This happens after player items update and before the rest of objects update.
        ref var timer = ref _stunTimers[(int)ObjectSlot.ObservedPlayerTimer];
        if (timer != 0) return;

        timer = Random.Shared.Next(0, 8);

        _giveFakePlayerPos = !_giveFakePlayerPos;
        if (_giveFakePlayerPos)
        {
            if (_fakePlayerPos.X == Game.Link.X)
            {
                _fakePlayerPos.X ^= 0xFF;
                _fakePlayerPos.Y ^= 0xFF;
            }
        }
    }

    private void UpdateRupees()
    {
        if ((Game.FrameCounter & 1) != 0) return;

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
        if (_state.Play.LiftItemId == 0) return;

        ArgumentOutOfRangeException.ThrowIfNegative(_state.Play.LiftItemTimer);

        _state.Play.LiftItemTimer--;
        if (_state.Play.LiftItemTimer == 0)
        {
            _state.Play.LiftItemId = 0;
            Game.Link.SetState(PlayerState.Idle);
        }
        else
        {
            Game.Link.SetState(PlayerState.Paused);
        }
    }

    private void DrawPlay()
    {
        if (_submenu != 0)
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
            DrawLinkLiftingItem(_state.Play.LiftItemId);
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
        using (var _ = Graphics.SetClip(0, TileMapBaseY + _submenuOffsetY, TileMapWidth, TileMapHeight - _submenuOffsetY))
        {
            ClearScreen();
            DrawMap(CurRoomId, _curTileMapIndex, 0, _submenuOffsetY);
        }

        if (IsUWMain(CurRoomId))
        {
            DrawDoors(CurRoomId, true, 0, _submenuOffsetY);
        }

        Menu.Draw(_submenuOffsetY);
    }

    private void DrawObjects(out Actor? objOverPlayer)
    {
        objOverPlayer = null;

        for (var i = ObjectSlot.FirstSlot; i < ObjectSlot.MaxObjects; i++)
        {
            CurObjectSlot = i;

            var obj = _objects[(int)i];
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

        if (_state.Play.RoomType == RoomType.Cave)
        {
            MakeCaveObjects();
            return;
        }

        var roomAttr = _roomAttrs[CurRoomId];
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
                list = _objLists.GetItem(listId);
            }
            else
            {
                Array.Fill(repeatedIds, (byte)objId, 0, count);
                list = repeatedIds;
            }

            var dirOrd = entryDir.GetOrdinal();
            // var spotSeq = extraData.GetItem<SpotSeq>(Extra.SpawnSpots);
            var spots = _extraData.ReadLengthPrefixedItem((int)Extra.SpawnSpots);
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
                    && type != ObjType.Zora // JOE: TODO: Move this to an attribute on the class?
                    && type != ObjType.Armos
                    && type != ObjType.StandingFire
                    && type != ObjType.Whirlwind
                    )
                {
                    _placeholderTypes[(int)slot] = (byte)type;
                }
                else if (FindSpawnPos(type, dirSpots, spotsLen, ref x, ref y))
                {
                    var obj = Actor.FromType(type, Game, x, y);
                    SetOnlyObject(slot, obj);
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
        const int startY = 0x9D;

        ReadOnlySpan<int> startXs = [0x20, 0x60, 0x90, 0xD0];

        for (var i = 0; i < 4; i++)
        {
            CurObjSlot = i;

            var keese = Actor.FromType(ObjType.BlueKeese, Game, startXs[i], startY);
            SetObject((ObjectSlot)i, keese);
        }
    }

    private void MakeCaveObjects()
    {
        var owRoomAttrs = CurrentOWRoomAttrs;
        var caveIndex = owRoomAttrs.GetCaveId() - FirstCaveIndex;

        var caves = _extraData.LoadVariableLengthData<CaveSpecListStruct, CaveSpecList>((int)Extra.Caves);

        if (caveIndex >= caves.Count)
        {
            throw new Exception($"Invalid caveIndex. {caveIndex} >= {caves.Count}");
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
            var stringIdTables = _extraData.GetItem<LevelPersonStrings>(Extra.LevelPersonStringIds);

            var levelIndex = _infoBlock.EffectiveLevelNumber - 1;
            int levelTableIndex = _levelGroups[levelIndex];
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
        ReadOnlySpan<int> teleportYs = [0x8D, 0xAD, 0x8D, 0x8D, 0xAD, 0x8D, 0xAD, 0x5D];

        if (WhirlwindTeleporting != 0)
        {
            var y = teleportYs[_teleportingRoomIndex];

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
        ref var timer = ref _stunTimers[(int)ObjectSlot.EdgeObjTimer];
        if (timer != 0) return;

        timer = Random.Shared.Next(0, 4) + 2;

        var x = _edgeX;
        var y = _edgeY;

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
            if (y == _edgeY && x == _edgeX) break;
        }

        _edgeX = x;
        _edgeY = y;

        if (Math.Abs(Game.Link.X - x) >= 0x22
            || Math.Abs(Game.Link.Y - y) >= 0x22)
        {
            // JOE: TODO: What?
            var obj = Actor.FromType((ObjType)_placeholderTypes[CurObjSlot], Game, x, y - 3);
            SetOnlyObject(CurObjectSlot, obj);
            _placeholderTypes[CurObjSlot] = 0;
            obj.Decoration = 0;
        }
    }

    private void HandleNormalObjectDeath()
    {
        var obj = _objects[CurObjSlot] ?? throw new Exception("Missing object");
        var x = obj.X;
        var y = obj.Y;

        _objects[CurObjSlot] = null;

        // JOE: TODO: Put whatever this is on the object itself.
        if (obj.ObjType is not (ObjType.ChildGel or ObjType.RedKeese or ObjType.DeadDummy))
        {
            var cycle = _worldKillCycle + 1;
            if (cycle == 10)
            {
                cycle = 0;
            }
            _worldKillCycle = (byte)cycle;

            // JOE: NOTE: I believe this is because Zora's always respawn.
            if (obj is not ZoraActor)
            {
                _roomKillCount++;
            }
        }

        TryDroppingItem(obj, x, y);
    }

    private void TryDroppingItem(Actor origType, int x, int y)
    {
        if (CurObjSlot == (int)ObjectSlot.Monster1 && origType is StalfosActor or GibdoActor) return;

        var objClass = origType.Attributes.GetItemDropClass();
        if (objClass == 0) return;
        objClass--;

        ItemId itemId;

        if (_worldKillCount == 0x10)
        {
            itemId = ItemId.Fairy;
            _helpDropCounter = 0;
            _helpDropValue = 0;
        }
        else if (_helpDropCounter >= 0xA)
        {
            itemId = _helpDropValue == 0 ? ItemId.FiveRupees : ItemId.Bomb;
            _helpDropCounter = 0;
            _helpDropValue = 0;
        }
        else
        {
            ReadOnlySpan<int> classBases = [0, 10, 20, 30];
            ReadOnlySpan<int> classRates = [0x50, 0x98, 0x68, 0x68];
            ReadOnlySpan<int> dropItems = [
                0x22, 0x18, 0x22, 0x18, 0x23, 0x18, 0x22, 0x22, 0x18, 0x18, 0x0F, 0x18, 0x22, 0x18, 0x0F, 0x22,
                0x21, 0x18, 0x18, 0x18, 0x22, 0x00, 0x18, 0x21, 0x18, 0x22, 0x00, 0x18, 0x00, 0x22, 0x22, 0x22,
                0x23, 0x18, 0x22, 0x23, 0x22, 0x22, 0x22, 0x18
            ];

            var r = Random.Shared.GetByte();
            var rate = classRates[objClass];
            if (r >= rate) return;

            var classIndex = classBases[objClass] + _worldKillCycle;
            itemId = (ItemId)dropItems[classIndex];
        }

        _objects[CurObjSlot] = GlobalFunctions.MakeItem(Game, itemId, x, y, false);
    }

    private void FillHeartsStep()
    {
        Game.Sound.PlayEffect(SoundEffect.Character);

        var maxHeartsValue = Profile.GetMaxHeartsValue();

        FillHearts(6);

        if (Profile.Hearts == maxHeartsValue)
        {
            _pause = 0;
            SwordBlocked = false;
        }
    }

    private void GotoScroll(Direction dir)
    {
        if (dir == Direction.None) throw new ArgumentOutOfRangeException(nameof(dir));

        _state.Scroll.CurRoomId = CurRoomId;
        _state.Scroll.ScrollDir = dir;
        _state.Scroll.Substate = ScrollState.Substates.Start;
        _curMode = GameMode.Scroll;
    }

    private void GotoScroll(Direction dir, int currentRoomId)
    {
        GotoScroll(dir);
        _state.Scroll.CurRoomId = currentRoomId;
    }

    private bool CalcMazeStayPut(Direction dir)
    {
        if (!IsOverworld()) return false;

        var mazeOptional = _sparseRoomAttrs.FindSparseAttr<SparseMaze>(Sparse.Maze, CurRoomId);
        if (mazeOptional == null) return false;

        var maze = mazeOptional.Value;

        if (dir == maze.ExitDirection)
        {
            _curMazeStep = 0;
            return false;
        }

        var paths = maze.Paths;
        if (dir != paths[_curMazeStep])
        {
            _curMazeStep = 0;
            return true;
        }

        _curMazeStep++;
        if (_curMazeStep != paths.Length)
        {
            return true;
        }

        _curMazeStep = 0;
        Game.Sound.PlayEffect(SoundEffect.Secret);
        return false;
    }

    private void UpdateScroll()
    {
        ScrollFuncs[(int)_state.Scroll.Substate]();
    }

    private void UpdateScroll_Start()
    {
        GetWorldCoord(_state.Scroll.CurRoomId, out var roomRow, out var roomCol);

        Actor.MoveSimple(ref roomCol, ref roomRow, _state.Scroll.ScrollDir, 1);

        var nextRoomId = CalcMazeStayPut(_state.Scroll.ScrollDir)
            ? _state.Scroll.CurRoomId
            : MakeRoomId(roomRow, roomCol);

        _state.Scroll.NextRoomId = nextRoomId;
        _state.Scroll.Substate = ScrollState.Substates.AnimatingColors;
    }

    private void UpdateScroll_AnimatingColors()
    {
        if (_curColorSeqNum == 0)
        {
            _state.Scroll.Substate = ScrollState.Substates.LoadRoom;
            return;
        }

        if ((Game.FrameCounter & 4) != 0)
        {
            _curColorSeqNum--;

            var colorSeq = _extraData.ReadLengthPrefixedItem((int)Extra.PondColors);
            int color = colorSeq[_curColorSeqNum];
            Graphics.SetColorIndexed((Palette)3, 3, color);
            Graphics.UpdatePalettes();

            if (_curColorSeqNum == 0)
            {
                _state.Scroll.Substate = ScrollState.Substates.LoadRoom;
            }
        }
    }

    private void UpdateScroll_FadeOut()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(_state.Scroll.Timer);

        if (_state.Scroll.Timer > 0)
        {
            _state.Scroll.Timer--;
            return;
        }

        for (var i = 0; i < 2; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i + 2, _infoBlock.DarkPalette(_darkRoomFadeStep, i));
        }
        Graphics.UpdatePalettes();

        _darkRoomFadeStep++;

        if (_darkRoomFadeStep == 4)
        {
            _state.Scroll.Substate = ScrollState.Substates.Scroll;
            _state.Scroll.Timer = ScrollState.StateTime;
        }
        else
        {
            _state.Scroll.Timer = 9;
        }
    }

    private void UpdateScroll_LoadRoom()
    {
        if (_state.Scroll.ScrollDir == Direction.Down
            && !IsOverworld()
            && CurRoomId == _infoBlock.StartRoomId)
        {
            GotoLoadLevel(0);
            return;
        }

        _state.Scroll.OffsetX = 0;
        _state.Scroll.OffsetY = 0;
        _state.Scroll.SpeedX = 0;
        _state.Scroll.SpeedY = 0;
        _state.Scroll.OldMapToNewMapDistX = 0;
        _state.Scroll.OldMapToNewMapDistY = 0;

        switch (_state.Scroll.ScrollDir)
        {
            case Direction.Left:
                _state.Scroll.OffsetX = -TileMapWidth;
                _state.Scroll.SpeedX = ScrollSpeed;
                _state.Scroll.OldMapToNewMapDistX = TileMapWidth;
                break;

            case Direction.Right:
                _state.Scroll.OffsetX = TileMapWidth;
                _state.Scroll.SpeedX = -ScrollSpeed;
                _state.Scroll.OldMapToNewMapDistX = -TileMapWidth;
                break;

            case Direction.Up:
                _state.Scroll.OffsetY = -TileMapHeight;
                _state.Scroll.SpeedY = ScrollSpeed;
                _state.Scroll.OldMapToNewMapDistY = TileMapHeight;
                break;

            case Direction.Down:
                _state.Scroll.OffsetY = TileMapHeight;
                _state.Scroll.SpeedY = -ScrollSpeed;
                _state.Scroll.OldMapToNewMapDistY = -TileMapHeight;
                break;
        }

        _state.Scroll.OldRoomId = CurRoomId;

        var nextRoomId = _state.Scroll.NextRoomId;
        var nextTileMapIndex = (_curTileMapIndex + 1) % 2;
        _state.Scroll.OldTileMapIndex = _curTileMapIndex;

        _tempShutterRoomId = nextRoomId;
        _tempShutterDoorDir = _state.Scroll.ScrollDir.GetOppositeDirection();

        LoadRoom(nextRoomId, nextTileMapIndex);

        var uwRoomAttrs = GetUWRoomAttrs(nextRoomId);
        if (uwRoomAttrs.IsDark() && _darkRoomFadeStep == 0 && !Profile.PreventDarkRooms(Game))
        {
            _state.Scroll.Substate = ScrollState.Substates.FadeOut;
            _state.Scroll.Timer = Game.Cheats.SpeedUp ? 1 : 9;
        }
        else
        {
            _state.Scroll.Substate = ScrollState.Substates.Scroll;
            _state.Scroll.Timer = Game.Cheats.SpeedUp ? 1 : ScrollState.StateTime;
        }
    }

    private void UpdateScroll_Scroll()
    {
        if (_state.Scroll.Timer > 0)
        {
            _state.Scroll.Timer--;
            return;
        }

        if (_state.Scroll.OffsetX == 0 && _state.Scroll.OffsetY == 0)
        {
            GotoEnter(_state.Scroll.ScrollDir);
            if (IsOverworld() && _state.Scroll.NextRoomId == (int)UniqueRoomIds.TopRightOverworldSecret)
            {
                Game.Sound.PlayEffect(SoundEffect.Secret);
            }
            return;
        }

        if (Game.Cheats.SpeedUp)
        {
            // JOE: TODO
        }

        _state.Scroll.OffsetX += _state.Scroll.SpeedX;
        _state.Scroll.OffsetY += _state.Scroll.SpeedY;

        // JOE: TODO: Does this prevent screen wrapping?
        var playerLimits = Link.PlayerLimits;
        if (_state.Scroll.SpeedX != 0)
        {
            Game.Link.X = Math.Clamp(Game.Link.X + _state.Scroll.SpeedX, playerLimits[1], playerLimits[0]);
        }
        else
        {
            Game.Link.Y = Math.Clamp(Game.Link.Y + _state.Scroll.SpeedY, playerLimits[3], playerLimits[2]);
        }

        Game.Link.Animator.Advance();
    }

    private void DrawScroll()
    {
        using (var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight))
        {
            ClearScreen();

            if (_state.Scroll.Substate is ScrollState.Substates.Scroll or ScrollState.Substates.FadeOut)
            {
                var oldMapOffsetX = _state.Scroll.OffsetX + _state.Scroll.OldMapToNewMapDistX;
                var oldMapOffsetY = _state.Scroll.OffsetY + _state.Scroll.OldMapToNewMapDistY;

                DrawMap(CurRoomId, _curTileMapIndex, _state.Scroll.OffsetX, _state.Scroll.OffsetY);
                DrawMap(_state.Scroll.OldRoomId, _state.Scroll.OldTileMapIndex, oldMapOffsetX, oldMapOffsetY);
            }
            else
            {
                DrawMap(CurRoomId, _curTileMapIndex, 0, 0);
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

        _state.Leave.CurRoomId = CurRoomId;
        _state.Leave.ScrollDir = dir;
        _state.Leave.Timer = LeaveState.StateTime;
        _curMode = GameMode.Leave;
    }

    private void GotoLeave(Direction dir, int currentRoomId)
    {
        GotoLeave(dir);
        _state.Leave.CurRoomId = currentRoomId;
    }

    private void UpdateLeave()
    {
        var playerLimits = Link.PlayerLimits;
        var dirOrd = Game.Link.Facing.GetOrdinal();
        var coord = Game.Link.Facing.IsVertical() ? Game.Link.Y : Game.Link.X;

        if (coord != playerLimits[dirOrd])
        {
            Game.Link.MoveLinear(_state.Leave.ScrollDir, Link.WalkSpeed);
            Game.Link.Animator.Advance();
            return;
        }

        if (_state.Leave.Timer == 0)
        {
            Game.Link.Animator.AdvanceFrame();
            GotoScroll(_state.Leave.ScrollDir, _state.Leave.CurRoomId);
            return;
        }

        _state.Leave.Timer--;
    }

    private void DrawLeave()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        DrawRoomNoObjects();
    }

    private void GotoEnter(Direction dir)
    {
        _state.Enter.Substate = EnterState.Substates.Start;
        _state.Enter.ScrollDir = dir;
        _state.Enter.Timer = 0;
        _state.Enter.PlayerPriority = SpritePriority.AboveBg;
        _state.Enter.PlayerSpeed = Link.WalkSpeed;
        _state.Enter.GotoPlay = false;
        Unpause();
        _curMode = GameMode.Enter;
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
        EnterFuncs[(int)_state.Enter.Substate]();

        if (_state.Enter.GotoPlay)
        {
            var origShutterDoorDir = _tempShutterDoorDir;
            _tempShutterDoorDir = Direction.None;
            if (IsUWMain(CurRoomId)
                && origShutterDoorDir != Direction.None
                && GetDoorType(CurRoomId, origShutterDoorDir) == DoorType.Shutter)
            {
                Game.Sound.PlayEffect(SoundEffect.Door);
                var doorOrd = origShutterDoorDir.GetOrdinal();
                UpdateDoorTileBehavior(doorOrd);
            }

            _statusBar.EnableFeatures(StatusBarFeatures.All, true);
            if (IsOverworld() && FromUnderground != 0)
            {
                Game.Sound.PlaySong(_infoBlock.SongId, SongStream.MainSong, true);
            }
            GotoPlay();
            return;
        }

        Game.Link.Animator.Advance();
    }

    private void UpdateEnter_Start()
    {
        _triggeredDoorCmd = 0;
        _triggeredDoorDir = Direction.None;

        if (IsOverworld())
        {
            var behavior = GetTileBehaviorXY(Game.Link.X, Game.Link.Y + 3);
            if (behavior == TileBehavior.Cave)
            {
                Game.Link.Y += MobTileHeight;
                Game.Link.Facing = Direction.Down;

                _state.Enter.PlayerFraction = 0;
                _state.Enter.PlayerSpeed = 0x40;
                _state.Enter.PlayerPriority = SpritePriority.BelowBg;
                _state.Enter.ScrollDir = Direction.Up;
                _state.Enter.TargetX = Game.Link.X;
                _state.Enter.TargetY = Game.Link.Y - (Game.Cheats.SpeedUp ? 0 : 0x10);
                _state.Enter.Substate = EnterState.Substates.WalkCave;

                Game.Sound.StopAll();
                Game.Sound.PlayEffect(SoundEffect.Stairs);
            }
            else
            {
                _state.Enter.Substate = EnterState.Substates.Wait;
                _state.Enter.Timer = EnterState.StateTime;
            }
        }
        else if (_state.Enter.ScrollDir != Direction.None)
        {
            var uwRoomAttrs = CurrentUWRoomAttrs;
            var oppositeDir = _state.Enter.ScrollDir.GetOppositeDirection();
            var door = oppositeDir.GetOrdinal();
            var doorType = uwRoomAttrs.GetDoor(door);
            var distance = doorType is DoorType.Shutter or DoorType.Bombable ? MobTileWidth * 2 : MobTileWidth;

            _state.Enter.TargetX = Game.Link.X;
            _state.Enter.TargetY = Game.Link.Y;
            Actor.MoveSimple(
                ref _state.Enter.TargetX,
                ref _state.Enter.TargetY,
                _state.Enter.ScrollDir,
                distance);

            if (!uwRoomAttrs.IsDark() && _darkRoomFadeStep > 0)
            {
                _state.Enter.Substate = EnterState.Substates.FadeIn;
                _state.Enter.Timer = 9;
            }
            else
            {
                _state.Enter.Substate = EnterState.Substates.Walk;
            }

            Game.Link.Facing = _state.Enter.ScrollDir;
        }
        else
        {
            _state.Enter.Substate = EnterState.Substates.Wait;
            _state.Enter.Timer = EnterState.StateTime;
        }

        DoorwayDir = IsUWMain(CurRoomId) ? _state.Enter.ScrollDir : Direction.None;
    }

    private void UpdateEnter_Wait()
    {
        _state.Enter.Timer--;
        if (_state.Enter.Timer == 0)
        {
            _state.Enter.GotoPlay = true;
        }
    }

    private void UpdateEnter_FadeIn()
    {
        if (_darkRoomFadeStep == 0)
        {
            _state.Enter.Substate = EnterState.Substates.Walk;
            return;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(_state.Enter.Timer);

        if (_state.Enter.Timer > 0)
        {
            _state.Enter.Timer--;
            return;
        }

        _darkRoomFadeStep--;
        _state.Enter.Timer = 9;

        for (var i = 0; i < 2; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i + 2, _infoBlock.DarkPalette(_darkRoomFadeStep, i));
        }
        Graphics.UpdatePalettes();
    }

    private void UpdateEnter_Walk()
    {
        if (_state.Enter.HasReachedTarget(Game.Link))
        {
            _state.Enter.GotoPlay = true;
        }
        else
        {
            Game.Link.MoveLinear(_state.Enter.ScrollDir, _state.Enter.PlayerSpeed);
        }
    }

    private void UpdateEnter_WalkCave()
    {
        if (_state.Enter.HasReachedTarget(Game.Link))
        {
            _state.Enter.GotoPlay = true;
        }
        else
        {
            MovePlayer(_state.Enter.ScrollDir, _state.Enter.PlayerSpeed, ref _state.Enter.PlayerFraction);
        }
    }

    private void DrawEnter()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);

        // JOE: The C++ code base had this check but it causes a black frame to be drawn.
        // if (_state.Enter.Substate != EnterState.Substates.Start)
        DrawRoomNoObjects(_state.Enter.PlayerPriority);
    }

    public void GotoLoadLevel(int level, bool restartOW = false)
    {
        _state.LoadLevel.Level = level;
        _state.LoadLevel.Substate = LoadLevelState.Substates.Load;
        _state.LoadLevel.Timer = 0;
        _state.LoadLevel.RestartOW = restartOW;

        _curMode = GameMode.LoadLevel;
    }

    private void SetPlayerExitPosOW(int roomId)
    {
        var owRoomAttrs = GetOWRoomAttrs(roomId);
        var exitRPos = owRoomAttrs.GetExitPosition();

        var col = exitRPos & 0x0F;
        var row = (exitRPos >> 4) + 4;

        Game.Link.X = col * MobTileWidth;
        Game.Link.Y = row * MobTileHeight + 0xD;
    }

    public ReadOnlySpan<byte> GetString(StringId stringId)
    {
        return _textTable.GetItem((int)stringId);
    }

    private void UpdateLoadLevel()
    {
        switch (_state.LoadLevel.Substate)
        {
            case LoadLevelState.Substates.Load:
                _state.LoadLevel.Timer = LoadLevelState.StateTime;
                _state.LoadLevel.Substate = LoadLevelState.Substates.Wait;

                int origLevel = _infoBlock.LevelNumber;
                var origRoomId = CurRoomId;

                Game.Sound.StopAll();
                _statusBarVisible = false;
                LoadLevel(_state.LoadLevel.Level);

                // Let the Unfurl game mode load the room and reset colors.

                if (_state.LoadLevel.Level == 0)
                {
                    CurRoomId = _savedOWRoomId;
                    _savedOWRoomId = -1;
                    FromUnderground = 2;
                }
                else
                {
                    CurRoomId = _infoBlock.StartRoomId;
                    if (origLevel == 0)
                    {
                        _savedOWRoomId = origRoomId;
                    }
                }

                break;

            case LoadLevelState.Substates.Wait when _state.LoadLevel.Timer == 0:
                GotoUnfurl(_state.LoadLevel.RestartOW);
                return;

            case LoadLevelState.Substates.Wait:
                _state.LoadLevel.Timer--;
                break;
        }
    }

    private void DrawLoadLevel()
    {
        using var _ = Graphics.SetClip(0, 0, Global.StdViewWidth, Global.StdViewHeight);
        ClearScreen();
    }

    private void GotoUnfurl(bool restartOW = false)
    {
        _state.Unfurl.Substate = UnfurlState.Substates.Start;
        _state.Unfurl.Timer = UnfurlState.StateTime;
        _state.Unfurl.StepTimer = 0;
        _state.Unfurl.Left = 0x80;
        _state.Unfurl.Right = 0x80;
        _state.Unfurl.RestartOW = restartOW;

        ClearLevelData();

        _curMode = GameMode.Unfurl;
    }

    private void UpdateUnfurl()
    {
        if (_state.Unfurl.Substate == UnfurlState.Substates.Start)
        {
            _state.Unfurl.Substate = UnfurlState.Substates.Unfurl;
            _statusBarVisible = true;
            _statusBar.EnableFeatures(StatusBarFeatures.All, false);

            if (_infoBlock.LevelNumber == 0 && !_state.Unfurl.RestartOW)
            {
                LoadRoom(CurRoomId, 0);
                SetPlayerExitPosOW(CurRoomId);
            }
            else
            {
                LoadRoom(_infoBlock.StartRoomId, 0);
                Game.Link.X = StartX;
                Game.Link.Y = _infoBlock.StartY;
            }

            for (var i = 0; i < LevelInfoBlock.LevelPaletteCount; i++)
            {
                Graphics.SetPaletteIndexed((Palette)i, _infoBlock.GetPalette(i));
            }

            Profile.SetPlayerColor();
            Graphics.UpdatePalettes();
            return;
        }

        if (_state.Unfurl.Timer > 0)
        {
            _state.Unfurl.Timer--;
            return;
        }

        if (_state.Unfurl.Left == 0 || Game.Cheats.SpeedUp)
        {
            _statusBar.EnableFeatures(StatusBarFeatures.EquipmentAndMap, true);
            if (!IsOverworld())
            {
                Game.Sound.PlaySong(_infoBlock.SongId, SongStream.MainSong, true);
            }
            GotoEnter(Direction.Up);
            return;
        }

        if (_state.Unfurl.StepTimer == 0)
        {
            _state.Unfurl.Left -= 8;
            _state.Unfurl.Right += 8;
            _state.Unfurl.StepTimer = 4;
        }
        else
        {
            _state.Unfurl.StepTimer--;
        }
    }

    private void DrawUnfurl()
    {
        if (_state.Unfurl.Substate == UnfurlState.Substates.Start) return;

        var width = _state.Unfurl.Right - _state.Unfurl.Left;

        using (var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight))
        {
            ClearScreen();
        }

        using (var _ = Graphics.SetClip(_state.Unfurl.Left, TileMapBaseY, width, TileMapHeight))
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
        _state.EndLevel.Substate = EndLevelState.Substates.Start;
        _curMode = GameMode.EndLevel;
    }

    private void UpdateEndLevel()
    {
        EndLevelFuncs[(int)_state.EndLevel.Substate]();
    }

    private void UpdateEndLevel_Start()
    {
        _state.EndLevel.Substate = EndLevelState.Substates.Wait1;
        _state.EndLevel.Timer = EndLevelState.Wait1Time;

        _state.EndLevel.Left = 0;
        _state.EndLevel.Right = TileMapWidth;
        _state.EndLevel.StepTimer = 4;

        _statusBar.EnableFeatures(StatusBarFeatures.Equipment, false);
        Game.Sound.PlaySong(SongId.Triforce, SongStream.MainSong, false);
    }

    private void UpdateEndLevel_Wait()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(_state.EndLevel.Timer);

        if (_state.EndLevel.Timer > 0)
        {
            _state.EndLevel.Timer--;
            return;
        }

        if (_state.EndLevel.Substate == EndLevelState.Substates.Wait3)
        {
            GotoLoadLevel(0);
        }
        else
        {
            _state.EndLevel.Substate += 1;
            if (_state.EndLevel.Substate == EndLevelState.Substates.Flash)
            {
                _state.EndLevel.Timer = EndLevelState.FlashTime;
            }
        }
    }

    private void UpdateEndLevel_Flash()
    {
        if (_state.EndLevel.Timer == 0)
        {
            _state.EndLevel.Substate += 1;
            return;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(_state.EndLevel.Timer);

        if (!Game.Enhancements.ReduceFlashing)
        {
            var step = _state.EndLevel.Timer & 0x7;
            switch (step)
            {
                case 0: SetFlashPalette(); break;
                case 3: SetLevelPalette(); break;
            }
        }
        _state.EndLevel.Timer--;
    }

    private void UpdateEndLevel_FillHearts()
    {
        var maxHeartValue = Profile.GetMaxHeartsValue();

        Game.Sound.PlayEffect(SoundEffect.Character);

        if (Profile.Hearts == maxHeartValue)
        {
            _state.EndLevel.Substate += 1;
            _state.EndLevel.Timer = EndLevelState.Wait2Time;
        }
        else
        {
            FillHearts(6);
        }
    }

    private void UpdateEndLevel_Furl()
    {
        if (_state.EndLevel.Left == WorldMidX)
        {
            _state.EndLevel.Substate += 1;
            _state.EndLevel.Timer = EndLevelState.Wait3Time;
        }
        else if (_state.EndLevel.StepTimer == 0)
        {
            _state.EndLevel.Left += 8;
            _state.EndLevel.Right -= 8;
            _state.EndLevel.StepTimer = 4;
        }
        else
        {
            _state.EndLevel.StepTimer--;
        }
    }

    private void DrawEndLevel()
    {
        var left = 0;
        var width = TileMapWidth;

        if (_state.EndLevel.Substate >= EndLevelState.Substates.Furl)
        {
            left = _state.EndLevel.Left;
            width = _state.EndLevel.Right - _state.EndLevel.Left;

            using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
            ClearScreen();
        }

        using (var _ = Graphics.SetClip(left, TileMapBaseY, width, TileMapHeight))
        {
            DrawRoomNoObjects(SpritePriority.None);
        }

        DrawLinkLiftingItem(ItemId.TriforcePiece);
    }

    private void GotoStairs(TileBehavior behavior)
    {
        _state.Stairs.Substate = StairsState.Substates.Start;
        _state.Stairs.TileBehavior = behavior;
        _state.Stairs.PlayerPriority = SpritePriority.AboveBg;

        _curMode = GameMode.Stairs;
    }

    private void UpdateStairsState()
    {
        switch (_state.Stairs.Substate)
        {
            case StairsState.Substates.Start:
                _state.Stairs.PlayerPriority = SpritePriority.BelowBg;

                if (IsOverworld()) Game.Sound.StopAll();

                if (_state.Stairs.TileBehavior == TileBehavior.Cave)
                {
                    Game.Link.Facing = Direction.Up;

                    _state.Stairs.TargetX = Game.Link.X;
                    _state.Stairs.TargetY = Game.Link.Y + (Game.Cheats.SpeedUp ? 0 : 0x10);
                    _state.Stairs.ScrollDir = Direction.Down;
                    _state.Stairs.PlayerSpeed = 0x40;
                    _state.Stairs.PlayerFraction = 0;

                    _state.Stairs.Substate = StairsState.Substates.WalkCave;
                    Game.Sound.PlayEffect(SoundEffect.Stairs);
                }
                else
                {
                    _state.Stairs.Substate = StairsState.Substates.Walk;
                }
                break;

            case StairsState.Substates.Walk when IsOverworld():
            case StairsState.Substates.WalkCave when _state.Stairs.HasReachedTarget(Game.Link):
                var owRoomAttrs = CurrentOWRoomAttrs;
                var cave = owRoomAttrs.GetCaveId();
                _log.Write($"Cave: {cave}");

                if (cave <= 9)
                {
                    GotoLoadLevel(cave);
                }
                else
                {
                    GotoPlayCave();
                }
                break;

            case StairsState.Substates.Walk:
                GotoPlayCellar();
                break;

            case StairsState.Substates.WalkCave:
                MovePlayer(_state.Stairs.ScrollDir, _state.Stairs.PlayerSpeed, ref _state.Stairs.PlayerFraction);
                Game.Link.Animator.Advance();
                break;
        }
    }

    private void DrawStairsState()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        DrawRoomNoObjects(_state.Stairs.PlayerPriority);
    }

    private void GotoPlayCellar()
    {
        _state.PlayCellar.Substate = PlayCellarState.Substates.Start;
        _state.PlayCellar.PlayerPriority = SpritePriority.None;

        _curMode = GameMode.InitPlayCellar;
    }

    private void UpdatePlayCellar()
    {
        PlayCellarFuncs[(int)_state.PlayCellar.Substate]();
    }

    private void UpdatePlayCellar_Start()
    {
        _state.PlayCellar.Substate = PlayCellarState.Substates.FadeOut;
        _state.PlayCellar.FadeTimer = 11;
        _state.PlayCellar.FadeStep = 0;
    }

    private void UpdatePlayCellar_FadeOut()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(_state.PlayCellar.FadeTimer);

        if (_state.PlayCellar.FadeTimer > 0)
        {
            _state.PlayCellar.FadeTimer--;
            return;
        }

        for (var i = 0; i < LevelInfoBlock.FadePals; i++)
        {
            var step = _state.PlayCellar.FadeStep;
            Graphics.SetPaletteIndexed((Palette)i + 2, _infoBlock.OutOfCellarPalette(step, i));
        }
        Graphics.UpdatePalettes();
        _state.PlayCellar.FadeTimer = 9;
        _state.PlayCellar.FadeStep++;

        if (_state.PlayCellar.FadeStep == LevelInfoBlock.FadeLength)
        {
            _state.PlayCellar.Substate = PlayCellarState.Substates.LoadRoom;
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

            _state.PlayCellar.TargetY = 0x60;
            _state.PlayCellar.Substate = PlayCellarState.Substates.FadeIn;
            _state.PlayCellar.FadeTimer = 35;
            _state.PlayCellar.FadeStep = 3;
        }
        else
        {
            GotoPlay();
        }
    }

    private void UpdatePlayCellar_FadeIn()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(_state.PlayCellar.FadeTimer);

        if (_state.PlayCellar.FadeTimer > 0)
        {
            _state.PlayCellar.FadeTimer--;
            return;
        }

        for (var i = 0; i < LevelInfoBlock.FadePals; i++)
        {
            var step = _state.PlayCellar.FadeStep;
            Graphics.SetPaletteIndexed((Palette)i + 2, _infoBlock.InCellarPalette(step, i));
        }
        Graphics.UpdatePalettes();
        _state.PlayCellar.FadeTimer = 9;
        _state.PlayCellar.FadeStep--;

        if (_state.PlayCellar.FadeStep < 0)
        {
            _state.PlayCellar.Substate = PlayCellarState.Substates.Walk;
        }
    }

    private void UpdatePlayCellar_Walk()
    {
        _state.PlayCellar.PlayerPriority = SpritePriority.AboveBg;

        _traceLog.Write($"UpdatePlayCellar_Walk: Game.Link.Y >= _state.PlayCellar.TargetY {Game.Link.Y} >= {_state.PlayCellar.TargetY}");
        if (Game.Link.Y >= _state.PlayCellar.TargetY)
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
        DrawRoomNoObjects(_state.PlayCellar.PlayerPriority);
    }

    private void GotoLeaveCellar()
    {
        _state.LeaveCellar.Substate = LeaveCellarState.Substates.Start;
        _curMode = GameMode.LeaveCellar;
    }

    private void UpdateLeaveCellar()
    {
        LeaveCellarFuncs[(int)_state.LeaveCellar.Substate]();
    }

    private void UpdateLeaveCellar_Start()
    {
        if (IsOverworld())
        {
            _state.LeaveCellar.Substate = LeaveCellarState.Substates.Wait;
            _state.LeaveCellar.Timer = 29;
        }
        else
        {
            _state.LeaveCellar.Substate = LeaveCellarState.Substates.FadeOut;
            _state.LeaveCellar.FadeTimer = 11;
            _state.LeaveCellar.FadeStep = 0;
        }
    }

    private void UpdateLeaveCellar_FadeOut()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(_state.LeaveCellar.FadeTimer);

        if (_state.LeaveCellar.FadeTimer > 0)
        {
            _state.LeaveCellar.FadeTimer--;
            return;
        }

        for (var i = 0; i < LevelInfoBlock.FadePals; i++)
        {
            var step = _state.LeaveCellar.FadeStep;
            Graphics.SetPaletteIndexed((Palette)i + 2, _infoBlock.InCellarPalette(step, i));
        }
        Graphics.UpdatePalettes();
        _state.LeaveCellar.FadeTimer = 9;
        _state.LeaveCellar.FadeStep++;

        if (_state.LeaveCellar.FadeStep == LevelInfoBlock.FadeLength)
        {
            _state.LeaveCellar.Substate = LeaveCellarState.Substates.LoadRoom;
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

        _state.LeaveCellar.Substate = LeaveCellarState.Substates.FadeIn;
        _state.LeaveCellar.FadeTimer = 35;
        _state.LeaveCellar.FadeStep = 3;
    }

    private void UpdateLeaveCellar_FadeIn()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(_state.LeaveCellar.FadeTimer);

        if (_state.LeaveCellar.FadeTimer > 0)
        {
            _state.LeaveCellar.FadeTimer--;
            return;
        }

        for (var i = 0; i < LevelInfoBlock.FadePals; i++)
        {
            var step = _state.LeaveCellar.FadeStep;
            Graphics.SetPaletteIndexed((Palette)i + 2, _infoBlock.OutOfCellarPalette(step, i));
        }
        Graphics.UpdatePalettes();
        _state.LeaveCellar.FadeTimer = 9;
        _state.LeaveCellar.FadeStep--;

        if (_state.LeaveCellar.FadeStep < 0)
        {
            _state.LeaveCellar.Substate = LeaveCellarState.Substates.Walk;
        }
    }

    private void UpdateLeaveCellar_Walk()
    {
        GotoEnter(Direction.None);
    }

    private void UpdateLeaveCellar_Wait()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(_state.LeaveCellar.FadeTimer);

        if (_state.LeaveCellar.Timer > 0)
        {
            _state.LeaveCellar.Timer--;
            return;
        }

        _state.LeaveCellar.Substate = LeaveCellarState.Substates.LoadOverworldRoom;
    }

    private void UpdateLeaveCellar_LoadOverworldRoom()
    {
        for (var i = 0; i < 2; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i + 2, _infoBlock.GetPalette(i + 2));
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

        switch (_state.LeaveCellar.Substate)
        {
            case LeaveCellarState.Substates.Start:
                break;

            case LeaveCellarState.Substates.Wait:
            case LeaveCellarState.Substates.LoadOverworldRoom:
                ClearScreen();
                break;

            default:
                DrawRoomNoObjects(SpritePriority.None);
                break;
        }
    }

    private void GotoPlayCave()
    {
        _state.PlayCave.Substate = PlayCaveState.Substates.Start;

        _curMode = GameMode.InitPlayCave;
    }

    private void UpdatePlayCave()
    {
        PlayCaveFuncs[(int)_state.PlayCave.Substate]();
    }

    private void UpdatePlayCave_Start()
    {
        _state.PlayCave.Substate = PlayCaveState.Substates.Wait;
        _state.PlayCave.Timer = 27;
    }

    private void UpdatePlayCave_Wait()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(_state.PlayCave.Timer);

        if (_state.PlayCave.Timer > 0)
        {
            _state.PlayCave.Timer--;
            return;
        }

        _state.PlayCave.Substate = PlayCaveState.Substates.LoadRoom;
    }

    private void UpdatePlayCave_LoadRoom()
    {
        var paletteSet = _extraData.GetItem<PaletteSet>(Extra.CavePalettes);
        var caveLayout = FindSparseFlag(Sparse.Shortcut, CurRoomId) ? Cave.Shortcut : Cave.Items;

        LoadCaveRoom(caveLayout);

        _state.PlayCave.Substate = PlayCaveState.Substates.Walk;
        _state.PlayCave.TargetY = 0xD5;

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
        _traceLog.Write($"UpdatePlayCave_Walk: Game.Link.Y <= _state.PlayCave.TargetY {Game.Link.Y} <= {_state.PlayCave.TargetY}");
        if (Game.Link.Y <= _state.PlayCave.TargetY)
        {
            FromUnderground = 1;
            GotoPlay(RoomType.Cave);
            return;
        }

        Game.Link.MoveLinear(Direction.Up, Link.WalkSpeed);
        Game.Link.Animator.Advance();
    }

    private void DrawPlayCave()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);

        switch (_state.PlayCave.Substate)
        {
            case PlayCaveState.Substates.Wait:
            case PlayCaveState.Substates.LoadRoom:
                ClearScreen();
                break;

            case PlayCaveState.Substates.Walk:
                DrawRoomNoObjects();
                break;
        }
    }

    public void GotoDie()
    {
        _state.Death.Substate = DeathState.Substates.Start;

        _curMode = GameMode.Death;
    }

    private void UpdateDie()
    {
        // ORIGINAL: Some of these are handled with object timers.
        if (_state.Death.Timer > 0)
        {
            _state.Death.Timer--;
            // JOE: Original does not return here.
        }

        DeathFuncs[(int)_state.Death.Substate]();
    }

    private void UpdateDie_Start()
    {
        Game.Link.InvincibilityTimer = 0x10;
        _state.Death.Timer = 0x20;
        _state.Death.Substate = DeathState.Substates.Flash;
        Game.Sound.StopEffects();
        Game.Sound.PlaySong(SongId.Death, SongStream.MainSong, false);
    }

    private void UpdateDie_Flash()
    {
        Game.Link.DecInvincibleTimer();

        if (_state.Death.Timer == 0)
        {
            _state.Death.Timer = 6;
            _state.Death.Substate = DeathState.Substates.Wait1;
        }
    }

    private static readonly ImmutableArray<ImmutableArray<byte>> _deathRedPals = [
        [0x0F, 0x17, 0x16, 0x26],
        [0x0F, 0x17, 0x16, 0x26]
    ];

    private void UpdateDie_Wait1()
    {
        // TODO: the last 2 frames make the whole play area use palette 3.

        if (_state.Death.Timer == 0)
        {
            SetLevelPalettes(_deathRedPals);

            _state.Death.Step = 16;
            _state.Death.Timer = 0;
            _state.Death.Substate = DeathState.Substates.Turn;
        }
    }

    private void UpdateDie_Turn()
    {
        if (_state.Death.Step == 0)
        {
            _state.Death.Step = 4;
            _state.Death.Timer = 0;
            _state.Death.Substate = DeathState.Substates.Fade;
        }
        else
        {
            if (_state.Death.Timer == 0)
            {
                _state.Death.Timer = 5;
                _state.Death.Step--;

                ReadOnlySpan<Direction> dirs = [Direction.Down, Direction.Left, Direction.Up, Direction.Right];

                var dir = dirs[_state.Death.Step & 3];
                Game.Link.Facing = dir;
            }
        }
    }

    private void UpdateDie_Fade()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(_state.Death.Step);

        if (_state.Death.Step > 0)
        {
            if (_state.Death.Timer == 0)
            {
                _state.Death.Timer = 10;
                _state.Death.Step--;

                var seq = 3 - _state.Death.Step;

                SetLevelPalettes(_infoBlock.DeathPalettes(seq));
            }
            return;
        }

        _state.Death.Substate = DeathState.Substates.GrayLink;
    }

    private void UpdateDie_GrayLink()
    {
        ReadOnlySpan<byte> grayPal = [0, 0x10, 0x30, 0];

        Graphics.SetPaletteIndexed(Palette.Player, grayPal);
        Graphics.UpdatePalettes();

        _state.Death.Substate = DeathState.Substates.Spark;
        _state.Death.Timer = 0x18;
        _state.Death.Step = 0;
    }

    private void UpdateDie_Spark()
    {
        if (_state.Death.Timer == 0)
        {
            if (_state.Death.Step == 0)
            {
                _state.Death.Timer = 10;
                Game.Sound.PlayEffect(SoundEffect.Character);
            }
            else if (_state.Death.Step == 1)
            {
                _state.Death.Timer = 4;
            }
            else
            {
                _state.Death.Substate = DeathState.Substates.Wait2;
                _state.Death.Timer = 46;
            }
            _state.Death.Step++;
        }
    }

    private void UpdateDie_Wait2()
    {
        if (_state.Death.Timer == 0)
        {
            _state.Death.Substate = DeathState.Substates.GameOver;
            _state.Death.Timer = 0x60;
        }
    }

    private void UpdateDie_GameOver()
    {
        if (_state.Death.Timer == 0)
        {
            Profile.Deaths++;
            GotoContinueQuestion();
        }
    }

    private void DrawDie()
    {
        if (_state.Death.Substate < DeathState.Substates.GameOver)
        {
            using (var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight))
            {
                DrawRoomNoObjects(SpritePriority.None);
            }
            var player = Game.Link;

            if (_state.Death.Substate == DeathState.Substates.Spark && _state.Death.Step > 0)
            {
                GlobalFunctions.DrawSparkle(player.X, player.Y, Palette.Blue, _state.Death.Step - 1);
            }
            else if (_state.Death.Substate <= DeathState.Substates.Spark)
            {
                Game.Link.Draw();
            }
            return;
        }

        GlobalFunctions.DrawString("game over", 0x60, 0x90, 0);
    }

    public void GotoContinueQuestion()
    {
        _state.Continue.Substate = ContinueState.Substates.Start;
        _state.Continue.SelectedIndex = 0;

        _curMode = GameMode.ContinueQuestion;
    }

    private void UpdateContinueQuestion()
    {
        switch (_state.Continue.Substate)
        {
            case ContinueState.Substates.Start:
                _statusBarVisible = false;
                Game.Sound.PlaySong(SongId.GameOver, SongStream.MainSong, true);
                _state.Continue.Substate = ContinueState.Substates.Idle;
                break;

            case ContinueState.Substates.Idle:
                if (IsAnyButtonPressing(GameButton.Select, GameButton.Down))
                {
                    _state.Continue.SelectedIndex++;
                    if (_state.Continue.SelectedIndex > ContinueState.Indexes.Retry)
                    {
                        _state.Continue.SelectedIndex = 0;
                    }
                    Game.Sound.PlayEffect(SoundEffect.Cursor);
                    break;
                }

                if (IsButtonPressing(GameButton.Up))
                {
                    _state.Continue.SelectedIndex--;
                    if (_state.Continue.SelectedIndex < 0)
                    {
                        _state.Continue.SelectedIndex = ContinueState.Indexes.Retry;
                    }
                    Game.Sound.PlayEffect(SoundEffect.Cursor);
                }

                if (IsButtonPressing(GameButton.Start))
                {
                    _state.Continue.Substate = ContinueState.Substates.Chosen;
                    _state.Continue.Timer = 0x40;
                }
                break;

            case ContinueState.Substates.Chosen when _state.Continue.Timer == 0:
                _statusBarVisible = true;
                Game.Sound.StopAll();

                switch (_state.Continue.SelectedIndex)
                {
                    case ContinueState.Indexes.Continue:
                        // So, that the OW song is played in the Enter mode.
                        FromUnderground = 2;
                        Game.Link.Initialize();
                        Profile.Hearts = PlayerProfile.GetMaxHeartsValue(PlayerProfile.DefaultHearts);
                        Unpause(); // It's easy for select+start to also pause the game, and that's confusing.
                        GotoUnfurl(true);
                        break;

                    case ContinueState.Indexes.Save:
                        SaveFolder.SaveProfiles();
                        GotoFileMenu();
                        break;

                    case ContinueState.Indexes.Retry:
                        GotoFileMenu();
                        break;
                }
                break;

            case ContinueState.Substates.Chosen:
                _state.Continue.Timer--;
                break;
        }
    }

    private void DrawContinueQuestion()
    {
        ReadOnlySpan<string> options = ["Continue", "Save", "Retry"];

        ClearScreen();

        var y = 0x50;

        for (var i = 0; i < 3; i++, y += 24)
        {
            var pal = 0;
            if (_state.Continue.Substate == ContinueState.Substates.Chosen
                && (int)_state.Continue.SelectedIndex == i)
            {
                pal = (Game.FrameCounter / 4) & 1;
            }

            GlobalFunctions.DrawString(options[i], 0x50, y, (Palette)pal);
        }

        y = 0x50 + ((int)_state.Continue.SelectedIndex * 24);
        GlobalFunctions.DrawChar(Char.FullHeart, 0x40, y, Palette.RedFgPalette);
    }

    private void GotoFileMenu()
    {
        GotoFileMenu(SaveFolder.Profiles.Profiles);
    }

    public void GotoFileMenu(List<PlayerProfile> summaries, int page = 0)
    {
        _nextGameMenu = new ProfileSelectMenu(Game, summaries, page);
        _curMode = GameMode.GameMenu;
    }

    public void GotoRegisterMenu(List<PlayerProfile> summaries)
    {
        _nextGameMenu = new RegisterMenu(Game, summaries);
        _curMode = GameMode.Register;
    }

    public void GotoEliminateMenu(List<PlayerProfile> summaries, int page)
    {
        _nextGameMenu = new EliminateMenu(Game, summaries, page);
        _curMode = GameMode.Elimination;
    }

    private void UpdateGameMenu() => _gameMenu.Update();
    private void UpdateRegisterMenu() => _gameMenu.Update();
    private void UpdateEliminateMenu() => _gameMenu.Update();
    private void DrawGameMenu() => _gameMenu?.Draw();

    private int FindCellarRoomId(int mainRoomId, out bool isLeft)
    {
        isLeft = false;
        for (var i = 0; i < LevelInfoBlock.LevelCellarCount; i++)
        {
            var cellarRoomId = _infoBlock.CellarRoomIds[i];
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

        CommonMakeObjectAction(ObjType.FlyingGhini, row, col, interaction, ref _ghostCount, _ghostCells);
    }

    public void ArmosTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction == TileInteraction.Push) Debug.WriteLine("Push armos: {0}, {1}", row, col);

        CommonMakeObjectAction(ObjType.Armos, row, col, interaction, ref _armosCount, _armosCells);
    }

    public void CommonMakeObjectAction(
        ObjType type, int row, int col, TileInteraction interaction, ref int patchCount, Cell[] patchCells)
    {
        switch (interaction)
        {
            case TileInteraction.Load:
                if (patchCount < 16)
                {
                    patchCells[patchCount] = new Cell((byte)row, (byte)col);
                    patchCount++;
                }
                break;

            case TileInteraction.Push:
                var map = _tileMaps[_curTileMapIndex];
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
                break;
        }
    }

    public void MakeActivatedObject(ObjType type, int row, int col)
    {
        row += BaseRows;

        var x = col * TileWidth;
        var y = row * TileHeight;

        for (var i = (int)ObjectSlot.LastMonster; i >= 0; i--)
        {
            var obj = _objects[i];
            if (obj == null || obj.ObjType != type) continue;

            var objCol = obj.X / TileWidth;
            var objRow = obj.Y / TileHeight;

            if (objCol == col && objRow == row) return;
        }

        var freeSlot = FindEmptyMonsterSlot();
        if (freeSlot >= 0)
        {
            var obj = Actor.FromType(type, Game, x, y);
            _objects[(int)freeSlot] = obj;
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
                switch (player.ObjTimer)
                {
                    case 0:
                        player.ObjTimer = 0x18;
                        break;

                    case 1:
                        LeaveRoom(player.Facing, CurRoomId);
                        player.Stop();
                        break;
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
                if (_triggeredDoorDir == Direction.None)
                {
                    if (UseKey())
                    {
                        // $8ADA
                        _triggeredDoorDir = player.MovingDirection;
                        _triggeredDoorCmd = 8;
                    }
                }
                break;
        }
    }
}