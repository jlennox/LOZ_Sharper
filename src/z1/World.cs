using System.Collections.Immutable;
using System.Diagnostics;
using z1.Actors;
using z1.IO;
using z1.Render;
using z1.UI;

namespace z1;

internal enum TileInteraction { Load, Push, Touch, Cover }
internal enum SpritePriority { None, AboveBg, BelowBg }
internal enum SubmenuState { IdleClosed, StartOpening, EndOpening = 7, IdleOpen, StartClose }

internal enum GameMode
{
    Demo,
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
    Stairs,
    Death,
    EndLevel,
    WinGame,

    InitPlayCellar,
    InitPlayCave,

    Max,
}

internal enum StunTimerSlot
{
    NoSword,
    RedLeever,
    ObservedPlayer,
    EdgeObject
}

internal record Cell(byte Y, byte X)
{
    public const int MobPatchCellCount = 16;
    public static Cell[] MakeMobPatchCell() => new Cell[MobPatchCellCount];
}

internal sealed partial class World
{
    public const int ScreenTileWidth = 32;
    public const int ScreenTileHeight = 22;
    public const int ScreenBlockWidth = 16;
    public const int BlockWidth = 16;
    public const int BlockHeight = 16;
    public const int TileWidth = 8;
    public const int TileHeight = 8;
    public const int TileMapWidth = ScreenTileWidth * TileWidth;

    public const int WorldLimitTop = TileMapBaseY;
    public const int WorldLimitBottom = WorldLimitTop + TileMapHeight;
    public const int WorldMidX = WorldLimitLeft + TileMapWidth / 2;
    private const int WorldLimitLeft = 0;
    private const int WorldLimitRight = TileMapWidth;
    public const int WorldWidth = 16;
    public const int WorldHeight = 8;

    public const int BaseRows = 8;
    private const int TileMapHeight = ScreenTileHeight * TileHeight;
    public const int TileMapBaseY = 0x40;

    private const int StartX = 0x78;

    private const int ScrollSpeed = 4;
    private const int RoomHistoryLength = 6;

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

    private const int UWBombRadius = 32;

    private enum PauseState { Unpaused, Paused, FillingHearts }

    private static readonly DebugLog _traceLog = new(nameof(World), DebugLogDestination.DebugBuildsOnly);
    private static readonly DebugLog _log = new(nameof(World), DebugLogDestination.DebugBuildsOnly);

    public Game Game { get; }
    public Player Player => Game.Player;
    public SubmenuType Menu;
    public int RoomObjCount;           // 34E
    public Actor? RoomObj;              // 35F
    public bool EnablePersonFireballs;
    public bool SwordBlocked;           // 52E
    public byte WhirlwindTeleporting;   // 522
    public Direction DoorwayDir;         // 53
    // JOE: TODO: Stick this on Player?
    public int FromUnderground;    // 5A
    public int ActiveShots;        // 34C
    public bool CandleUsed;         // 513
    // JOE: NOTE: Ultimately this (and others, like CandleUsed) needs to be owned by Player so that multiple Players are possible.
    public PlayerProfile Profile { get; private set; }

    private LevelInfoEx _extraData;
    private readonly RoomHistory _roomHistory;
    private readonly GameWorld _overworldWorld;
    private readonly Dictionary<GameWorldType, GameWorld> _commonWorlds = [];
    public GameWorld CurrentWorld;
    public GameRoom CurrentRoom;
    public PersistedRoomState CurrentPersistedRoomState => CurrentRoom.PersistedRoomState;

    private int _rowCount;
    private int _colCount;
    private int _startRow;
    private int _startCol;
    // JOE: TODO: Move to rect.
    public int MarginRight;
    public int MarginLeft;
    public int MarginBottom;
    public int MarginTop;
    private GLImage _doorsBmp;

    private GameMode _lastMode;
    private GameMode _curMode;
    private readonly StatusBar _statusBar;
    private CreditsType? _credits;
    private TextBox? _textBox1;
    private TextBox? _textBox2;

    private readonly WorldState _state = new();
    private int _curColorSeqNum;
    private int _darkRoomFadeStep;
    private int _curMazeStep;
    private int _spotIndex;
    private GameRoom? _tempShutterRoom;
    private Direction _tempShutterDoorDir;
    private bool _tempShutters;
    private GameRoom? _savedOWRoom;
    private int _edgeX;
    private int _edgeY;

    private byte _worldKillCycle;         // 52A
    private byte _worldKillCount;         // 627
    private byte _helpDropCounter;        // 50
    private byte _helpDropValue;          // 51
    private int _roomKillCount;          // 34F
    private bool _roomAllDead;            // 34D
    private byte _teleportingRoomIndex;   // 523
    private PauseState _pause;                  // E0
    private SubmenuState _submenu;                // E1
    private int _submenuOffsetY;         // EC
    private bool _statusBarVisible;

    private bool _giveFakePlayerPos;
    private Point _fakePlayerPos;

    private readonly List<Actor> _objects = new();
    private readonly Dictionary<ObjectTimer, int> _objectTimers = new();
    private int _longTimer;
    private readonly Dictionary<StunTimerSlot, int> _stunTimers = new();
    private readonly List<ObjType> _pendingEdgeSpawns = new();

    private int _triggeredDoorCmd;   // 54
    private Direction _triggeredDoorDir;   // 55

    // JOE: TODO: ActiveShots doesn't need to be reference counted anymore and should be based on the object table.
    // Though note that ones owned by Player should be excluded.
    private bool _triggerShutters;    // 4CE
    private bool _summonedWhirlwind;  // 508
    private bool _powerTriforceFanfare;   // 509
    // private Direction _shuttersPassedDirs; // 519 // JOE: NOTE: Delete this, it's unused.
    private bool _brightenRoom;       // 51E

    private readonly bool _dummyWorld;

    public Rectangle PlayAreaRect { get; set; }

    public World(Game game)
    {
        _dummyWorld = true;
        Game = game;

        _overworldWorld = GameWorld.Load(game, "Maps/Overworld.world", 1);
        CurrentWorld = _overworldWorld;
        _commonWorlds[GameWorldType.OverworldCommon] = GameWorld.Load(game, "Maps/OverworldCommon.world", 1);
        _commonWorlds[GameWorldType.UnderworldCommon] = GameWorld.Load(game, "Maps/UnderworldCommon.world", 1);

        _roomHistory = new RoomHistory(game, RoomHistoryLength);
        _statusBar = new StatusBar(this);
        Menu = new SubmenuType(game);

        _lastMode = GameMode.Demo;
        _curMode = GameMode.Play;
        _edgeY = 0x40;

        Validate();

        PlayAreaRect = new Rectangle(0, TileMapBaseY, ScreenTileWidth * TileWidth, TileMapHeight);
        LoadOpenRoomContext();

        void LoadOpenRoomContext()
        {
            _colCount = 32;
            _rowCount = 22;
            _startRow = 0;
            _startCol = 0;
            MarginRight = OWMarginRight;
            MarginLeft = OWMarginLeft;
            MarginBottom = OWMarginBottom;
            MarginTop = OWMarginTop;
        }
    }

    public World(Game game, PlayerProfile profile)
        : this(game)
    {
        // I'm not fond of _dummyWorld, but I want to keep Game.World and World.Profile to not be nullable
        _dummyWorld = false;
        Init(profile);
    }

    // This irl should be moved over to tests.
    private void Validate()
    {
        // Ensure there's one defined for each.
        foreach (var action in Enum.GetValues<TileAction>()) GetTileActionFunction(action);
        foreach (var action in Enum.GetValues<DoorType>()) GetDoorFace(action);
    }

    private void Init(PlayerProfile profile)
    {
        _extraData = new Asset("overworldInfoEx.json").ReadJson<LevelInfoEx>();
        _doorsBmp = Graphics.CreateImage(new Asset("underworldDoors.png"));

        Profile = profile;
        Profile.Hearts = PlayerProfile.GetMaxHeartsValue(PlayerProfile.DefaultHeartCount);

        GotoLoadLevel(0, true);
    }

    public void Update()
    {
        if (_dummyWorld) throw new Exception("This version of the world should never be run.");

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
                    Game.Player.Stop();
                }
            }

            _lastMode = mode;
        }

        switch (_curMode)
        {
            case GameMode.Demo: /* no-op */ break;
            case GameMode.LoadLevel: UpdateLoadLevel(); break;
            case GameMode.Unfurl: UpdateUnfurl(); break;
            case GameMode.Enter: UpdateEnter(); break;
            case GameMode.Play: UpdatePlay(); break;
            case GameMode.Leave: UpdateLeave(); break;
            case GameMode.Scroll: UpdateScroll(); break;
            case GameMode.ContinueQuestion: UpdateContinueQuestion(); break;
            case GameMode.PlayCellar: UpdatePlay(); break;
            case GameMode.LeaveCellar: UpdateLeaveCellar(); break;
            case GameMode.PlayCave: UpdatePlay(); break;
            case GameMode.PlayShortcuts: /* no-op */ break;
            case GameMode.Stairs: UpdateStairsState(); break;
            case GameMode.Death: UpdateDie(); break;
            case GameMode.EndLevel: UpdateEndLevel(); break;
            case GameMode.WinGame: UpdateWinGame(); break;
            case GameMode.InitPlayCellar: UpdatePlayCellar(); break;
            case GameMode.InitPlayCave: UpdatePlayCave(); break;
            default: throw new ArgumentOutOfRangeException(nameof(_curMode), _curMode, "Invalid game mode.");
        }
    }

    public void Draw()
    {
        if (_dummyWorld) throw new Exception("This version of the world should never be run.");
        if (_statusBarVisible)
        {
            _statusBar.Draw(_submenuOffsetY);
        }

        switch (_curMode)
        {
            case GameMode.Demo: /* no-op */ break;
            case GameMode.LoadLevel: DrawLoadLevel(); break;
            case GameMode.Unfurl: DrawUnfurl(); break;
            case GameMode.Enter: DrawEnter(); break;
            case GameMode.Play: DrawPlay(); break;
            case GameMode.Leave: DrawLeave(); break;
            case GameMode.Scroll: DrawScroll(); break;
            case GameMode.ContinueQuestion: DrawContinueQuestion(); break;
            case GameMode.PlayCellar: DrawPlay(); break;
            case GameMode.LeaveCellar: DrawLeaveCellar(); break;
            case GameMode.PlayCave: DrawPlay(); break;
            case GameMode.PlayShortcuts: /* no-op */ break;
            case GameMode.Stairs: DrawStairsState(); break;
            case GameMode.Death: DrawDie(); break;
            case GameMode.EndLevel: DrawEndLevel(); break;
            case GameMode.WinGame: DrawWinGame(); break;
            case GameMode.InitPlayCellar: DrawPlayCellar(); break;
            case GameMode.InitPlayCave: DrawPlayCave(); break;
            default: throw new ArgumentOutOfRangeException(nameof(_curMode), _curMode, "Invalid game mode.");
        }
    }

    private bool IsButtonPressing(GameButton button) => Game.Input.IsButtonPressing(button);
    private bool IsAnyButtonPressing(GameButton a, GameButton b) => Game.Input.IsAnyButtonPressing(a, b);
    private void DrawRoom() => DrawMap(CurrentRoom, 0, 0);
    public void PauseFillHearts() => _pause = PauseState.FillingHearts;
    public void LeaveRoom(Direction dir, GameRoom currentRoom)
    {
        if (!TryGetNextRoom(currentRoom, dir, out _))
        {
            GotoLeaveCellar();
            return;
        }

        GotoLeave(dir, currentRoom);

        // switch (currentRoom.World.WorldType)
        // {
        //     case GameWorldType.OverworldCommon:
        //     case GameWorldType.UnderworldCommon:
        //     case GameWorldType.Underworld:
        //         GotoLeaveCellar();
        //         break;
        //
        //     default:
        //         GotoLeave(dir, currentRoom);
        //         break;
        // }
    }
    public void LeaveCellar() => GotoLeaveCellar();

    public void LeaveCellarByShortcut(GameRoom targetRoom)
    {
        CurrentRoom = targetRoom;
        // JOE: TODO: MAP REWRITE TakeShortcut();
        LeaveCellar();
    }

    public void UnfurlLevel() => GotoUnfurl();
    private bool IsPlaying() => IsPlaying(_curMode);
    private static bool IsPlaying(GameMode mode) => mode is GameMode.Play or GameMode.PlayCave or GameMode.PlayCellar or GameMode.PlayShortcuts;

    public GameMode GetMode() => _curMode switch
    {
        GameMode.InitPlayCave => GameMode.PlayCave,
        GameMode.InitPlayCellar => GameMode.PlayCellar,
        _ => _curMode
    };

    public Point GetObservedPlayerPos() => _fakePlayerPos;
    public LadderActor? GetLadder() => GetObject<LadderActor>();
    public void SetLadder(LadderActor ladder) => AddOnlyObjectOfType(ladder);
    public void RemoveLadder() => RemoveObject<LadderActor>();

    public void UseRecorder()
    {
        Game.Sound.PushSong(SongId.Recorder);
        SetObjectTimer(ObjectTimer.RecorderMusic, 0x98);

        if (!IsPlaying()) return;

        // Expected behaviors:
        //
        // Level 7 entrance:
        // - The full song plays out. Player can't move during this time, but unfreezes when the song is done.
        // - The pond animates colors. Player can not walk over the water until the animation is done.
        // - The staircase appears.
        //
        // Whirlwind:
        // - A spawning cloud appears on the screen's edge where the whirlwind will come in from.
        // - The full song plays out. Player can't move during this time.
        // - The whirlwind enters the screen.

        var shouldSummonWhirlwind = true;
        foreach (var obj in GetObjects<InteractiveGameObjectActor>())
        {
            // If any action spots support the recorder, we should not summon the whirlwind.
            if (obj.NontargetedAction(Interaction.Recorder)) shouldSummonWhirlwind = false;
        }

        if (!shouldSummonWhirlwind) return;
        if (!CurrentWorld.Settings.AllowWhirlwind) return;

        SummonWhirlwind();
    }

    private void SummonWhirlwind()
    {
        if (!_summonedWhirlwind
            && WhirlwindTeleporting == 0
            && IsOverworld()
            && IsPlaying()
            && !CurrentRoom.IsCave
            && GetItem(ItemSlot.TriforcePieces) != 0)
        {
            ReadOnlySpan<byte> teleportRoomIds = [0x36, 0x3B, 0x73, 0x44, 0x0A, 0x21, 0x41, 0x6C];

            var whirlwind = new WhirlwindActor(Game, 0, Game.Player.Y);
            AddObject(whirlwind);

            _summonedWhirlwind = true;
            _teleportingRoomIndex = GetNextTeleportingRoomIndex();
            // JOE: TODO: MAP REWRITE whirlwind.SetTeleportPrevRoomId(teleportRoomIds[_teleportingRoomIndex]);
        }
    }

    private TileBehavior GetTileBehavior(int tileY, int tileX) // Arg, these are not x/y ordered.
    {
        return CurrentRoom.RoomMap.AsBehaviors(tileX, tileY);
    }

    private TileBehavior GetTileBehaviorXY(int x, int y)
    {
        var tileX = x / TileWidth;
        var tileY = (y - TileMapBaseY) / TileHeight;

        return GetTileBehavior(tileY, tileX);
    }

    public void SetMapObjectXY(int x, int y, TileType mobIndex) => SetMapObjectXY(x, y, (BlockType)mobIndex);

    public void SetMapObjectXY(int x, int y, BlockType mobIndex)
    {
        var fineTileX = x / TileWidth;
        var fineTileY = (y - TileMapBaseY) / TileHeight;

        if (fineTileX is < 0 or >= ScreenTileWidth || fineTileY is < 0 or >= ScreenTileHeight) return;

        SetMapObject(fineTileY, fineTileX, mobIndex);
    }

    private void SetMapObject(int tileY, int tileX, BlockType mobIndex)
    {
        var map = CurrentRoom.RoomMap;
        // _loadMapObjectFunc(ref map, tileY, tileX, (byte)mobIndex); // JOE: FIXME: BlockObjTypes
        // map.SetBlock(tileX, tileY, new TiledTile(1));
        if (!CurrentRoom.TryGetBlockObjectTiles(mobIndex, out var tileObject))
        {
            throw new Exception($"Unable to locate BlockObjType {mobIndex}");
        }

        map.SetBlock(tileX, tileY, tileObject);

        for (var currentTileY = tileY; currentTileY < tileY + 2; currentTileY++)
        {
            for (var currentTileX = tileX; currentTileX < tileX + 2; currentTileX++)
            {
                var tile = map[currentTileX, currentTileY];
                map.Behavior(currentTileX, currentTileY) = CurrentRoom.GetBehavior(tile); // JOE: TODO: Map conversion. Is this right?
            }
        }
    }

    public IEnumerable<Actor> GetObjects() => _objects;
    public T? GetObject<T>() where T : Actor => _objects.OfType<T>().FirstOrDefault(); // JOE: De-linq this.
    public T? GetObject<T>(Func<T, bool> pred) where T : Actor => _objects.OfType<T>().FirstOrDefault(pred); // JOE: De-linq this.
    public Actor? GetObject(Func<Actor, bool> pred) => GetObject<Actor>(pred);
    public IEnumerable<T> GetObjects<T>() where T : Actor => _objects.OfType<T>(); // JOE: De-linq this.
    public IEnumerable<T> GetObjects<T>(Func<T, bool> pred) where T : Actor => _objects.OfType<T>().Where(pred); // JOE: De-linq this.
    public IEnumerable<Actor> GetObjects(Func<Actor, bool> pred) => GetObjects<Actor>(pred);
    public int CountObjects<T>() where T : Actor => GetObjects<T>().Count();
    public int CountObjects() => _objects.Count;

    public bool HasObject<T>() where T : Actor => GetObjects<T>().Any();

    public void AddObject(Actor obj)
    {
        _traceLog.Write($"AddObject({obj.ObjType}); {obj.X:X2},{obj.Y:X2}");
        _objects.Add(obj);
    }

    public void AddUniqueObject(Actor obj)
    {
        if (!_objects.Contains(obj)) AddObject(obj);
    }

    public void AddOnlyObject(Actor? old, Actor obj)
    {
        _traceLog.Write($"AddOnlyObject({old?.ObjType}, {obj.ObjType}); {obj.X:X2},{obj.Y:X2}");
        AddObject(obj);
        old?.Delete();
    }

    public void RemoveObject<T>()
        where T : Actor
    {
        for (var i = _objects.Count - 1; i >= 0; i--)
        {
            if (_objects[i] is T actor)
            {
                _traceLog.Write($"RemoveObject({actor.ObjType}); {actor.X:X2},{actor.Y:X2}");
                actor.Delete();
                _objects.RemoveAt(i);
                return;
            }
        }
    }

    public int GetObjectTimer(ObjectTimer slot) => _objectTimers.GetValueOrDefault(slot);
    public void SetObjectTimer(ObjectTimer slot, int value) => _objectTimers[slot] = value;
    public int GetStunTimer(StunTimerSlot slot) => _stunTimers.GetValueOrDefault(slot);
    public void SetStunTimer(StunTimerSlot slot, int value) => _stunTimers[slot] = value;
    public void PushTile(int row, int col) => InteractTile(row, col, TileInteraction.Push);
    // private void TouchTile(int row, int col) => InteractTile(row, col, TileInteraction.Touch);
    // public void CoverTile(int row, int col) => InteractTile(row, col, TileInteraction.Cover);

    private void InteractTile(int row, int col, TileInteraction interaction)
    {
        if (row < 0 || col < 0 || row >= ScreenTileHeight || col >= ScreenTileWidth) return;

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

    public void OnActivatedArmos(int x, int y)
    {
        // JOE: TODO: MAP REWRITE var pos = FindSparsePos2(Sparse.ArmosStairs, CurrentRoom);
        // JOE: TODO: MAP REWRITE
        // JOE: TODO: MAP REWRITE if (pos != null && x == pos.Value.x && y == pos.Value.y)
        // JOE: TODO: MAP REWRITE {
        // JOE: TODO: MAP REWRITE     SetMapObjectXY(x, y, BlockObjType.Stairs);
        // JOE: TODO: MAP REWRITE     Game.Sound.PlayEffect(SoundEffect.Secret);
        // JOE: TODO: MAP REWRITE }
        // JOE: TODO: MAP REWRITE else
        // JOE: TODO: MAP REWRITE {
        // JOE: TODO: MAP REWRITE     SetMapObjectXY(x, y, BlockObjType.Ground);
        // JOE: TODO: MAP REWRITE }
        // JOE: TODO: MAP REWRITE
        // JOE: TODO: MAP REWRITE if (!CurrentRoom.PersistedRoomState.ItemState)
        // JOE: TODO: MAP REWRITE {
        // JOE: TODO: MAP REWRITE     var roomItem = FindSparseItem(Sparse.ArmosItem, CurrentRoom);
        // JOE: TODO: MAP REWRITE
        // JOE: TODO: MAP REWRITE     if (roomItem != null && x == roomItem.Value.x && y == roomItem.Value.y)
        // JOE: TODO: MAP REWRITE     {
        // JOE: TODO: MAP REWRITE         var itemObj = new ItemObjActor(Game, roomItem.Value.AsItemId, true, roomItem.Value.x, roomItem.Value.y);
        // JOE: TODO: MAP REWRITE         AddOnlyObjectOfType(itemObj);
        // JOE: TODO: MAP REWRITE     }
        // JOE: TODO: MAP REWRITE }
    }

    public void OnTouchedPowerTriforce()
    {
        _powerTriforceFanfare = true;
        Game.Player.SetState(PlayerState.Paused);
        Game.Player.ObjTimer = 0xC0;

        ReadOnlySpan<byte> palette = [0, 0x0F, 0x10, 0x30];
        Graphics.SetPaletteIndexed(Palette.SeaPal, palette);
        Graphics.UpdatePalettes();
    }

    private void CheckPowerTriforceFanfare()
    {
        if (!_powerTriforceFanfare) return;

        if (Game.Player.ObjTimer == 0)
        {
            _powerTriforceFanfare = false;
            Game.Player.SetState(PlayerState.Idle);
            AddItem(ItemId.PowerTriforce);
            GlobalFunctions.SetPilePalette();
            Graphics.UpdatePalettes();
            Game.Sound.PlaySong(SongId.Level9, SongStream.MainSong, true);
        }
        else
        {
            var timer = Game.Player.ObjTimer;
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

            switch (Profile.SelectedItem)
            {
                case ItemSlot.Potion: Profile.SelectedItem = ItemSlot.Letter; break;
                case ItemSlot.Letter: Profile.SelectedItem = ItemSlot.Food; break;
                case ItemSlot.Bombs: Profile.SelectedItem = ItemSlot.Boomerang; break;
                case ItemSlot.Boomerang: Profile.SelectedItem = ItemSlot.Rod; break;
                default: Profile.SelectedItem--; break;
            }
        }
    }

    private void WarnLowHPIfNeeded()
    {
        if (Game.Enhancements.DisableLowHealthWarning) return;
        if (Profile.Hearts >= 0x100) return;

        Game.Sound.PlayEffect(SoundEffect.LowHp);
    }

    private void PlayAmbientSounds()
    {
        var playedSound = false;

        var ambientSound = CurrentRoom.Settings.AmbientSound;
        if (ambientSound != null)
        {
            // JOE: TODO: This does sadly limit the usage of the boss roar, and I'm not sure how it behaves in the overworld.
            // Instead have a "killed boss" flag?
            var isBossRoar = ambientSound.Value is SoundEffect.BossRoar1 or SoundEffect.BossRoar2 or SoundEffect.BossRoar3;
            if (!isBossRoar || CurrentWorld.IsBossAlive)
            {
                Game.Sound.PlayEffect(ambientSound.Value, true, Sound.AmbientInstance);
                playedSound = true;
            }
        }

        if (!playedSound)
        {
            Game.Sound.StopEffects();
        }
    }

    public void ShowShortcutStairs(GameRoom room)
    {
        // JOE: TODO: MAP REWRITE var index = owRoomAttrs.GetShortcutStairsIndex();
        // JOE: TODO: MAP REWRITE var pos = _infoBlock.ShortcutPosition[index];
        // JOE: TODO: MAP REWRITE GetRoomCoord(pos, out var tileY, out var tileX);
        // JOE: TODO: MAP REWRITE SetMapObject(tileY * 2, tileX * 2, BlockObjType.Stairs);
    }

    private void DrawDoors(GameRoom room, bool above, int offsetX, int offsetY)
    {
        if (!room.HasUnderworldDoors) return;

        var outerPalette = CurrentRoom.Settings.OuterPalette;
        var baseY = above ? DoorOverlayBaseY : DoorUnderlayBaseY;
        var flags = CurrentRoom.PersistedRoomState;

        foreach (var direction in TiledRoomProperties.DoorDirectionOrder)
        {
            var doorType = room.UnderworldDoors[direction];
            var doorState = flags.GetDoorState(direction);
            if (_tempShutterDoorDir != 0
                && room == _tempShutterRoom
                && doorType == DoorType.Shutter)
            {
                if (direction == _tempShutterDoorDir)
                {
                    doorState = true;
                }
            }
            if (doorType == DoorType.Shutter && _tempShutters && _tempShutterRoom == room)
            {
                doorState = true;
            }
            var doorfaces = GetDoorFace(doorType);
            var doorface = doorfaces.GetState(doorState);
            if (doorface == DoorState.None) continue;

            var doorPos = _doorPos[direction];
            Graphics.DrawImage(
                _doorsBmp,
                DoorWidth * (int)doorface,
                doorPos.SourceY + baseY,
                DoorWidth,
                DoorHeight,
                doorPos.X + offsetX,
                doorPos.Y + offsetY,
                outerPalette,
                0);
        }
    }

    public bool HasItem(ItemSlot itemSlot) => GetItem(itemSlot) > 0;
    public int GetItem(ItemSlot itemSlot) => Profile.Items[itemSlot];
    public void SetItem(ItemSlot itemSlot, int value) => Profile.Items[itemSlot] = value;

    private void PostRupeeChange(int value, ItemSlot itemSlot)
    {
        if (itemSlot is not (ItemSlot.RupeesToAdd or ItemSlot.RupeesToSubtract))
        {
            throw new ArgumentOutOfRangeException(nameof(itemSlot), itemSlot, "Invalid itemSlot for PostRupeeChange.");
        }

        var profile = Profile;
        var curValue = profile.Items[itemSlot];
        var newValue = Math.Clamp(curValue + value, 0, 255);

        switch (itemSlot)
        {
            case ItemSlot.RupeesToAdd: profile.Statistics.RupeesCollected += value; break;
            case ItemSlot.RupeesToSubtract: profile.Statistics.RupeesSpent += value; break;
        }

        profile.Items[itemSlot] = newValue;
    }

    public void PostRupeeWin(int value) => PostRupeeChange(value, ItemSlot.RupeesToAdd);
    public void PostRupeeLoss(int value) => PostRupeeChange(value, ItemSlot.RupeesToSubtract);

    public void FillHearts(int heartValue)
    {
        var profile = Profile;
        var maxHeartValue = profile.Items[ItemSlot.HeartContainers] << 8;

        profile.Hearts += heartValue;
        if (profile.Hearts >= maxHeartValue)
        {
            profile.Hearts = maxHeartValue - 1;
        }
    }

    public void AddItem(ItemId itemId, int? amount = null)
    {
        if ((int)itemId >= (int)ItemId.MAX) return;

        GlobalFunctions.PlayItemSound(Game, itemId);
        var profile = Profile;

        var equip = ItemToEquipment[itemId];
        var slot = equip.Slot;
        var value = amount ?? equip.Value;

        var max = -1;
        if (equip.MaxValue.HasValue) max = equip.MaxValue.Value;
        if (equip.Max.HasValue) max = profile.Items[equip.Max.Value];

        if (slot == ItemSlot.RupeesToAdd)
        {
            PostRupeeWin(value);
            return;
        }

        if (itemId is ItemId.Heart or ItemId.Fairy)
        {
            var heartValue = value << 8;
            FillHearts(heartValue);
            return;
        }
        else if (slot is ItemSlot.Keys or ItemSlot.HeartContainers or ItemSlot.MaxBombs or ItemSlot.Bombs)
        {
            value += (byte)profile.Items[slot];

        }
        else if (itemId is ItemId.Compass or ItemId.Map)
        {
            profile.SetDungeonItem(CurrentWorld.Settings.LevelNumber, itemId);
            return;
        }
        else if (itemId == ItemId.TriforcePiece)
        {
            var bit = 1 << (CurrentWorld.Settings.LevelNumber - 1);
            value = (byte)(profile.Items[ItemSlot.TriforcePieces] | bit);
            profile.SetDungeonItem(CurrentWorld.Settings.LevelNumber, itemId);
        }

        if (max > 0) value = Math.Min(value, max);

        profile.Items[slot] = value;

        if (slot == ItemSlot.Ring)
        {
            profile.SetPlayerColor();
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

    public bool HasCurrentMap() => Profile.GetDungeonItem(CurrentWorld.Settings.LevelNumber, ItemId.Map);
    public bool HasCurrentCompass() => Profile.GetDungeonItem(CurrentWorld.Settings.LevelNumber, ItemId.Compass);

    private bool GetEffectiveDoorState(GameRoom room, Direction doorDir)
    {
        // TODO: the original game does it a little different, by looking at $EE.
        var type = room.UnderworldDoors[doorDir];
        return room.PersistedRoomState.GetDoorState(doorDir)
            || (type == DoorType.Shutter && _tempShutters && room == _tempShutterRoom) // JOE: I think doing object instance comparisons is fine?
            || (_tempShutterDoorDir == doorDir && room == _tempShutterRoom);
    }

    private bool GetEffectiveDoorState(Direction doorDir) => GetEffectiveDoorState(CurrentRoom, doorDir);
    public WorldSettings GetLevelInfo() => CurrentWorld.Settings;
    public bool IsOverworld() => CurrentWorld.IsOverworld;

    public Actor DebugSpawnItem(ItemId itemId)
    {
        return AddItem(itemId, Game.Player.X, Game.Player.Y - TileHeight);
    }

    public void DebugSpawnCave(Func<CaveSpec[], CaveSpec> getSpec)
    {
        MakePersonRoomObjects(getSpec(_extraData.CaveSpec), null);
    }

    public void DebugClearHistory()
    {
        CurrentWorld.ResetLevelKillCounts();
        _roomHistory.Clear();
    }

    public ReadOnlySpan<GameRoom> GetShortcutRooms()
    {
        // JOE: TODO: MAP REWRITE var valueArray = _sparseRoomAttrs.GetItems<byte>(Sparse.Shortcut);
        // JOE: TODO: MAP REWRITE // elemSize is at 1, but we don't need it.
        // JOE: TODO: MAP REWRITE return valueArray[2..(2 + valueArray[0])];
        throw new Exception();
    }

    // JOE: TODO: MAP REWRITE private void TakeShortcut() => GetRoomFlags(CurrentRoom).ShortcutState = true;
    // JOE: TODO: MAP REWRITE public void TakeSecret() => GetRoomFlags(CurrentRoom).SecretState = true;

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

        Game.Player.SetState(PlayerState.Paused);
    }

    public bool IsLiftingItem()
    {
        if (!IsPlaying()) return false;

        return _state.Play.LiftItemId != 0;
    }

    public void OpenShutters()
    {
        _tempShutters = true;
        _tempShutterRoom = CurrentRoom;
        Game.Sound.PlayEffect(SoundEffect.Door);

        foreach (var direction in TiledRoomProperties.DoorDirectionOrder)
        {
            if (CurrentRoom.UnderworldDoors[direction] == DoorType.Shutter)
            {
                UpdateDoorTileBehavior(CurrentRoom, direction);
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

    internal enum ObjectTimer
    {
        Fade,
        Monster1,
        RecorderMusic,
        Door
    }

    public void FadeIn()
    {
        if (_darkRoomFadeStep == 0)
        {
            _brightenRoom = false;
            return;
        }

        var timer = GetObjectTimer(ObjectTimer.Fade);

        if (timer == 0)
        {
            _darkRoomFadeStep--;

            for (var i = 0; i < 2; i++)
            {
                Graphics.SetPaletteIndexed((Palette)(i + 2), CurrentWorld.Settings.DarkPalette[_darkRoomFadeStep][i]);
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

    public ObjectAttribute GetObjectAttribute(ObjType type)
    {
        if (!_extraData.ObjectAttribute.TryGetValue(type, out var objAttr))
        {
            // throw new ArgumentOutOfRangeException(nameof(type), type, "Unable to locate object attributes.");
            // This is mutable which makes me hate not instancing something new here :)
            return ObjectAttribute.Default;
        }
        return objAttr;
    }

    public int GetObjectMaxHP(ObjType type)
    {
        var objAttr = GetObjectAttribute(type);
        return objAttr.HitPoints;
    }

    public int GetPlayerDamage(ObjType type)
    {
        var objAttr = GetObjectAttribute(type);
        return objAttr.Damage;
    }

    public void LoadOverworldRoom(int x, int y) => LoadRoom(CurrentWorld.GameWorldMap.RoomGrid[x, y] ?? throw new Exception("Invalid room coordinates."));

    private void LoadRoom(GameRoom room)
    {
        // This feels like a mess :/
        CurrentRoom = room;
        CurrentWorld = room.World;

        LoadMap(room);

        if (IsOverworld())
        {
            if (room.PersistedRoomState.ShortcutState)
            {
                // JOE: TODO: MAP REWRITE if (FindSparseFlag(Sparse.Shortcut, roomId))
                // JOE: TODO: MAP REWRITE {
                // JOE: TODO: MAP REWRITE     ShowShortcutStairs(room);
                // JOE: TODO: MAP REWRITE }
            }

            // if (!CurrentRoom.PersistedRoomState.ItemState)
            {
                // JOE: TODO: OBJECT REWRITE if (CurrentRoom.TryGetActionObject(TileAction.Item, out var itemObject))
                // JOE: TODO: OBJECT REWRITE {
                // JOE: TODO: OBJECT REWRITE     itemObject.GetScreenTileCoordinates(out var tileX, out var tileY);
                // JOE: TODO: OBJECT REWRITE     var itemId = itemObject.ItemId ?? throw new Exception($"Item object at {tileX},{tileY} has no item ID in \"{room.Id}\" in world \"{CurrentWorld.Name}\"");
                // JOE: TODO: OBJECT REWRITE     var itemObj = new ItemObjActor(Game, itemId, true, tileX, tileY);
                // JOE: TODO: OBJECT REWRITE     AddOnlyObjectOfType(itemObj);
                // JOE: TODO: OBJECT REWRITE }
            }
        }
        else
        {
            // if (!CurrentRoom.PersistedRoomState.ItemState)
            // {
            //     if (room.Secret is not (Secret.FoesItem or Secret.LastBoss))
            //     {
            //         AddUWRoomItem(room);
            //     }
            // }
        }
    }

    public void AddUWRoomItem() => AddUWRoomItem(CurrentRoom);

    private void AddUWRoomItem(GameRoom room)
    {
        // JOE: TODO: MAP REWRITE var itemId = room.ItemId;
        // JOE: TODO: MAP REWRITE
        // JOE: TODO: MAP REWRITE if (itemId != ItemId.None)
        // JOE: TODO: MAP REWRITE {
        // JOE: TODO: MAP REWRITE     var pos = room.ItemPosition;
        // JOE: TODO: MAP REWRITE     var itemObj = new ItemObjActor(Game, itemId, ItemObjActorOptions.IsRoomItem, pos.X, pos.Y);
        // JOE: TODO: MAP REWRITE     AddOnlyObjectOfType(itemObj);
        // JOE: TODO: MAP REWRITE
        // JOE: TODO: MAP REWRITE     if (room.Secret is Secret.FoesItem or Secret.LastBoss)
        // JOE: TODO: MAP REWRITE     {
        // JOE: TODO: MAP REWRITE         Game.Sound.PlayEffect(SoundEffect.RoomItem);
        // JOE: TODO: MAP REWRITE     }
        // JOE: TODO: MAP REWRITE }
    }

    private void LoadCaveRoom(Entrance entrance)
    {
        // JOE: TODO: Major rewrite here.
        // LoadLayout((int)uniqueRoomId); // JOE: TODO: Map rewrite. This feels super wrong.
    }

    private void UpdateDoorTileBehavior(GameRoom room, Direction doorDir)
    {
        var map = CurrentRoom.RoomMap;
        var doorOrd = doorDir.GetOrdinal();
        var corner = _doorCorners[doorOrd];
        var type = room.UnderworldDoors[doorDir];
        var effectiveDoorState = GetEffectiveDoorState(room, doorDir);
        var behavior = _doorBehaviors[(int)type].GetBehavior(effectiveDoorState);

        map.SetBlockBehavior(corner.X, corner.Y, behavior);

        if (behavior == TileBehavior.Doorway)
        {
            corner = _behindDoorCorners[doorOrd];
            map.SetBlockBehavior(corner.X, corner.Y, behavior);
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

    private void GotoPlay(ObjectState? entranceRoomsState = null, Entrance? fromEntrence = null)
    {
        _curMode = CurrentRoom switch
        {
            { IsCave: true } => GameMode.PlayCave,
            { IsCellar: true } => GameMode.PlayCellar,
            _ => GameMode.Play,
        };

        _curColorSeqNum = 0;
        _tempShutters = false;
        RoomObjCount = 0;
        RoomObj = null;
        _roomKillCount = 0;
        _roomAllDead = false;
        EnablePersonFireballs = false;
        ActiveShots = 0;

        _state.Play.Reset();

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
        MakeObjects(Game.Player.Facing, entranceRoomsState, fromEntrence);
        MakeWhirlwind();
        _roomHistory.AddRoomToHistory();

        CurrentRoom.PersistedRoomState.VisitState = true;
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
            if (IsButtonPressing(GameButton.ItemNext)) Menu.SelectNextItem();
            if (IsButtonPressing(GameButton.ItemPrevious)) Menu.SelectPreviousItem();
            if (IsButtonPressing(GameButton.ItemBoomerang)) Menu.SelectItem(ItemSlot.Boomerang);
            if (IsButtonPressing(GameButton.ItemBombs)) Menu.SelectItem(ItemSlot.Bombs);
            if (IsButtonPressing(GameButton.ItemArrow)) Menu.SelectItem(ItemSlot.Arrow);
            if (IsButtonPressing(GameButton.ItemCandle)) Menu.SelectItem(ItemSlot.Candle);
            if (IsButtonPressing(GameButton.ItemRecorder)) Menu.SelectItem(ItemSlot.Recorder);
            if (IsButtonPressing(GameButton.ItemFood)) Menu.SelectItem(ItemSlot.Food);
            if (IsButtonPressing(GameButton.ItemLetter)) Menu.SelectItem(ItemSlot.Letter);
            if (IsButtonPressing(GameButton.ItemRod)) Menu.SelectItem(ItemSlot.Rod);

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

        // Freeze all things until the recorder music is finished.
        if (GetObjectTimer(ObjectTimer.RecorderMusic) != 0) return;

        if (_pause == PauseState.FillingHearts)
        {
            FillHeartsStep();
            return;
        }

        if (_state.Play.AnimatingRoomColors)
        {
            UpdateRoomColors();
        }

        CheckBombableUWWalls();
        UpdateRupees();
        UpdateLiftItem();

        Game.Player.DecInvincibleTimer();
        Game.Player.Update();

        // The player's update might have changed the world's State.
        if (!IsPlaying()) return;

        UpdateObservedPlayerPos();

        // not sure why these are done backward.
        for (var i = _objects.Count - 1; i >= 0; i--)
        {
            var obj = _objects[i];
            if (obj.IsDeleted)
            {
                _objects.RemoveAt(i);
                continue;
            }

            if (obj.DecoratedUpdate())
            {
                HandleNormalObjectDeath(obj);
            }
        }

        for (var i = _pendingEdgeSpawns.Count - 1; i >= 0; i--)
        {
            if (PutEdgeObject(_pendingEdgeSpawns[i]))
            {
                _pendingEdgeSpawns.RemoveAt(i);
            }
        }

        DeleteDeadObjects();

        CheckSecrets();
        CheckShutters();
        UpdateDoors2();
        UpdateStatues();
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
        if (!CurrentRoom.HasUnderworldDoors) return;
        if (!_triggerShutters) return;

        _triggerShutters = false;
        var dirs = Direction.None;

        foreach (var direction in TiledRoomProperties.DoorDirectionOrder)
        {
            if (CurrentRoom.UnderworldDoors[direction] == DoorType.Shutter
                && !GetEffectiveDoorState(direction))
            {
                dirs |= direction;
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
        if (!CurrentRoom.HasUnderworldDoors) return;

        if (GetMode() == GameMode.EndLevel
            || GetObjectTimer(ObjectTimer.Door) != 0
            || _triggeredDoorCmd == 0)
        {
            return;
        }

        if ((_triggeredDoorCmd & 1) == 0)
        {
            _triggeredDoorCmd++;
            SetObjectTimer(ObjectTimer.Door, 8);
            return;
        }

        if ((_triggeredDoorDir & (Direction)0x10) != 0)
        {
            OpenShutters();
        }

        var flags = CurrentRoom.PersistedRoomState;

        foreach (var dir in TiledRoomProperties.DoorDirectionOrder)
        {
            if ((_triggeredDoorDir & dir) == 0) continue;

            var type = CurrentRoom.UnderworldDoors[dir];
            if (!type.IsLockedType()) continue;
            if (flags.GetDoorState(dir)) continue;

            var oppositeDir = dir.GetOppositeDirection();
            if (!TryGetNextRoom(CurrentRoom, dir, out var nextRoom))
            {
                _log.Error("Attempted to move to invalid room.");
                return;
            }

            flags.SetDoorState(dir);
            nextRoom.PersistedRoomState.SetDoorState(oppositeDir);
            if (type != DoorType.Bombable)
            {
                Game.Sound.PlayEffect(SoundEffect.Door);
            }
            UpdateDoorTileBehavior(CurrentRoom, dir);
        }

        _triggeredDoorCmd = 0;
        _triggeredDoorDir = Direction.None;
    }

    private byte GetNextTeleportingRoomIndex()
    {
        var facing = Game.Player.Facing;
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
            // var posAttr = FindSparsePos(Sparse.Recorder, CurRoomId);
            // if (posAttr != null)
            // {
            //     GetRoomCoord(posAttr.Value.pos, out var row, out var col);
            //     SetMob(row * 2, col * 2, BlockObjType.MobStairs);
            //     Game.Sound.PlayEffect(SoundEffect.Secret);
            // }
            _state.Play.CompletePondDryoutEvent();
            return;
        }

        if ((_state.Play.Timer % 8) == 0)
        {
            var colorSeq = _extraData.OWPondColors;
            if (_curColorSeqNum < colorSeq.Length - 1)
            {
                if (_curColorSeqNum == colorSeq.Length - 2)
                {
                    CurrentRoom.RoomMap.UpdateTileBehavior(TileBehavior.Water, TileBehavior.GenericWalkable);
                }

                int colorIndex = colorSeq[_curColorSeqNum];
                _curColorSeqNum++;
                // JOE: The ordering on these appears wrong. colorIndex should be the second argument?
                Graphics.SetColorIndexed((Palette)3, 3, colorIndex);
                Graphics.UpdatePalettes();
            }
        }

        _state.Play.Timer--;
    }

    private void CheckBombableUWWalls()
    {
        if (!CurrentRoom.HasUnderworldDoors) return;

        foreach (var bomb in Game.World.GetObjects<BombActor>())
        {
            if (bomb.BombState != BombState.Fading) continue;

            // JOE: Why the + 8...?
            var bombX = bomb.X + 8;
            var bombY = bomb.Y + 8;

            foreach (var direction in TiledRoomProperties.DoorDirectionOrder)
            {
                var doorType = CurrentRoom.UnderworldDoors[direction];
                if (doorType != DoorType.Bombable) continue;

                var doorState = CurrentRoom.PersistedRoomState.GetDoorState(direction);
                if (doorState) continue;

                var doorMiddle = _doorMiddles[direction];
                if (Math.Abs(bombX - doorMiddle.X) < UWBombRadius
                    && Math.Abs(bombY - doorMiddle.Y) < UWBombRadius)
                {
                    _triggeredDoorCmd = 6;
                    _triggeredDoorDir = direction;
                    Profile.Statistics.UWWallsBombed++;
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
        return GetObjects().Any(static monster => !monster.IsDeleted && monster.CountsAsLiving);
    }

    private void CheckSecrets()
    {
        if (IsOverworld()) return;

        if (!_roomAllDead)
        {
            if (!CalcHasLivingObjects())
            {
                Game.Player.ClearParalized();
                _roomAllDead = true;
            }
        }
    }

    public void KillAllObjects()
    {
        foreach (var monster in GetObjects())
        {
            if (monster.ObjType < ObjType.PersonEnd && monster.Decoration == 0)
            {
                // JOE: TODO: This is too cryptic. Have a "Kill" on Actor? Have "Decoration" be an enum as well?
                monster.Decoration = 0x10;
            }
        }
    }

    public void DebugKillAllObjects()
    {
        foreach (var monster in _objects)
        {
            monster.Delete();
        }
        _objects.Clear();
    }

    private void UpdateStatues()
    {
        // if (IsOverworld()) return;

        var pattern = EnablePersonFireballs ? 2 : CurrentRoom.FireballLayout;

        if (pattern != null)
        {
            Statues.Update(Game, pattern.Value);
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
        _tempShutterRoom = null;
        _tempShutterDoorDir = 0;
        _tempShutters = false;
        WhirlwindTeleporting = 0;
        ActiveShots = 0;

        _roomKillCount = 0;
        _roomAllDead = false;
        EnablePersonFireballs = false;
    }

    private static bool IsRecurringFoe(ObjType type)
    {
        return type is < ObjType.OneDodongo or ObjType.RedLamnola or ObjType.BlueLamnola or >= ObjType.Trap;
    }

    private void SaveObjectCount()
    {
        var flags = CurrentRoom.PersistedRoomState;

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
                        CurrentRoom.LevelKillCount += _roomKillCount;
                        var count = CurrentRoom.LevelKillCount < 3 ? CurrentRoom.LevelKillCount : 2;
                        flags.ObjectCount = (byte)count;
                        return;
                    }
                }
            }

            CurrentRoom.LevelKillCount = 0xF;
            flags.ObjectCount = 3;
        }
    }

    private void CalcObjCountToMake(ref ObjType type, ref int count)
    {
        var flags = CurrentRoom.PersistedRoomState;

        if (IsOverworld())
        {
            if (!_roomHistory.IsRoomInHistory() && (flags.ObjectCount == 7))
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
            if (_roomHistory.IsRoomInHistory() || flags.ObjectCount != 3)
            {
                if (count < CurrentRoom.LevelKillCount)
                {
                    type = ObjType.None;
                    count = 0;
                }
                else
                {
                    count -= CurrentRoom.LevelKillCount;
                }
                return;
            }

            if (IsRecurringFoe(type))
            {
                flags.ObjectCount = 0;
                CurrentRoom.LevelKillCount = 0;
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
            _fakePlayerPos.X = Game.Player.X;
            _fakePlayerPos.Y = Game.Player.Y;
        }

        // ORIGINAL: This happens after player items update and before the rest of objects update.
        var timer = GetStunTimer(StunTimerSlot.ObservedPlayer);
        if (timer != 0) return;

        SetStunTimer(StunTimerSlot.ObservedPlayer, Game.Random.Next(0, 8));

        _giveFakePlayerPos = !_giveFakePlayerPos;
        if (_giveFakePlayerPos)
        {
            if (_fakePlayerPos.X == Game.Player.X)
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
            Game.Player.SetState(PlayerState.Idle);
        }
        else
        {
            Game.Player.SetState(PlayerState.Paused);
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
            DrawPlayerLiftingItem(_state.Play.LiftItemId);
        }
        else
        {
            Game.Player.Draw();
        }

        objOverPlayer?.DecoratedDraw();

        DrawDoors(CurrentRoom, true, 0, 0);
    }

    private void DrawSubmenu()
    {
        using (var _ = Graphics.SetClip(0, TileMapBaseY + _submenuOffsetY, TileMapWidth, TileMapHeight - _submenuOffsetY))
        {
            ClearScreen();
            DrawMap(CurrentRoom, 0, _submenuOffsetY);
        }

        DrawDoors(CurrentRoom, true, 0, _submenuOffsetY);
        Menu.Draw(_submenuOffsetY);
    }

    private void DrawObjects(out Actor? objOverPlayer)
    {
        objOverPlayer = null;

        foreach (var obj in _objects)
        {
            if (obj.IsDeleted) continue;

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

    private void DrawPrincessLiftingTriforce(int x, int y)
    {
        var image = Graphics.GetSpriteImage(TileSheet.Boss9, AnimationId.B3_Princess_Lift);
        image.Draw(TileSheet.Boss9, x, y, Palette.Player);

        GlobalFunctions.DrawItem(Game, ItemId.TriforcePiece, x, y - 0x10, 0);
    }

    private void DrawPlayerLiftingItem(ItemId itemId)
    {
        var animIndex = itemId == ItemId.TriforcePiece ? AnimationId.PlayerLiftHeavy : AnimationId.PlayerLiftLight;
        var image = Graphics.GetSpriteImage(TileSheet.PlayerAndItems, animIndex);
        image.Draw(TileSheet.PlayerAndItems, Game.Player.X, Game.Player.Y, Palette.Player);

        GlobalFunctions.DrawItem(Game, itemId, Game.Player.X, Game.Player.Y - 0x10, 0);
    }

    private void MakeObjects(Direction entryDir, ObjectState? entranceRoomsState, Entrance? fromEntrence)
    {
        // JOE: TODO: MAP REWRITE if (IsUWCellar(CurrentRoom))
        // JOE: TODO: MAP REWRITE {
        // JOE: TODO: MAP REWRITE     MakeCellarObjects();
        // JOE: TODO: MAP REWRITE     return;
        // JOE: TODO: MAP REWRITE }

        // I'm... not entirely sure what happens when both CaveSpec's hit?
        if (/*_curMode == GameMode.PlayCave && */fromEntrence?.Cave != null)
        {
            MakePersonRoomObjects(fromEntrence.Cave, entranceRoomsState);
        }

        if (CurrentRoom.CaveSpec != null)
        {
            // The nameof isn't my favorite here...
            var state = CurrentRoom.PersistedRoomState.GetObjectState(nameof(CaveSpec));
            MakePersonRoomObjects(CurrentRoom.CaveSpec, state);
        }

        var monstersEnterFromEdge = CurrentRoom.MonstersEnter;
        var monsterList = CurrentRoom.Monsters;
        var monsterCount = monsterList.Length;

        // Zoras are a bit special and are never not spawned.
        for (var i = 0; i < CurrentRoom.ZoraCount; i++)
        {
            Actor.AddFromType(ObjType.Zora, Game, 0, 0);
        }

        if (monsterCount == 0) return;

        // It's kind of weird how this is handled in the actual game.
        var firstObject = monsterList[0].ObjType;

        CalcObjCountToMake(ref firstObject, ref monsterCount);

        RoomObjCount = monsterCount;
        var roomObj = GetObject<ItemObjActor>();

        var dirOrd = entryDir.GetOrdinal();
        var spots = _extraData.SpawnSpot.AsSpan();
        var spotsLen = spots.Length / 4;
        var dirSpots = spots[(spotsLen * dirOrd)..];

        var x = 0;
        var y = 0;
        for (var i = 0; i < monsterCount; i++)
        {
            var entry = monsterList[i];
            var type = entry.ObjType;

            var cannotEdgeSpawn = type is ObjType.Zora or ObjType.Armos or ObjType.StandingFire or ObjType.Whirlwind;
            if (monstersEnterFromEdge && !cannotEdgeSpawn)
            {
                _pendingEdgeSpawns.Add(type);
                continue;
            }

            if (!FindSpawnPos(type, dirSpots, spotsLen, ref x, ref y))
            {
                _log.Error($"Couldn't find spawn position for {type}.");
                continue;
            }

            var obj = Actor.AddFromType(type, Game, x, y);
            if (obj is MonsterActor mactor && entry.IsRingleader) mactor.IsRingleader = true;

            // The NES logic would only set HoldingItem for the first object.
            if (i == 0)
            {
                RoomObj = obj; // JOE: I'm not sure what RoomObj is for...?

                if (obj.CanHoldRoomItem && roomObj != null)
                {
                    roomObj.X = obj.X;
                    roomObj.Y = obj.Y;
                    obj.HoldingItem = roomObj;
                }
            }
        }
    }

    // private void MakeCellarObjects()
    // {
    //     const int startY = 0x9D;
    //
    //     ReadOnlySpan<int> startXs = [0x20, 0x60, 0x90, 0xD0];
    //
    //     foreach (var x in startXs)
    //     {
    //         Actor.AddFromType(ObjType.BlueKeese, Game, x, startY);
    //     }
    // }

    private void MakePersonRoomObjects(CaveSpec spec, ObjectState? state)
    {
        ReadOnlySpan<int> fireXs = [0x48, 0xA8];

        if (spec.DwellerType != CaveDwellerType.None)
        {
            // JOE: TODO: Fix CaveId.Cave1.
            var person = new PersonActor(Game, state, CaveId.Cave1, spec, 0x78, 0x80);
            AddObject(person);
        }

        for (var i = 0; i < 2; i++)
        {
            var fire = new StandingFireActor(Game, fireXs[i], 0x80);
            AddObject(fire);
        }
    }

    // JOE: TODO: This does not work in cellar/cave because of the use of _state.Play.
    public DeferredEvent DryoutWater()
    {
        if (!IsPlaying()) return DeferredEvent.CompletedEvent;

        _state.Play.AnimatingRoomColors = true;
        _state.Play.Timer = 88;
        return _state.Play.CreatePondDryoutEvent();
    }

    private void MakeWhirlwind()
    {
        ReadOnlySpan<int> teleportYs = [0x8D, 0xAD, 0x8D, 0x8D, 0xAD, 0x8D, 0xAD, 0x5D];

        if (WhirlwindTeleporting != 0)
        {
            var y = teleportYs[_teleportingRoomIndex];

            WhirlwindTeleporting = 2;

            var whirlwind = new WhirlwindActor(Game, 0, y);
            AddObject(whirlwind);

            Game.Player.SetState(PlayerState.Paused);
            Game.Player.X = whirlwind.X;
            Game.Player.Y = 0xF8;
        }
    }

    private bool FindSpawnPos(ObjType type, ReadOnlySpan<PointXY> spots, int len, ref int x, ref int y)
    {
        var objAttrs = GetObjectAttribute(type);

        var playerX = Game.Player.X;
        var playerY = Game.Player.Y;
        var noWorldCollision = !objAttrs.HasWorldCollision;

        for (var i = 0; i < len; i++)
        {
            var point = spots[_spotIndex];
            x = point.X;
            y = point.Y;
            _spotIndex = (_spotIndex + 1) % len;

            if ((playerX != x || playerY != y)
                && (noWorldCollision || !CollidesWithTileStill(x, y)))
            {
                return true;
            }
        }

        return false;
    }

    private bool PutEdgeObject(ObjType placeholder)
    {
        var timer = GetStunTimer(StunTimerSlot.EdgeObject);
        if (timer != 0) return false;

        timer = Game.Random.Next(0, 4) + 2;
        SetStunTimer(StunTimerSlot.EdgeObject, timer);

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
        const int playerBoundary = 0x22;
        if (Math.Abs(Game.Player.X - x) >= playerBoundary
            || Math.Abs(Game.Player.Y - y) >= playerBoundary)
        {
            // Bring them in from the edge of the screen if player isn't too close.
            var obj = Actor.AddFromType(placeholder, Game, x, y - 3);
            obj.Decoration = 0;
            return true;
        }

        return false;
    }

    private void HandleNormalObjectDeath(Actor obj)
    {
        var x = obj.X;
        var y = obj.Y;

        _objects.Remove(obj);

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
        if (origType.HoldingItem != null) return;

        var objClass = origType.Attributes.ItemDropClass;
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

            var r = Game.Random.GetByte();
            var rate = classRates[objClass];
            if (r >= rate) return;

            var classIndex = classBases[objClass] + _worldKillCycle;
            itemId = (ItemId)dropItems[classIndex];
        }

        AddItem(itemId, x, y);
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

        _state.Scroll.CurrentRoom = CurrentRoom;
        _state.Scroll.ScrollDir = dir;
        _state.Scroll.Substate = ScrollState.Substates.Start;
        _curMode = GameMode.Scroll;
    }

    private void GotoScroll(Direction dir, GameRoom currentRoom)
    {
        GotoScroll(dir);
        _state.Scroll.CurrentRoom = currentRoom;
    }

    private void UpdateScroll()
    {
        switch (_state.Scroll.Substate)
        {
            case ScrollState.Substates.Start: ScrollStart(Game, ref _state.Scroll); break;
            case ScrollState.Substates.AnimatingColors: ScrollAnimatingColors(Game, ref _state.Scroll); break;
            case ScrollState.Substates.FadeOut: ScrollFadeOut(Game, ref _state.Scroll); break;
            case ScrollState.Substates.LoadRoom: ScrollLoadRoom(Game, ref _state.Scroll); break;
            case ScrollState.Substates.Scroll: ScrollScroll(Game, ref _state.Scroll); break;
            default: throw new Exception($"Unknown ScrollState \"{_state.Scroll.Substate}\"");
        }
        return;

        static void ScrollStart(Game game, ref ScrollState state)
        {
            if (CalcMazeStayPut(game.World.CurrentRoom.Maze, game.Sound, state.ScrollDir, ref game.World._curMazeStep))
            {
                state.NextRoom = state.CurrentRoom;
            }
            else
            {
                // if (!TryGetNextRoom(CurrentRoom, state.ScrollDir, out var nextRoom))
                // {
                //     if (!TryTakePreviousEntrance(out var previousEntrance))
                //     {
                //         previousEntrance = new RoomHistoryEntry(
                //             _overworldWorld.EntryRoom, _overworldWorld, new Entrance());
                //     }
                //
                //     state.NextRoom = state.CurrentRoom;
                //     _log.Error("Attempted to move to invalid room.");
                //     return;
                // }

                state.NextRoom = game.World.GetNextRoom(state.ScrollDir, out _);
            }

            state.Substate = ScrollState.Substates.AnimatingColors;
        }

        static void ScrollAnimatingColors(Game game, ref ScrollState state)
        {
            if (game.World._curColorSeqNum == 0)
            {
                state.Substate = ScrollState.Substates.LoadRoom;
                return;
            }

            if ((game.FrameCounter & 4) != 0)
            {
                game.World._curColorSeqNum--;

                var colorSeq = game.World._extraData.OWPondColors;
                int color = colorSeq[game.World._curColorSeqNum];
                Graphics.SetColorIndexed((Palette)3, 3, color);
                Graphics.UpdatePalettes();

                if (game.World._curColorSeqNum == 0)
                {
                    state.Substate = ScrollState.Substates.LoadRoom;
                }
            }
        }

        static void ScrollFadeOut(Game game, ref ScrollState state)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(state.Timer);

            if (state.Timer > 0)
            {
                state.Timer--;
                return;
            }

            var darkPalette = game.World.CurrentWorld.Settings.DarkPalette;
            for (var i = 0; i < 2; i++)
            {
                Graphics.SetPaletteIndexed((Palette)i + 2, darkPalette[game.World._darkRoomFadeStep][i]);
            }
            Graphics.UpdatePalettes();

            game.World._darkRoomFadeStep++;

            if (game.World._darkRoomFadeStep == 4)
            {
                state.Substate = ScrollState.Substates.Scroll;
                state.Timer = ScrollState.StateTime;
            }
            else
            {
                state.Timer = 9;
            }
        }

        static void ScrollLoadRoom(Game game, ref ScrollState state)
        {
            if (state.ScrollDir == Direction.Down
                && !game.World.IsOverworld()
                && game.World.CurrentRoom == game.World.CurrentWorld.EntryRoom)
            {
                game.World.GotoLoadLevel(0);
                return;
            }

            state.OffsetX = 0;
            state.OffsetY = 0;
            state.SpeedX = 0;
            state.SpeedY = 0;
            state.OldMapToNewMapDistX = 0;
            state.OldMapToNewMapDistY = 0;

            switch (state.ScrollDir)
            {
                case Direction.Left:
                    state.OffsetX = -TileMapWidth;
                    state.SpeedX = ScrollSpeed;
                    state.OldMapToNewMapDistX = TileMapWidth;
                    break;

                case Direction.Right:
                    state.OffsetX = TileMapWidth;
                    state.SpeedX = -ScrollSpeed;
                    state.OldMapToNewMapDistX = -TileMapWidth;
                    break;

                case Direction.Up:
                    state.OffsetY = -TileMapHeight;
                    state.SpeedY = ScrollSpeed;
                    state.OldMapToNewMapDistY = TileMapHeight;
                    break;

                case Direction.Down:
                    state.OffsetY = TileMapHeight;
                    state.SpeedY = -ScrollSpeed;
                    state.OldMapToNewMapDistY = -TileMapHeight;
                    break;
            }

            state.OldRoom = game.World.CurrentRoom;

            var nextRoom = state.NextRoom;

            game.World._tempShutterRoom = nextRoom;
            game.World._tempShutterDoorDir = state.ScrollDir.GetOppositeDirection();

            game.World.LoadRoom(nextRoom);

            if (game.World.CurrentRoom.Settings.IsDark
                && game.World._darkRoomFadeStep == 0
                && !game.World.Profile.PreventDarkRooms(game))
            {
                state.Substate = ScrollState.Substates.FadeOut;
                state.Timer = Game.Cheats.SpeedUp ? 1 : 9;
            }
            else
            {
                state.Substate = ScrollState.Substates.Scroll;
                state.Timer = Game.Cheats.SpeedUp ? 1 : ScrollState.StateTime;
            }
        }

        static void ScrollScroll(Game game, ref ScrollState state)
        {
            if (state.Timer > 0)
            {
                state.Timer--;
                return;
            }

            if (state is { OffsetX: 0, OffsetY: 0 })
            {
                game.World.GotoEnter(state.ScrollDir);
                if (state.NextRoom.Settings.PlaysSecretChime)
                {
                    game.Sound.PlayEffect(SoundEffect.Secret);
                }
                return;
            }

            var speedMultiplier = Game.Cheats.SpeedUp ? 2 : 1;
            var speedX = state.SpeedX * speedMultiplier;
            var speedY = state.SpeedY * speedMultiplier;

            state.OffsetX += speedX;
            state.OffsetY += speedY;

            // JOE: TODO: Does this prevent screen wrapping?
            var playerLimits = Player.PlayerLimits;
            if (state.SpeedX != 0)
            {
                game.Player.X = Math.Clamp(game.Player.X + speedX, playerLimits[1], playerLimits[0]);
            }
            else
            {
                game.Player.Y = Math.Clamp(game.Player.Y + speedY, playerLimits[3], playerLimits[2]);
            }

            game.Player.Animator.Advance();
        }
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

                DrawMap(CurrentRoom, _state.Scroll.OffsetX, _state.Scroll.OffsetY);
                DrawMap(_state.Scroll.OldRoom, oldMapOffsetX, oldMapOffsetY);
            }
            else
            {
                DrawMap(CurrentRoom, 0, 0);
            }
        }

        if (IsOverworld())
        {
            Game.Player.Draw();
        }
    }

    // Returns false if the next room should stay the same as the current room.
    private static bool CalcMazeStayPut(MazeRoom? maze, Sound sound, Direction dir, ref int currentMazeStep)
    {
        if (maze == null) return false;

        if (dir == maze.ExitDirection)
        {
            currentMazeStep = 0;
            return false;
        }

        if (dir != maze.Path[currentMazeStep])
        {
            currentMazeStep = 0;
            return true;
        }

        currentMazeStep++;
        if (currentMazeStep != maze.Path.Length)
        {
            return true;
        }

        currentMazeStep = 0;
        sound.PlayEffect(SoundEffect.Secret);
        return false;
    }

    private void GotoLeave(Direction dir)
    {
        if (dir == Direction.None) throw new ArgumentOutOfRangeException(nameof(dir));

        _state.Leave.CurrentRoom = CurrentRoom;
        _state.Leave.ScrollDir = dir;
        _state.Leave.Timer = LeaveState.StateTime;
        _curMode = GameMode.Leave;
    }

    private void GotoLeave(Direction dir, GameRoom currentRoom)
    {
        GotoLeave(dir);
        _state.Leave.CurrentRoom = currentRoom;
    }

    private void UpdateLeave()
    {
        UpdateLeave(Game, ref _state.Leave);
    }

    private static void UpdateLeave(Game game, ref LeaveState state)
    {
        var playerLimits = Player.PlayerLimits;
        var dirOrd = game.Player.Facing.GetOrdinal();
        var coord = game.Player.Facing.IsVertical() ? game.Player.Y : game.Player.X;

        if (coord != playerLimits[dirOrd])
        {
            game.Player.MoveLinear(state.ScrollDir, Player.WalkSpeed);
            game.Player.Animator.Advance();
            return;
        }

        if (state.Timer == 0)
        {
            game.Player.Animator.AdvanceFrame();
            game.World.GotoScroll(state.ScrollDir, state.CurrentRoom);
            return;
        }

        state.Timer--;
    }

    private void DrawLeave()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        DrawRoomNoObjects();
    }

    private void MovePlayer(Direction dir, int speed, ref int fraction)
    {
        fraction += speed;
        var carry = fraction >> 8;
        fraction &= 0xFF;

        var x = Game.Player.X;
        var y = Game.Player.Y;
        Actor.MoveSimple(ref x, ref y, dir, carry);

        Game.Player.X = x;
        Game.Player.Y = y;
    }

    private void GotoEnter(Direction dir)
    {
        _state.Enter.Substate = EnterState.Substates.Start;
        _state.Enter.ScrollDir = dir;
        _state.Enter.Timer = 0;
        _state.Enter.PlayerPriority = SpritePriority.AboveBg;
        _state.Enter.PlayerSpeed = Player.WalkSpeed;
        _state.Enter.GotoPlay = false;
        Unpause();
        _curMode = GameMode.Enter;
    }

    private void UpdateEnter()
    {
        switch (_state.Enter.Substate)
        {
            case EnterState.Substates.Start: EnterStart(Game, ref _state.Enter); break;
            case EnterState.Substates.Wait: EnterWait(Game, ref _state.Enter); break;
            case EnterState.Substates.FadeIn: EnterFadeIn(Game, ref _state.Enter); break;
            case EnterState.Substates.Walk: EnterWalk(Game, ref _state.Enter); break;
            case EnterState.Substates.WalkCave: EnterWalkCave(Game, ref _state.Enter); break;
            default: throw new Exception($"Unknown EnterState \"{_state.Enter.Substate}\"");
        }

        if (_state.Enter.GotoPlay)
        {
            var origShutterDoorDir = _tempShutterDoorDir;
            _tempShutterDoorDir = Direction.None;
            if (CurrentRoom.HasUnderworldDoors
                && origShutterDoorDir != Direction.None
                && CurrentRoom.UnderworldDoors[origShutterDoorDir] == DoorType.Shutter)
            {
                Game.Sound.PlayEffect(SoundEffect.Door);
                UpdateDoorTileBehavior(CurrentRoom, origShutterDoorDir);
            }

            _statusBar.EnableFeatures(StatusBarFeatures.All, true);
            if (IsOverworld() && FromUnderground != 0)
            {
                Game.Sound.PlaySong(CurrentWorld.Settings.SongId, SongStream.MainSong, true);
            }
            GotoPlay();
            return;
        }

        Game.Player.Animator.Advance();
        return;

        static void EnterStart(Game game, ref EnterState state)
        {
            game.World._triggeredDoorCmd = 0;
            game.World._triggeredDoorDir = Direction.None;

            if (game.World.IsOverworld())
            {
                var behavior = game.World.GetTileBehaviorXY(game.Player.X, game.Player.Y + 3);
                if (behavior == TileBehavior.Cave)
                {
                    game.Player.Y += BlockHeight;
                    game.Player.Facing = Direction.Down;

                    state.PlayerFraction = 0;
                    state.PlayerSpeed = 0x40;
                    state.PlayerPriority = SpritePriority.BelowBg;
                    state.ScrollDir = Direction.Up;
                    state.TargetX = game.Player.X;
                    state.TargetY = game.Player.Y - (Game.Cheats.SpeedUp ? 0 : 0x10);
                    state.Substate = EnterState.Substates.WalkCave;

                    game.Sound.StopAll();
                    game.Sound.PlayEffect(SoundEffect.Stairs);
                }
                else
                {
                    state.Substate = EnterState.Substates.Wait;
                    state.Timer = EnterState.StateTime;
                }
            }
            else if (state.ScrollDir != Direction.None)
            {
                var oppositeDir = state.ScrollDir.GetOppositeDirection();
                var doorType = game.World.CurrentRoom.UnderworldDoors[oppositeDir];
                var distance = doorType is DoorType.Shutter or DoorType.Bombable ? BlockWidth * 2 : BlockWidth;

                state.TargetX = game.Player.X;
                state.TargetY = game.Player.Y;
                Actor.MoveSimple(
                    ref state.TargetX,
                    ref state.TargetY,
                    state.ScrollDir,
                    distance);

                if (!game.World.CurrentRoom.Settings.IsDark
                    && game.World._darkRoomFadeStep > 0)
                {
                    state.Substate = EnterState.Substates.FadeIn;
                    state.Timer = 9;
                }
                else
                {
                    state.Substate = EnterState.Substates.Walk;
                }

                game.Player.Facing = state.ScrollDir;
            }
            else
            {
                state.Substate = EnterState.Substates.Wait;
                state.Timer = EnterState.StateTime;
            }

            game.World.DoorwayDir = game.World.CurrentRoom.HasUnderworldDoors ? state.ScrollDir : Direction.None;
        }

        static void EnterWait(Game game, ref EnterState state)
        {
            state.Timer--;
            if (state.Timer == 0)
            {
                state.GotoPlay = true;
            }
        }

        static void EnterFadeIn(Game game, ref EnterState state)
        {
            if (game.World._darkRoomFadeStep == 0)
            {
                state.Substate = EnterState.Substates.Walk;
                return;
            }

            ArgumentOutOfRangeException.ThrowIfNegative(state.Timer);

            if (state.Timer > 0)
            {
                state.Timer--;
                return;
            }

            game.World._darkRoomFadeStep--;
            state.Timer = 9;

            var darkPalette = game.World.CurrentWorld.Settings.DarkPalette;
            for (var i = 0; i < 2; i++)
            {
                Graphics.SetPaletteIndexed((Palette)i + 2, darkPalette[game.World._darkRoomFadeStep][i]);
            }
            Graphics.UpdatePalettes();
        }

        static void EnterWalk(Game game, ref EnterState state)
        {
            if (state.HasReachedTarget(game.Player))
            {
                state.GotoPlay = true;
                return;
            }

            game.Player.MoveLinear(state.ScrollDir, state.PlayerSpeed);
        }

        static void EnterWalkCave(Game game, ref EnterState state)
        {
            if (state.HasReachedTarget(game.Player))
            {
                state.GotoPlay = true;
                return;
            }

            game.World.MovePlayer(state.ScrollDir, state.PlayerSpeed, ref state.PlayerFraction);
        }
    }

    private void DrawEnter()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);

        // JOE: The C++ code base had this check but it causes a black frame to be drawn.
        // if (_state.Enter.Substate != EnterState.Substates.Start)
        DrawRoomNoObjects(_state.Enter.PlayerPriority);
    }

    private void SetPlayerExitPosOW(GameRoom room)
    {
        // if (room.ExitPosition != null) // JOE: TODO: MAP REWRITE. This check might not be right.
        // {
        //     Game.Player.X = room.ExitPosition.X;
        //     Game.Player.Y = room.ExitPosition.Y;
        // }
    }

    public void GotoLoadLevel(int level, bool restartOW = false)
    {
        _state.LoadLevel.Level = level;
        _state.LoadLevel.Substate = LoadLevelState.Substates.Load;
        _state.LoadLevel.Timer = 0;
        _state.LoadLevel.RestartOW = restartOW;

        _curMode = GameMode.LoadLevel;
    }

    private void UpdateLoadLevel()
    {
        switch (_state.LoadLevel.Substate)
        {
            case LoadLevelState.Substates.Load:
                _state.LoadLevel.Timer = LoadLevelState.StateTime;
                _state.LoadLevel.Substate = LoadLevelState.Substates.Wait;

                int origLevel = CurrentWorld.Settings.LevelNumber;
                var origRoom = CurrentRoom;

                Game.Sound.StopAll();
                _statusBarVisible = false;
                LoadLevel(_state.LoadLevel.Level);

                // Let the Unfurl game mode load the room and reset colors.

                if (_state.LoadLevel.Level == 0)
                {
                    if (_savedOWRoom != null)
                    {
                        CurrentRoom = _savedOWRoom;
                        _savedOWRoom = null;
                    }
                    FromUnderground = 2;
                }
                else
                {
                    CurrentRoom = CurrentWorld.EntryRoom;
                    if (origLevel == 0)
                    {
                        _savedOWRoom = origRoom;
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

            if (CurrentWorld.Settings.LevelNumber == 0 && !_state.Unfurl.RestartOW)
            {
                LoadRoom(CurrentRoom);
                SetPlayerExitPosOW(CurrentRoom);
            }
            else
            {
                LoadRoom(CurrentWorld.EntryRoom);
                Game.Player.X = CurrentWorld.EntryRoom.EntryPosition?.X ?? 120;
                Game.Player.Y = CurrentWorld.EntryRoom.EntryPosition?.Y ?? 141;
            }

            for (var i = 0; i < CurrentWorld.Settings.Palettes.Length; i++)
            {
                Graphics.SetPaletteIndexed((Palette)i, CurrentWorld.Settings.Palettes[i]);
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
                Game.Sound.PlaySong(CurrentWorld.Settings.SongId, SongStream.MainSong, true);
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

    public void GotoEndLevel()
    {
        _state.EndLevel.Substate = EndLevelState.Substates.Start;
        _curMode = GameMode.EndLevel;
    }

    private void UpdateEndLevel()
    {
        switch (_state.EndLevel.Substate)
        {
            case EndLevelState.Substates.Start: EndLevelStart(); break;
            case EndLevelState.Substates.Wait1: EndLevelWait(); break;
            case EndLevelState.Substates.Flash: EndLevelFlash(); break;
            case EndLevelState.Substates.FillHearts: EndLevelFillHearts(); break;
            case EndLevelState.Substates.Wait2: EndLevelWait(); break;
            case EndLevelState.Substates.Furl: EndLevelFurl(); break;
            case EndLevelState.Substates.Wait3: EndLevelWait(); break;
            default: throw new Exception($"Unknown EndLevelState \"{_state.EndLevel.Substate}\"");
        }
        return;

        void EndLevelStart()
        {
            _state.EndLevel.Substate = EndLevelState.Substates.Wait1;
            _state.EndLevel.Timer = EndLevelState.Wait1Time;

            _state.EndLevel.Left = 0;
            _state.EndLevel.Right = TileMapWidth;
            _state.EndLevel.StepTimer = 4;

            _statusBar.EnableFeatures(StatusBarFeatures.Equipment, false);
            Game.Sound.PlaySong(SongId.Triforce, SongStream.MainSong, false);
        }

        void EndLevelWait()
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

        void EndLevelFlash()
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

        void EndLevelFillHearts()
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

        void EndLevelFurl()
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

        DrawPlayerLiftingItem(ItemId.TriforcePiece);
    }

    public void GotoStairs(TileBehavior behavior, Entrance cave, ObjectState state)
    {
        if (cave == null) throw new Exception("Unable to locate stairs action object.");
        if (cave.Destination == null) throw new Exception("Stairs do not target a proper location.");

        _state.Stairs.Substate = StairsState.Substates.Start;
        _state.Stairs.TileBehavior = behavior;
        _state.Stairs.Entrance = cave;
        _state.Stairs.ObjectState = state;
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
                    Game.Player.Facing = Direction.Up;

                    _state.Stairs.TargetX = Game.Player.X;
                    _state.Stairs.TargetY = Game.Player.Y + (Game.Cheats.SpeedUp ? 0 : 0x10);
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
            case StairsState.Substates.WalkCave when _state.Stairs.HasReachedTarget(Game.Player):
                _log.Write($"CaveType: {_state.Stairs.Entrance}");
                LoadEntrance(_state.Stairs.Entrance, _state.Stairs.ObjectState);
                break;

            case StairsState.Substates.Walk:
                GotoPlayCellar(_state.Stairs.Entrance, _state.Stairs.ObjectState);
                break;

            case StairsState.Substates.WalkCave:
                MovePlayer(_state.Stairs.ScrollDir, _state.Stairs.PlayerSpeed, ref _state.Stairs.PlayerFraction);
                Game.Player.Animator.Advance();
                break;
        }
    }

    private void LoadEntrance(Entrance entrance, ObjectState? state)
    {
        switch (entrance.DestinationType)
        {
            case GameWorldType.Underworld:
                GotoLoadLevel(entrance.GetLevelNumber());
                break;

            case GameWorldType.UnderworldCommon:
                GotoPlayCellar(entrance, state);
                break;

            case GameWorldType.OverworldCommon:
                GotoPlayCave(entrance, state);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(entrance.DestinationType), entrance.DestinationType, "Unsupported entrance type.");
        }
    }

    // JOE: Arg. Use this everywhere presumably?
    private void LoadEntranceRoom(Entrance entrance, out int? destinationY)
    {
        var room = _commonWorlds[entrance.DestinationType].GetRoomByName(entrance.Destination);
        LoadMap(room, _state.PlayCave.Entrance);
        destinationY = null;
        var pos = entrance.EntryPosition;
        if (pos != null)
        {
            Player.X = pos.X;
            Player.Y = pos.Y;
            destinationY = pos.TargetY;
            if (pos.Facing != Direction.None) Player.Facing = pos.Facing;
        }
    }

    private void DrawStairsState()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        DrawRoomNoObjects(_state.Stairs.PlayerPriority);
    }

    private void GotoPlayCellar(Entrance entrance, ObjectState? state)
    {
        _state.PlayCellar.Entrance = entrance;
        _state.PlayCellar.ObjectState = state;
        _state.PlayCellar.Substate = PlayCellarState.Substates.Start;
        _state.PlayCellar.PlayerPriority = SpritePriority.None;

        _curMode = GameMode.InitPlayCellar;
    }

    private void UpdatePlayCellar()
    {
        switch (_state.PlayCellar.Substate)
        {
            case PlayCellarState.Substates.Start: PlayCellarStart(Game, ref _state.PlayCellar); break;
            case PlayCellarState.Substates.FadeOut: PlayCellarFadeOut(Game, ref _state.PlayCellar); break;
            case PlayCellarState.Substates.LoadRoom: PlayCellarLoadRoom(Game, ref _state.PlayCellar); break;
            case PlayCellarState.Substates.FadeIn: PlayCellarFadeIn(Game, ref _state.PlayCellar); break;
            case PlayCellarState.Substates.Walk: PlayCellarWalk(Game, ref _state.PlayCellar); break;
            default: throw new Exception($"Unknown PlayCellarState \"{_state.PlayCellar.Substate}\"");
        }
        return;

        static void PlayCellarStart(Game game, ref PlayCellarState state)
        {
            state.Substate = PlayCellarState.Substates.FadeOut;
            state.FadeTimer = 11;
            state.FadeStep = 0;
        }

        static void PlayCellarFadeOut(Game game, ref PlayCellarState state)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(state.FadeTimer);

            if (state.FadeTimer > 0)
            {
                state.FadeTimer--;
                return;
            }

            for (var i = 0; i < LevelInfoBlock.FadePals; i++)
            {
                var step = state.FadeStep;
                Graphics.SetPaletteIndexed((Palette)i + 2, game.World.CurrentWorld.Settings.OutOfCellarPalette[step][i]);
            }
            Graphics.UpdatePalettes();
            state.FadeTimer = 9;
            state.FadeStep++;

            if (state.FadeStep == LevelInfoBlock.FadeLength)
            {
                state.Substate = PlayCellarState.Substates.LoadRoom;
            }
        }

        static void PlayCellarLoadRoom(Game game, ref PlayCellarState state)
        {
            var entrance = state.Entrance ?? throw new Exception();
            game.World.LoadEntranceRoom(entrance, out var targetY);

            state.TargetY = targetY ?? 0x60;
            state.Substate = PlayCellarState.Substates.FadeIn;
            state.FadeTimer = 35;
            state.FadeStep = 3;
        }

        static void PlayCellarFadeIn(Game game, ref PlayCellarState state)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(state.FadeTimer);

            if (state.FadeTimer > 0)
            {
                state.FadeTimer--;
                return;
            }

            for (var i = 0; i < LevelInfoBlock.FadePals; i++)
            {
                var step = state.FadeStep;
                Graphics.SetPaletteIndexed((Palette)i + 2, game.World.CurrentWorld.Settings.InCellarPalette[step][i]);
            }
            Graphics.UpdatePalettes();
            state.FadeTimer = 9;
            state.FadeStep--;

            if (state.FadeStep < 0)
            {
                state.Substate = PlayCellarState.Substates.Walk;
            }
        }

        static void PlayCellarWalk(Game game, ref PlayCellarState state)
        {
            state.PlayerPriority = SpritePriority.AboveBg;

            _traceLog.Write($"PlayerCellarWalk: Game.Player.Y >= state.TargetY {game.Player.Y} >= {state.TargetY}");
            if (game.Player.Y >= state.TargetY)
            {
                game.World.FromUnderground = 1;
                game.World.GotoPlay(state.ObjectState, state.Entrance);
            }
            else
            {
                game.Player.MoveLinear(Direction.Down, Player.WalkSpeed);
                game.Player.Animator.Advance();
            }
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
        switch (_state.LeaveCellar.Substate)
        {
            case LeaveCellarState.Substates.Start: LeaveCellarStart(Game, ref _state.LeaveCellar); break;
            case LeaveCellarState.Substates.FadeOut: LeaveCellarFadeOut(Game, ref _state.LeaveCellar); break;
            case LeaveCellarState.Substates.LoadRoom: LeaveCellarLoadRoom(Game, ref _state.LeaveCellar); break;
            case LeaveCellarState.Substates.FadeIn: LeaveCellarFadeIn(Game, ref _state.LeaveCellar); break;
            case LeaveCellarState.Substates.Walk: LeaveCellarWalk(Game, ref _state.LeaveCellar); break;
            case LeaveCellarState.Substates.Wait: LeaveCellarWait(Game, ref _state.LeaveCellar); break;
            case LeaveCellarState.Substates.LoadOverworldRoom: LeaveCellarLoadOverworldRoom(Game, ref _state.LeaveCellar); break;
            default: throw new Exception($"Unknown LeaveCellarState \"{_state.LeaveCellar.Substate}\"");
        }

        return;

        static void LeaveCellarStart(Game game, ref LeaveCellarState state)
        {
            if (game.World.GetPreviousEntrance()?.Room.World.IsOverworld ?? true)
            {
                state.Substate = LeaveCellarState.Substates.Wait;
                state.Timer = 29;
            }
            else
            {
                state.Substate = LeaveCellarState.Substates.FadeOut;
                state.FadeTimer = 11;
                state.FadeStep = 0;
            }
        }

        static void LeaveCellarFadeOut(Game game, ref LeaveCellarState state)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(state.FadeTimer);

            if (state.FadeTimer > 0)
            {
                state.FadeTimer--;
                return;
            }

            for (var i = 0; i < LevelInfoBlock.FadePals; i++)
            {
                var step = state.FadeStep;
                Graphics.SetPaletteIndexed((Palette)i + 2, game.World.CurrentWorld.Settings.InCellarPalette[step][i]);
            }
            Graphics.UpdatePalettes();
            state.FadeTimer = 9;
            state.FadeStep++;

            if (state.FadeStep == LevelInfoBlock.FadeLength)
            {
                state.Substate = LeaveCellarState.Substates.LoadRoom;
            }
        }

        static void LeaveCellarLoadRoom(Game game, ref LeaveCellarState state)
        {
            var room = game.World.GetNextRoom(game.Player.Facing, out var entry);

            var nextRoomId = game.Player.X < 0x80
                ? room.CellarStairsLeftRoomId
                : room.CellarStairsRightRoomId;

            if (nextRoomId == null)
            {
                throw new Exception($"Missing CellarStairs[Left/Right]RoomId attributes in room \"{room}\"");
            }

            game.World.LoadRoom(room);

            game.Player.X = entry.FromEntrance.ExitPosition?.X ?? 0x60;
            game.Player.Y = entry.FromEntrance.ExitPosition?.Y ?? 0xA0;
            game.Player.Facing = Direction.Down;

            state.Substate = LeaveCellarState.Substates.FadeIn;
            state.FadeTimer = 35;
            state.FadeStep = 3;
        }

        static void LeaveCellarFadeIn(Game game, ref LeaveCellarState state)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(state.FadeTimer);

            if (state.FadeTimer > 0)
            {
                state.FadeTimer--;
                return;
            }

            for (var i = 0; i < LevelInfoBlock.FadePals; i++)
            {
                var step = state.FadeStep;
                Graphics.SetPaletteIndexed((Palette)i + 2, game.World.CurrentWorld.Settings.OutOfCellarPalette[step][i]);
            }
            Graphics.UpdatePalettes();
            state.FadeTimer = 9;
            state.FadeStep--;

            if (state.FadeStep < 0)
            {
                state.Substate = LeaveCellarState.Substates.Walk;
            }
        }

        static void LeaveCellarWalk(Game game, ref LeaveCellarState state)
        {
            game.World.GotoEnter(Direction.None);
        }

        static void LeaveCellarWait(Game game, ref LeaveCellarState state)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(state.FadeTimer);

            if (state.Timer > 0)
            {
                state.Timer--;
                return;
            }

            state.Substate = LeaveCellarState.Substates.LoadOverworldRoom;
        }

        static void LeaveCellarLoadOverworldRoom(Game game, ref LeaveCellarState state)
        {
            for (var i = 0; i < 2; i++)
            {
                Graphics.SetPaletteIndexed((Palette)i + 2, game.World.CurrentWorld.Settings.Palettes[i + 2]);
            }
            Graphics.UpdatePalettes();

            // JOE: TODO: Write a generic "goto previous entrance" or w/e method.
            var historyEntry = game.World.TakePreviousEntranceOrDefault();

            game.World.LoadRoom(historyEntry.Room);
            var exitPosition = historyEntry.FromEntrance.ExitPosition;
            if (exitPosition != null)
            {
                game.Player.X = exitPosition.X;
                game.Player.Y = exitPosition.Y;
            }
            game.World.GotoEnter(Direction.None);
            game.Player.Facing = Direction.Down;
        }
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

    private void GotoPlayCave(Entrance entrance, ObjectState? state)
    {
        _state.PlayCave.Substate = PlayCaveState.Substates.Start;
        _state.PlayCave.Entrance = entrance;
        _state.PlayCave.ObjectState = state;

        _curMode = GameMode.InitPlayCave;
    }

    private void UpdatePlayCave()
    {
        switch (_state.PlayCave.Substate)
        {
            case PlayCaveState.Substates.Start: PlayCaveStart(Game, ref _state.PlayCave); break;
            case PlayCaveState.Substates.Wait: PlayCaveWait(Game, ref _state.PlayCave); break;
            case PlayCaveState.Substates.LoadRoom: PlayCaveLoadRoom(Game, ref _state.PlayCave); break;
            case PlayCaveState.Substates.Walk: PlayCaveWalk(Game, ref _state.PlayCave); break;
            default: throw new Exception($"Unknown PlayCaveState \"{_state.PlayCave.Substate}\"");
        }
        return;

        static void PlayCaveStart(Game game, ref PlayCaveState state)
        {
            state.Substate = PlayCaveState.Substates.Wait;
            state.Timer = 27;
        }

        static void PlayCaveWait(Game game, ref PlayCaveState state)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(state.Timer);

            if (state.Timer > 0)
            {
                state.Timer--;
                return;
            }

            state.Substate = PlayCaveState.Substates.LoadRoom;
        }

        static void PlayCaveLoadRoom(Game game, ref PlayCaveState state)
        {
            // JOE: TODO: MAP REWRITE var paletteSet = _extraData.CavePalette;
            // var caveLayout = FindSparseFlag(Sparse.Shortcut, CurrentRoom) ? CaveType.Shortcut : CaveType.Items;

            // LoadCaveRoom(caveLayout);
            var entrance = state.Entrance ?? throw new Exception();
            game.World.LoadEntranceRoom(entrance, out var targetY);

            state.Substate = PlayCaveState.Substates.Walk;
            state.TargetY = targetY ?? 0xD5;

            game.Player.Facing = Direction.Up;

            for (var i = 0; i < 2; i++)
            {
                // JOE: TODO: MAP REWRITE Graphics.SetPaletteIndexed((Palette)i + 2, paletteSet.GetByIndex(i));
            }
            Graphics.UpdatePalettes();
        }

        static void PlayCaveWalk(Game game, ref PlayCaveState state)
        {
            _traceLog.Write($"PlayCaveWalk: Game.Player.Y <= state.TargetY {game.Player.Y} <= {state.TargetY}");
            if (game.Player.Y <= state.TargetY)
            {
                game.World.FromUnderground = 1;
                game.World.GotoPlay(state.ObjectState, state.Entrance);
                return;
            }

            game.Player.MoveLinear(Direction.Up, Player.WalkSpeed);
            game.Player.Animator.Advance();
        }
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

    private static readonly ImmutableArray<ImmutableArray<byte>> _deathRedPals = [
        [0x0F, 0x17, 0x16, 0x26],
        [0x0F, 0x17, 0x16, 0x26]
    ];

    private void UpdateDie()
    {
        // ORIGINAL: Some of these are handled with object timers.
        if (_state.Death.Timer > 0)
        {
            _state.Death.Timer--;
            // JOE: C++ does not return here.
        }

        switch (_state.Death.Substate)
        {
            case DeathState.Substates.Start: DieStart(Game, ref _state.Death); break;
            case DeathState.Substates.Flash: DieFlash(Game, ref _state.Death); break;
            case DeathState.Substates.Wait1: DieWait1(Game, ref _state.Death); break;
            case DeathState.Substates.Turn: DieTurn(Game, ref _state.Death); break;
            case DeathState.Substates.Fade: DieFade(Game, ref _state.Death); break;
            case DeathState.Substates.GrayPlayer: DieGrayPlayer(Game, ref _state.Death); break;
            case DeathState.Substates.Spark: DieSpark(Game, ref _state.Death); break;
            case DeathState.Substates.Wait2: DieWait2(Game, ref _state.Death); break;
            case DeathState.Substates.GameOver: DieGameOver(Game, ref _state.Death); break;
            default: throw new Exception($"Unknown DeathState \"{_state.Death.Substate}\"");
        }
        return;

        static void DieStart(Game game, ref DeathState state)
        {
            game.Player.InvincibilityTimer = 0x10;
            state.Timer = 0x20;
            state.Substate = DeathState.Substates.Flash;
            game.Sound.StopEffects();
            game.Sound.PlaySong(SongId.Death, SongStream.MainSong, false);
        }

        static void DieFlash(Game game, ref DeathState state)
        {
            game.Player.DecInvincibleTimer();

            if (state.Timer == 0)
            {
                state.Timer = 6;
                state.Substate = DeathState.Substates.Wait1;
            }
        }

        static void DieWait1(Game game, ref DeathState state)
        {
            // TODO: the last 2 frames make the whole play area use palette 3.

            if (state.Timer == 0)
            {
                SetLevelPalettes(_deathRedPals);

                state.Step = 16;
                state.Timer = 0;
                state.Substate = DeathState.Substates.Turn;
            }
        }

        static void DieTurn(Game game, ref DeathState state)
        {
            if (state.Step == 0)
            {
                state.Step = 4;
                state.Timer = 0;
                state.Substate = DeathState.Substates.Fade;
            }
            else
            {
                if (state.Timer == 0)
                {
                    state.Timer = 5;
                    state.Step--;

                    ReadOnlySpan<Direction> dirs = [Direction.Down, Direction.Left, Direction.Up, Direction.Right];

                    var dir = dirs[state.Step & 3];
                    game.Player.Facing = dir;
                }
            }
        }

        static void DieFade(Game game, ref DeathState state)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(state.Step);

            if (state.Step > 0)
            {
                if (state.Timer == 0)
                {
                    state.Timer = 10;
                    state.Step--;

                    var seq = 3 - state.Step;

                    SetLevelPalettes(game.World.CurrentWorld.Settings.DeathPalette[seq]);
                }
                return;
            }

            state.Substate = DeathState.Substates.GrayPlayer;
        }

        static void DieGrayPlayer(Game game, ref DeathState state)
        {
            ReadOnlySpan<byte> grayPal = [0, 0x10, 0x30, 0];

            Graphics.SetPaletteIndexed(Palette.Player, grayPal);
            Graphics.UpdatePalettes();

            state.Substate = DeathState.Substates.Spark;
            state.Timer = 0x18;
            state.Step = 0;
        }

        static void DieSpark(Game game, ref DeathState state)
        {
            if (state.Timer != 0) return;

            switch (state.Step)
            {
                case 0:
                    state.Timer = 10;
                    game.Sound.PlayEffect(SoundEffect.Character);
                    break;

                case 1:
                    state.Timer = 4;
                    break;

                default:
                    state.Substate = DeathState.Substates.Wait2;
                    state.Timer = 46;
                    break;
            }

            state.Step++;
        }

        static void DieWait2(Game game, ref DeathState state)
        {
            if (state.Timer == 0)
            {
                state.Substate = DeathState.Substates.GameOver;
                state.Timer = 0x60;
            }
        }

        static void DieGameOver(Game game, ref DeathState state)
        {
            if (state.Timer == 0)
            {
                game.World.Profile.Deaths++;
                game.World.GotoContinueQuestion();
            }
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
            var player = Game.Player;

            if (_state.Death.Substate == DeathState.Substates.Spark && _state.Death.Step > 0)
            {
                GlobalFunctions.DrawSparkle(player.X, player.Y, Palette.Blue, _state.Death.Step - 1);
            }
            else if (_state.Death.Substate <= DeathState.Substates.Spark)
            {
                Game.Player.Draw();
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
                        Game.Player.Initialize();
                        Profile.Hearts = PlayerProfile.GetMaxHeartsValue(PlayerProfile.DefaultHeartCount);
                        Unpause(); // It's easy for select+start to also pause the game, and that's confusing.
                        GotoUnfurl(true);
                        break;

                    case ContinueState.Indexes.Save:
                        SaveFolder.SaveProfiles();
                        Game.Menu.GotoFileMenu();
                        break;

                    case ContinueState.Indexes.Retry:
                        Game.Menu.GotoFileMenu();
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
        GlobalFunctions.DrawChar(Chars.FullHeart, 0x40, y, Palette.Red);
    }

    private GameRoom? FindCellarRoomId(GameRoom mainRoom, out bool isLeft)
    {
        isLeft = false;
        // JOE: TODO: REWRITE for (var i = 0; i < LevelInfoBlock.LevelCellarCount; i++)
        // JOE: TODO: REWRITE {
        // JOE: TODO: REWRITE     var cellarRoomId = _infoBlock.CellarRoomIds[i];
        // JOE: TODO: REWRITE     if (cellarRoomId >= 0x80) break;
        // JOE: TODO: REWRITE
        // JOE: TODO: REWRITE     if (mainRoom.Id == mainRoom.CellarStairsLeftRoomId)
        // JOE: TODO: REWRITE     {
        // JOE: TODO: REWRITE         isLeft = true;
        // JOE: TODO: REWRITE         return cellarRoomId;
        // JOE: TODO: REWRITE     }
        // JOE: TODO: REWRITE
        // JOE: TODO: REWRITE     if (mainRoomId == uwRoomAttrs.GetRightCellarExitRoomId())
        // JOE: TODO: REWRITE     {
        // JOE: TODO: REWRITE         isLeft = false;
        // JOE: TODO: REWRITE         return cellarRoomId;
        // JOE: TODO: REWRITE     }
        // JOE: TODO: REWRITE }

        return null;
    }

    private void DrawRoomNoObjects(SpritePriority playerPriority = SpritePriority.AboveBg)
    {
        ClearScreen();

        if (playerPriority == SpritePriority.BelowBg)
        {
            Game.Player.Draw();
        }

        DrawRoom();

        if (playerPriority == SpritePriority.AboveBg)
        {
            Game.Player.Draw();
        }

        // if (IsUWMain(CurrentRoom))
        {
            DrawDoors(CurrentRoom, true, 0, 0);
        }
    }

    private static void NoneTileAction(int tileY, int tileX, TileInteraction interaction)
    {
        // Nothing to do. Should never be called.
        // Debugger.Break(); // JOE: TODO: This was called. I burned myself with the red candle.
    }

    private void PushTileAction(int tileY, int tileX, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load) return;

        var rock = new RockObj(Game, tileX * TileWidth, TileMapBaseY + tileY * TileHeight);
        SetBlockObj(rock);
    }

    private void BombTileAction(int tileY, int tileX, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load) return;

        if (CurrentRoom.PersistedRoomState.SecretState)
        {
            SetMapObject(tileY, tileX, BlockType.Cave);
            return;
        }

        var rockWall = new RockWallActor(Game, tileX * TileWidth, TileMapBaseY + tileY * TileHeight);
        SetBlockObj(rockWall);
    }

    private void BurnTileAction(int tileY, int tileX, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load) return;

        if (CurrentRoom.PersistedRoomState.SecretState)
        {
            SetMapObject(tileY, tileX, BlockType.Stairs);
            return;
        }

        var tree = new TreeActor(Game, tileX * TileWidth, TileMapBaseY + tileY * TileHeight);
        SetBlockObj(tree);
    }

    private void HeadstoneTileAction(int tileY, int tileX, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load) return;

        var headstone = new HeadstoneObj(Game, tileX * TileWidth, TileMapBaseY + tileY * TileHeight);
        SetBlockObj(headstone);
    }

    private void LadderTileAction(int tileY, int tileX, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Touch) return;

        Debug.WriteLine("Touch water: {0}, {1}", tileY, tileX);
    }

    private void RaftTileAction(int tileY, int tileX, TileInteraction interaction)
    {
        // TODO: instantiate the Dock here on Load interaction, and set its position.

        if (interaction != TileInteraction.Cover) return;

        Debug.WriteLine("Cover dock: {0}, {1}", tileY, tileX);

        // JOE: TODO: This appears to do nothing?
        // if (GetItem(ItemSlot.Raft) == 0) return;
        // if (!FindSparseFlag(Sparse.Dock, CurrentRoom)) return;
    }

    private void CaveTileAction(int tileY, int tileX, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Cover) return;

        if (IsOverworld())
        {
            var behavior = GetTileBehavior(tileY, tileX);
            // JOE: TODO: OBJECT REWRITE var stairsTo = CurrentRoom.GetActionObject(TileAction.Cave, tileX, tileY);
            // JOE: TODO: OBJECT REWRITE GotoStairs(behavior, stairsTo);
        }

        Debug.WriteLine("Cover cave: {0}, {1}", tileY, tileX);
    }

    private void StairsTileAction(int tileY, int tileX, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Cover) return;

        if (GetMode() == GameMode.Play)
        {
            // JOE: TODO: OBJECT REWRITEvar stairsTo = CurrentRoom.GetActionObject(TileAction.Stairs, tileX, tileY);
            // JOE: TODO: OBJECT REWRITEGotoStairs(TileBehavior.Stairs, stairsTo);
        }

        Debug.WriteLine("Cover stairs: {0}, {1}", tileY, tileX);
    }

    public void GhostTileAction(int tileY, int tileX, TileInteraction interaction)
    {
        if (interaction == TileInteraction.Push) Debug.WriteLine("Push headstone: {0}, {1}", tileY, tileX);

        // CommonMakeObjectAction(ObjType.FlyingGhini, tileY, tileX, interaction, ref _ghostCount, _ghostCells);
    }

    public void ArmosTileAction(int tileY, int tileX, TileInteraction interaction)
    {
        if (interaction == TileInteraction.Push) Debug.WriteLine("Push armos: {0}, {1}", tileY, tileX);

        // CommonMakeObjectAction(ObjType.Armos, tileY, tileX, interaction, ref _armosCount, _armosCells);
    }

    public void CommonMakeObjectAction(
        ObjType type, int tileY, int tileX, TileInteraction interaction, ref int patchCount, Cell[] patchCells)
    {
        switch (interaction)
        {
            case TileInteraction.Load:
                if (patchCount < 16)
                {
                    patchCells[patchCount] = new Cell((byte)tileY, (byte)tileX);
                    patchCount++;
                }
                break;

            case TileInteraction.Push:
                var map = CurrentRoom.RoomMap;
                var behavior = map.Behavior(tileX, tileY);

                if (tileY > 0 && map.Behavior(tileX, tileY - 1) == behavior)
                {
                    tileY--;
                }
                if (tileX > 0 && map.Behavior(tileX - 1, tileY) == behavior)
                {
                    tileX--;
                }

                // JOE: TODO: Screen conversion. MakeActivatedObject seems to believe these are normal x/y?
                MakeActivatedObject(type, tileX, tileY);
                break;
        }
    }

    public Actor? CommonMakeObjectAction(ObjType type, int tileX, int tileY)
    {
        var map = CurrentRoom.RoomMap;
        var behavior = map.Behavior(tileX, tileY);

        if (tileY > 0 && map.Behavior(tileX, tileY - 1) == behavior)
        {
            tileY--;
        }
        if (tileX > 0 && map.Behavior(tileX - 1, tileY) == behavior)
        {
            tileX--;
        }

        return MakeActivatedObject(type, tileX, tileY);
    }

    public Actor? MakeActivatedObject(ObjType type, int tileX, int tileY)
    {
        if (type is not (ObjType.FlyingGhini or ObjType.Armos))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, $"Invalid type given to {nameof(MakeActivatedObject)}");
        }

        tileY += BaseRows;

        var x = tileX * TileWidth;
        var y = tileY * TileHeight;

        foreach (var obj in GetObjects<MonsterActor>())
        {
            if (obj.ObjType != type) continue;

            var objX = obj.X / TileWidth;
            var objY = obj.Y / TileHeight;

            if (objX == x && objY == y) return null;
        }

        var activatedObj = Actor.AddFromType(type, Game, x, y);
        activatedObj.ObjTimer = 0x40;

        return activatedObj;
    }

    public void BlockTileAction(int tileY, int tileX, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load) return;

        var block = new BlockObj(Game, tileX * TileWidth, TileMapBaseY + tileY * TileHeight);
        SetBlockObj(block);
    }

    public void RecorderTileAction(int tileY, int tileX, TileInteraction interaction)
    {
        // JOE: TODO
        if (interaction != TileInteraction.Load) return;

        var block = new BlockObj(Game, tileX * TileWidth, TileMapBaseY + tileY * TileHeight);
        SetBlockObj(block);
    }

    public void DoorTileAction(int tileY, int tileX, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Push) return;

        // Based on $91D6 and old implementation Player::CheckDoor.

        Debug.WriteLine("Push door: {0}, {1}", tileY, tileX);
        var player = Player;

        var doorType = CurrentRoom.UnderworldDoors[player.MovingDirection];

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
                        LeaveRoom(player.Facing, CurrentRoom);
                        player.Stop();
                        break;
                }
                break;

            case DoorType.Bombable:
                if (GetEffectiveDoorState(player.MovingDirection))
                {
                    LeaveRoom(player.Facing, CurrentRoom);
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

    public Actor AddItem(ItemId itemId, int x, int y, ItemObjActorOptions options = ItemObjActorOptions.None)
    {
        Actor actor = itemId == ItemId.Fairy
            ? new FairyActor(Game, x, y)
            : new ItemObjActor(Game, itemId, options, x, y);

        _objects.Add(actor);
        return actor;
    }
}