﻿using System.Collections.Immutable;
using System.Diagnostics;
using z1.IO;
using z1.Render;
using z1.UI;

namespace z1;

internal enum TileInteraction { Load, Push, Touch, Cover }
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
}

internal enum StunTimerSlot
{
    NoSword,
    RedLeever,
    ObservedPlayer,
    EdgeObject
}

internal record Cell(byte Y, byte X);

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
    public bool DrawHitDetection { get; set; }

    public int ActiveMonsterShots => _objects.Count(t => !t.IsDeleted && t is IProjectile { IsPlayerWeapon: false });
    public Player Player => Game.Player;
    public PlayerProfile Profile => Game.Player.Profile;

    public SubmenuType Menu;
    public int RoomObjCount;           // 34E
    public Actor? RoomObj;              // 35F
    public bool EnablePersonFireballs;
    public bool SwordBlocked;           // 52E
    public byte WhirlwindTeleporting;   // 522
    public Direction DoorwayDir;         // 53

    private readonly RoomHistory _roomHistory;
    private GameWorld _overworld;
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

    private GameMode _lastMode;
    private GameMode _curMode;
    private readonly StatusBar _statusBar;
    private CreditsType? _credits;
    private TextBox? _textBox1;
    private TextBox? _textBox2;

    private readonly WorldState _state = new();
    // Runs when the state is changed.
    private Action? _stateCleanup;
    private int _curColorSeqNum;
    private int _darkRoomFadeStep;
    private int _curMazeStep;
    private int _spotIndex;
    private GameRoom? _tempShutterRoom;
    private Direction _tempShutterDoorDir;
    private bool _tempShutters;
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
    private readonly DoorTileIndex _doorTileIndex;

    private int _triggeredDoorCmd;   // 54
    private Direction _triggeredDoorDir;   // 55

    private bool _triggerShutters;    // 4CE
    private bool _summonedWhirlwind;  // 508
    private bool _powerTriforceFanfare;   // 509
    private bool _brightenRoom;       // 51E

    public Rectangle PlayAreaRect { get; set; }

    public World(Game game)
    {
        Game = game;

        _entranceHistory = new EntranceHistory(this);

        _roomHistory = new RoomHistory(game, RoomHistoryLength);
        _statusBar = new StatusBar(this);
        Menu = new SubmenuType(game);

        _lastMode = GameMode.Demo;
        _curMode = GameMode.Play;
        _edgeY = 0x40;

        Validate();

        PlayAreaRect = new Rectangle(0, TileMapBaseY, ScreenTileWidth * TileWidth, TileMapHeight);
        LoadOpenRoomContext();

        // I'm not fond of _dummyWorld, but I want to keep Game.World and World.Profile to not be nullable
        // LoadOverworld();
        // GotoLoadOverworld();

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

    public void Start()
    {
        _overworld = GameWorld.Load(this, "Maps/Overworld.world", 1);
        LoadOverworld();
        GotoLoadOverworld();
        _commonWorlds[GameWorldType.OverworldCommon] = GameWorld.Load(this, "Maps/OverworldCommon.world", 1);
        _commonWorlds[GameWorldType.UnderworldCommon] = GameWorld.Load(this, "Maps/UnderworldCommon.world", 1);
    }

    // This irl should be moved over to tests.
    private void Validate()
    {
        // Ensure there's one defined for each.
        foreach (var action in Enum.GetValues<DoorType>()) DoorStateFaces.GetState(action, true);
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
                    Game.Player.Stop();
                }
            }

            if (_stateCleanup != null)
            {
                _stateCleanup();
                _stateCleanup = null;
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
        // ReturnToPreviousEntrance(Game.Player.Facing);
        if (!TryGetConnectedRoom(currentRoom, dir, out _))
        {
            ReturnToPreviousEntrance(Game.Player.Facing);
            return;
        }

        GotoLeave(dir, currentRoom);

        // switch (currentRoom.WorldType)
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

    public void LeaveCellarByShortcut(GameRoom targetRoom)
    {
        CurrentRoom = targetRoom;
        // JOE: TODO: MAP REWRITE TakeShortcut();
        GotoLeaveCellar();
    }

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
        foreach (var obj in GetObjects<InteractableBlockActor>())
        {
            // If any action spots support the recorder, we should not summon the whirlwind.
            if (obj.NonTargetedAction(Interaction.Recorder)) shouldSummonWhirlwind = false;
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

            var whirlwind = new WhirlwindActor(this, 0, Game.Player.Y);
            AddObject(whirlwind);

            _summonedWhirlwind = true;
            _teleportingRoomIndex = GetNextTeleportingRoomIndex();
            // JOE: TODO: MAP REWRITE whirlwind.SetTeleportPrevRoomId(teleportRoomIds[_teleportingRoomIndex]);
        }
    }

    public TileBehavior GetTileBehavior(int tileX, int tileY)
    {
        return CurrentRoom.RoomMap.AsBehaviors(tileX, tileY);
    }

    public TileBehavior GetTileBehaviorXY(int x, int y)
    {
        var tileX = x / TileWidth;
        var tileY = (y - TileMapBaseY) / TileHeight;

        return GetTileBehavior(tileX, tileY);
    }

    public void SetMapObjectXY(int x, int y, BlockType block)
    {
        var fineTileX = x / TileWidth;
        var fineTileY = (y - TileMapBaseY) / TileHeight;

        if (fineTileX is < 0 or >= ScreenTileWidth || fineTileY is < 0 or >= ScreenTileHeight) return;

        SetMapObject(fineTileY, fineTileX, block);
    }

    private void SetMapObject(int tileY, int tileX, BlockType block)
    {
        var map = CurrentRoom.RoomMap;
        if (!CurrentRoom.TryGetBlockObjectTiles(block, out var tileObject))
        {
            throw new Exception($"Unable to locate BlockObjType {block}");
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
    public int CountObjects<T>() where T : Actor => GetObjects<T>().Count(static t => !t.IsDeleted);
    public int CountObjects() => _objects.Count;

    public bool HasObject<T>() where T : Actor => GetObjects<T>().Any();

    public void AddObject(Actor actor)
    {
        _traceLog.Write($"AddObject({actor.ObjType}); ({actor.X:X2},{actor.Y:X2})");
        _objects.Add(actor);
    }

    public void AddUniqueObject(Actor obj)
    {
        if (!_objects.Contains(obj)) AddObject(obj);
    }

    public void AddOnlyObject(Actor? old, Actor actor)
    {
        _traceLog.Write($"AddOnlyObject({old?.ObjType}, {actor.ObjType}); ({actor.X:X2},{actor.Y:X2})");
        AddObject(actor);
        old?.Delete();
    }

    public void RemoveObject<T>()
        where T : Actor
    {
        for (var i = _objects.Count - 1; i >= 0; i--)
        {
            if (_objects[i] is T actor)
            {
                _traceLog.Write($"RemoveObject({actor.ObjType}); ({actor.X:X2},{actor.Y:X2})");
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

    private void InteractTile(int rowY, int colX, TileInteraction interaction)
    {
        if (rowY < 0 || colX < 0 || rowY >= ScreenTileHeight || colX >= ScreenTileWidth) return;

        var behavior = GetTileBehavior(colX, rowY);
        RunTileBehavior(behavior, rowY, colX, interaction);
    }

    public TileCollision CollidesWithTileStill(int x, int y)
    {
        return CollidesWithTile(x, y, Direction.None, 0);
    }

    public TileCollision CollidesWithTileMoving(int x, int y, Direction dir, bool isPlayer)
    {
        if (Game.Cheats.NoClip && isPlayer)
        {
            return new TileCollision(false, TileBehavior.GenericWalkable, x / TileWidth, y / TileHeight);
        }

        var offset = dir switch
        {
            Direction.Right => 0x10,
            Direction.Down => 8,
            _ => isPlayer ? -8 : -0x10,
        };

        // Originally, this code did not perform the secondary collision check. That was reserved only for the player
        // and only when they were in the underworld. This check is important because walls disallow the player
        // from having their sprite's top half overlap them. What's mysterious to me, is what's now a `IsVertical` check
        // was formally a `IsHorizontal` check. This caused you to not be able to walk left or right once clipped into
        // a wall, sure. But the reason you couldn't overlap the wall was because there was a hard coded check on the
        // player's coordinates, that then for the sake of the math, did a -8 on the y axis.
        var collision1 = CollidesWithTile(x, y, dir, offset);
        if (isPlayer)
        {
            if (dir.IsVertical() && collision1.TileBehavior != TileBehavior.Wall)
            {
                var collision2 = CollidesWithTile(x, y - 8, dir, offset);
                if (collision2.TileBehavior == TileBehavior.Wall)
                {
                    return collision2;
                }
            }

            if (collision1.TileBehavior == TileBehavior.Doorway)
            {
                collision1.Collides = false;
            }
        }

        return collision1;
    }

    public TileCollision CollidesWithTile(int x, int y, Direction dir, int offset)
    {
        y += 0x0B;

        if (dir.IsVertical())
        {
            if (dir == Direction.Up || y < 0xDD)
            {
                y += offset;
            }
        }
        else
        {
            // I believe these constants should be computed from offset?
            if ((dir == Direction.Left && x >= 0x10) || (dir == Direction.Right && x < 0xF0))
            {
                x += offset;
            }
        }

        if (y < TileMapBaseY)
        {
            // JOE: FIXME: Arg. This is a workaround to a bug in the original C++ but the original C++ is a proper
            // translation from the assembly. I was unable to reproduce this issue in the original game, so it's either
            // a logic change or an issue higher up.
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
            var curBehavior = GetTileBehavior(c, fineRow);
            if (curBehavior > behavior)
            {
                behavior = curBehavior;
                hitFineCol = (byte)c;
            }
        }

        return new TileCollision(behavior.CollidesTile(), behavior, hitFineCol, fineRow);
    }

    public bool TouchesWall(int x, int y, int tileOffset)
    {
        // TileOffset has to be checked to be 0. At least as far as I can tell, there's never a "transition" unless
        // you're on a tile boundary. With walls now being checked specifically instead of as bound checks, it was
        // easier for objects with TileOffsets to "touch" things while still in that transitory state.
        //
        // Since the AIs can only turn when they're on a tile boundary, this could cause them to continue to walk into
        // a wall indefinitely.
        if (tileOffset != 0) return false;

        var fineRow = (int)(byte)((y - TileMapBaseY) / 8);
        var fineCol1 = (int)(byte)(x / 8);

        for (var c = fineCol1; c <= fineCol1 + 1; c++)
        {
            for (var r = fineRow; r <= fineRow + 1; r++)
            {
                var curBehavior = CurrentRoom.RoomMap.AsBehaviors(c, r);
                if (curBehavior.CollidesWall()) return true;
            }
        }

        return false;
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
                if (Profile.Items.Get(ItemSlot.Arrow) != 0
                    && Profile.Items.Get(ItemSlot.Bow) != 0)
                {
                    break;
                }
            }
            else
            {
                if (Profile.Items.Get(Profile.SelectedItem) != 0)
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

    private DoorState GetDoorState(GameRoom room, Direction doorDir, PersistedRoomState roomState)
    {
        var doorType = room.UnderworldDoors[doorDir];
        var doorState = roomState.IsDoorOpen(doorDir);
        if (_tempShutterDoorDir != 0
            && room == _tempShutterRoom
            && doorType == DoorType.Shutter)
        {
            if (doorDir == _tempShutterDoorDir)
            {
                doorState = true;
            }
        }
        if (doorType == DoorType.Shutter && _tempShutters && _tempShutterRoom == room)
        {
            doorState = true;
        }
        return DoorStateFaces.GetState(doorType, doorState);
    }

    public bool HasItem(ItemSlot itemSlot) => GetItem(itemSlot) > 0;
    public int GetItem(ItemSlot itemSlot) => Profile.Items.Get(itemSlot);
    public void SetItem(ItemSlot itemSlot, int value) => Profile.Items.Set(itemSlot, value);

    private void PostRupeeChange(int value, ItemSlot itemSlot)
    {
        if (itemSlot is not (ItemSlot.RupeesToAdd or ItemSlot.RupeesToSubtract))
        {
            throw new ArgumentOutOfRangeException(nameof(itemSlot), itemSlot, "Invalid itemSlot for PostRupeeChange.");
        }

        var curValue = Profile.Items.Get(itemSlot);
        var newValue = Math.Clamp(curValue + value, 0, 255);

        switch (itemSlot)
        {
            case ItemSlot.RupeesToAdd: Profile.Statistics.RupeesCollected += value; break;
            case ItemSlot.RupeesToSubtract: Profile.Statistics.RupeesSpent += value; break;
        }

        Profile.Items.Set(itemSlot, newValue);
    }

    public void PostRupeeWin(int value) => PostRupeeChange(value, ItemSlot.RupeesToAdd);
    public void PostRupeeLoss(int value) => PostRupeeChange(value, ItemSlot.RupeesToSubtract);

    public void FillHearts(int heartValue)
    {
        var maxHeartValue = Profile.Items.Get(ItemSlot.HeartContainers) << 8;

        Profile.Hearts += heartValue;
        if (Profile.Hearts >= maxHeartValue)
        {
            Profile.Hearts = maxHeartValue - 1;
        }
    }

    public void AddItem(ItemId itemId, int? amount = null)
    {
        if ((int)itemId >= (int)ItemId.MAX) return;

        Game.Sound.PlayItemSound(itemId);

        if (itemId is ItemId.Compass or ItemId.Map or ItemId.TriforcePiece)
        {
            Profile.SetDungeonItem(CurrentWorld, itemId);
            return;
        }

        var equip = ItemToEquipment[itemId];
        var slot = equip.Slot;
        var value = amount ?? equip.Value;

        var max = -1;
        if (equip.MaxValue.HasValue) max = equip.MaxValue.Value;
        if (equip.Max.HasValue) max = Profile.Items.GetMax(equip.Max.Value);

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
            value += (byte)Profile.Items.Get(slot);

        }

        if (max > 0) value = Math.Min(value, max);

        Profile.Items.Set(slot, value);

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
            Profile.Items.Set(itemSlot, val - 1);
        }
    }

    private bool GetEffectiveDoorState(GameRoom room, Direction doorDir)
    {
        // TODO: the original game does it a little different, by looking at $EE.
        var type = room.UnderworldDoors[doorDir];
        return room.PersistedRoomState.IsDoorOpen(doorDir)
            || (type == DoorType.Shutter && _tempShutters && room == _tempShutterRoom) // JOE: I think doing object instance comparisons is fine?
            || (_tempShutterDoorDir == doorDir && room == _tempShutterRoom);
    }

    public bool IsOverworld() => CurrentWorld != null && CurrentWorld.IsOverworld;

    public Actor DebugSpawnItem(ItemId itemId)
    {
        return AddItemActor(itemId, Game.Player.X, Game.Player.Y - TileHeight);
    }

    public void DebugSpawnCave(Func<ShopSpec[], ShopSpec> getSpec)
    {
        MakePersonRoomObjects(getSpec(Game.Data.CaveSpec), null);
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

    public void TriggerShutters()
    {
        _triggerShutters = true;
    }

    private void OpenShutters()
    {
        _tempShutters = true;
        _tempShutterRoom = CurrentRoom;
        Game.Sound.PlayEffect(SoundEffect.Door);

        var roomState = CurrentRoom.PersistedRoomState;

        foreach (var direction in TiledRoomProperties.DoorDirectionOrder)
        {
            if (CurrentRoom.UnderworldDoors[direction] == DoorType.Shutter)
            {
                UpdateDoorTileBehavior(CurrentRoom, direction);
            }
            UpdateDoorTiles(CurrentRoom, direction, roomState);
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

    public void LoadOverworldRoom(int x, int y)
    {
        var room = CurrentWorld.GameWorldMap.RoomGrid[x, y];
        if (room == null)
        {
            Game.Toast($"Invalid room {x},{y}");
            return;
        }
        LoadRoom(room);
    }

    private void LoadRoom(GameRoom room)
    {
        CurrentRoom = room;
        CurrentWorld = room.GameWorld;

        LoadMap(room);
    }

    private void UpdateDoorTileBehavior(GameRoom room, Direction doorDir)
    {
        var map = CurrentRoom.RoomMap;
        var (corner, behindCorner, _) = DoorCorner.Get(doorDir);
        var type = room.UnderworldDoors[doorDir];
        if (type == DoorType.None) return;

        var effectiveDoorState = GetEffectiveDoorState(room, doorDir);
        var behavior = DoorStateBehaviors.Get(type).GetBehavior(effectiveDoorState);

        map.SetBlockBehavior(corner,  behavior);
        map.SetBlock(corner.X, corner.Y, TiledTile.Empty);

        if (behavior == TileBehavior.Doorway)
        {
            map.SetBlockBehavior(behindCorner, behavior);
        }
    }

    private void UpdateDoorTiles(GameRoom room, Direction doorDir, PersistedRoomState roomState)
    {
        var map = room.RoomMap;
        var state = GetDoorState(room, doorDir, roomState);
        var (corner, _, drawOffset) = DoorCorner.Get(doorDir);
        var tiles = Game.Data.DoorTileIndex.Get(doorDir, state);
        var drawat = corner + drawOffset;
        map.Blit(tiles.Entries, tiles.Width, tiles.Height, drawat.X, drawat.Y);
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
                && !GetEffectiveDoorState(CurrentRoom, direction))
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

        var roomState = CurrentRoom.PersistedRoomState;

        foreach (var dir in TiledRoomProperties.DoorDirectionOrder)
        {
            if ((_triggeredDoorDir & dir) == 0) continue;

            var type = CurrentRoom.UnderworldDoors[dir];
            if (!type.IsLockedType()) continue;
            if (roomState.IsDoorOpen(dir)) continue;

            var oppositeDir = dir.GetOppositeDirection();
            if (!TryGetConnectedRoom(CurrentRoom, dir, out var nextRoom))
            {
                _log.Error("Attempted to move to invalid room.");
                return;
            }

            roomState.SetDoorState(dir, PersistedDoorState.Open);
            nextRoom.PersistedRoomState.SetDoorState(oppositeDir, PersistedDoorState.Open);
            if (type != DoorType.Bombable)
            {
                Game.Sound.PlayEffect(SoundEffect.Door);
            }
            UpdateDoorTileBehavior(CurrentRoom, dir);
            UpdateDoorTiles(CurrentRoom, dir, roomState);
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
            _state.Play.CompleteWaterDryoutEvent();
            return;
        }

        if ((_state.Play.Timer % 8) == 0)
        {
            var colorSeq = Game.Data.OWPondColors;
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

        foreach (var bomb in GetObjects<BombActor>())
        {
            if (bomb.BombState != BombState.Fading) continue;

            // JOE: Why the + 8...?
            var bombX = bomb.X + 8;
            var bombY = bomb.Y + 8;

            foreach (var direction in TiledRoomProperties.DoorDirectionOrder)
            {
                var doorType = CurrentRoom.UnderworldDoors[direction];
                if (doorType != DoorType.Bombable) continue;

                var doorState = CurrentRoom.PersistedRoomState.IsDoorOpen(direction);
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
                if (_roomKillCount == 0 || RoomObj is { IsReoccuring: true })
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

        var rupeesToAdd = Profile.Items.Get(ItemSlot.RupeesToAdd);
        var rupeesToSubtract = Profile.Items.Get(ItemSlot.RupeesToSubtract);

        if (rupeesToAdd > 0 && rupeesToSubtract == 0)
        {
            if (Profile.Items.Get(ItemSlot.Rupees) < 255)
            {
                Profile.Items.Add(ItemSlot.Rupees, 1);
            }
            else
            {
                Profile.Items.Set(ItemSlot.RupeesToAdd, 0);
            }

            Game.Sound.PlayEffect(SoundEffect.Character);
        }
        else if (rupeesToAdd == 0 && rupeesToSubtract > 0)
        {
            if (Profile.Items.Get(ItemSlot.Rupees) > 0)
            {
                Profile.Items.Add(ItemSlot.Rupees, -1);
            }
            else
            {
                Profile.Items.Set(ItemSlot.RupeesToSubtract, 0);
            }

            Game.Sound.PlayEffect(SoundEffect.Character);
        }

        if (Profile.Items.Get(ItemSlot.RupeesToAdd) > 0) Profile.Items.Add(ItemSlot.RupeesToAdd, -1);
        if (Profile.Items.Get(ItemSlot.RupeesToSubtract) > 0) Profile.Items.Add(ItemSlot.RupeesToSubtract, -1);
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

        DrawObjects();

        if (IsLiftingItem())
        {
            DrawPlayerLiftingItem(_state.Play.LiftItemId);
        }
        else
        {
            Game.Player.Draw();
        }
    }

    private void DrawSubmenu()
    {
        using (var _ = Graphics.SetClip(0, TileMapBaseY + _submenuOffsetY, TileMapWidth, TileMapHeight - _submenuOffsetY))
        {
            ClearScreen();
            DrawMap(CurrentRoom, 0, _submenuOffsetY);
        }

        Menu.Draw(_submenuOffsetY);
    }

    private void DrawObjects()
    {
        foreach (var obj in _objects)
        {
            if (obj.IsDeleted) continue;

            obj.DecoratedDraw();
        }
    }

    private void DrawPrincessLiftingTriforce(int x, int y)
    {
        var image = Graphics.GetSpriteImage(TileSheet.Boss9, AnimationId.B3_Princess_Lift);
        image.Draw(TileSheet.Boss9, x, y, Palette.Player, DrawOrder.Sprites);

        GlobalFunctions.DrawItem(Game, ItemId.TriforcePiece, x, y - 0x10, 0, DrawOrder.Foreground);
    }

    private void DrawPlayerLiftingItem(ItemId itemId)
    {
        var animIndex = itemId == ItemId.TriforcePiece ? AnimationId.PlayerLiftHeavy : AnimationId.PlayerLiftLight;
        var image = Graphics.GetSpriteImage(TileSheet.PlayerAndItems, animIndex);
        image.Draw(TileSheet.PlayerAndItems, Game.Player.X, Game.Player.Y, Palette.Player, DrawOrder.Sprites);

        GlobalFunctions.DrawItem(Game, itemId, Game.Player.X, Game.Player.Y - 0x10, 0, DrawOrder.Foreground);
    }

    private void MakeObjects(Direction entryDir, ObjectState? entranceRoomsState, Entrance? fromEntrence)
    {
        // JOE: TODO: MAP REWRITE if (IsUWCellar(CurrentRoom))
        // JOE: TODO: MAP REWRITE {
        // JOE: TODO: MAP REWRITE     MakeCellarObjects();
        // JOE: TODO: MAP REWRITE     return;
        // JOE: TODO: MAP REWRITE }

        // I'm... not entirely sure what happens when both ShopSpec's hit?
        if (/*_curMode == GameMode.PlayCave && */fromEntrence?.Shop != null)
        {
            MakePersonRoomObjects(fromEntrence.Shop, entranceRoomsState);
        }

        if (CurrentRoom.CaveSpec != null)
        {
            // The nameof isn't my favorite here...
            var state = CurrentRoom.PersistedRoomState.GetObjectState(nameof(ShopSpec));
            MakePersonRoomObjects(CurrentRoom.CaveSpec, state);
        }

        var monstersEnterFromEdge = CurrentRoom.MonstersEnter;
        var monsterList = CurrentRoom.Monsters;
        var monsterCount = monsterList.Length;

        // Zoras are a bit special and are never not spawned.
        for (var i = 0; i < CurrentRoom.ZoraCount; i++)
        {
            Actor.AddFromType(ObjType.Zora, this, 0, 0);
        }

        if (monsterCount == 0) return;

        // It's kind of weird how this is handled in the actual game.
        var firstObject = monsterList[0].ObjType;

        CalcObjCountToMake(ref firstObject, ref monsterCount);

        RoomObjCount = monsterCount;
        var roomObj = GetObject<ItemObjActor>();

        var dirOrd = entryDir.GetOrdinal();
        var spots = Game.Data.SpawnSpot.AsSpan();
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

            var obj = Actor.AddFromType(type, this, x, y);
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

    private void MakePersonRoomObjects(ShopSpec spec, ObjectState? state)
    {
        ReadOnlySpan<int> fireXs = [0x48, 0xA8];

        if (spec.DwellerType != DwellerType.None)
        {
            // JOE: TODO: Fix CaveId.Cave1.
            var person = new PersonActor(this, state, CaveId.Cave1, spec, 0x78, 0x80);
            AddObject(person);
        }

        for (var i = 0; i < 2; i++)
        {
            var fire = new StandingFireActor(this, fireXs[i], 0x80);
            AddObject(fire);
        }
    }

    // JOE: TODO: This does not work in cellar/cave because of the use of _state.Play.
    public DeferredEvent DryoutWater()
    {
        if (!IsPlaying()) return DeferredEvent.CompletedEvent;

        _state.Play.AnimatingRoomColors = true;
        _state.Play.Timer = 88;
        return _state.Play.CreateWaterDryoutEvent();
    }

    private void MakeWhirlwind()
    {
        ReadOnlySpan<int> teleportYs = [0x8D, 0xAD, 0x8D, 0x8D, 0xAD, 0x8D, 0xAD, 0x5D];

        if (WhirlwindTeleporting != 0)
        {
            var y = teleportYs[_teleportingRoomIndex];

            WhirlwindTeleporting = 2;

            var whirlwind = new WhirlwindActor(this, 0, y);
            AddObject(whirlwind);

            Player.SetState(PlayerState.Paused);
            Player.X = whirlwind.X;
            Player.Y = 0xF8;
        }
    }

    private bool FindSpawnPos(ObjType type, ReadOnlySpan<PointXY> spots, int len, ref int x, ref int y)
    {
        var playerX = Game.Player.X;
        var playerY = Game.Player.Y;
        var objAttrs = Game.Data.GetObjectAttribute(type);
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
            var behavior = GetTileBehavior(col, row);

            if (behavior != TileBehavior.Sand && !behavior.CollidesTile()) break;
            if (y == _edgeY && x == _edgeX) break;
        }

        _edgeX = x;
        _edgeY = y;
        const int playerBoundary = 0x22;
        if (Math.Abs(Game.Player.X - x) >= playerBoundary
            || Math.Abs(Game.Player.Y - y) >= playerBoundary)
        {
            // Bring them in from the edge of the screen if player isn't too close.
            var obj = Actor.AddFromType(placeholder, this, x, y - 3);
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

            // Zora's always respawn.
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

        AddItemActor(itemId, x, y);
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
        GotoScroll(dir, CurrentRoom);
    }

    private void GotoScroll(Direction dir, GameRoom currentRoom)
    {
        if (dir == Direction.None) throw new ArgumentOutOfRangeException(nameof(dir));

        _state.Scroll.CurrentRoom = currentRoom;
        _state.Scroll.ScrollDir = dir;
        _state.Scroll.Substate = ScrollState.Substates.Start;

        if (CalcMazeStayPut(currentRoom.Maze, Game.Sound, dir, ref _curMazeStep))
        {
            _state.Scroll.NextRoom = currentRoom;
        }
        else
        {
            _state.Scroll.IsExitingWorld = !TryGetConnectedRoom(currentRoom, dir, out var nextRoom);
            _state.Scroll.NextRoom = nextRoom;
        }

        _curMode = GameMode.Scroll;
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

                var colorSeq = game.Data.OWPondColors;
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
            if (state.IsExitingWorld)
            {
                var entranceEntry = game.World._entranceHistory.TakePreviousEntranceOrDefault();
                game.World.GotoLoadLevel(entranceEntry);
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
    private static bool CalcMazeStayPut(MazeRoom? maze, ISound sound, Direction dir, ref int currentMazeStep)
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
        UpdateLeaveInner(Game, ref _state.Leave);

        return;
        static void UpdateLeaveInner(Game game, ref LeaveState state)
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
    }

    private void DrawLeave()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        DrawRoomNoObjects(false);
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

    private void GotoEnter(Direction dir, EntranceHistoryEntry? entranceEntry = null)
    {
        _state.Enter.Substate = EnterState.Substates.Start;
        _state.Enter.ScrollDir = dir;
        _state.Enter.Timer = 0;
        _state.Enter.PlayerSpeed = Player.WalkSpeed;
        _state.Enter.GotoPlay = false;
        _state.Enter.EntranceEntry = entranceEntry;

        Player.DrawOrder = DrawOrder.Player;
        _stateCleanup = MakePlayerNormalDrawingOrder;

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
                UpdateDoorTiles(CurrentRoom, origShutterDoorDir, CurrentRoom.PersistedRoomState);
            }

            _statusBar.EnableFeatures(StatusBarFeatures.All, true);
            if (IsOverworld() && Player.FromUnderground)
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
                    state.ScrollDir = Direction.Up;
                    state.TargetX = game.Player.X;
                    state.TargetY = game.Player.Y - (Game.Cheats.SpeedUp ? 0 : 0x10);
                    state.Substate = EnterState.Substates.WalkCave;

                    game.Player.DrawOrder = DrawOrder.BehindBackground;

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
        DrawRoomNoObjects(false);
    }

    private void SetPlayerExitPosOW(GameRoom room)
    {
        // if (room.ExitPosition != null) // JOE: TODO: MAP REWRITE. This check might not be right.
        // {
        //     Game.Player.X = room.ExitPosition.X;
        //     Game.Player.Y = room.ExitPosition.Y;
        // }
    }

    public void GotoLoadOverworld() => GotoLoadLevel(GameWorldType.Overworld, "Overworld");
    public void GotoLoadLevel(EntranceHistoryEntry entrance)
    {
        GotoLoadLevel(entrance.Room.GameWorld, entrance);
    }
    public void GotoLoadLevel(int levelNumber) => GotoLoadLevel(GameWorldType.Underworld, $"00_{levelNumber:D2}"); // JOE: TODO: Quests
    public void GotoLoadLevel(GameWorldType type, string destination) => GotoLoadLevel(GetWorld(type, destination));
    public void GotoLoadLevel(GameWorld world, EntranceHistoryEntry? entranceEntry = null)
    {
        _state.LoadLevel.GameWorld = world;
        _state.LoadLevel.EntranceEntry = entranceEntry;
        _state.LoadLevel.Substate = LoadLevelState.Substates.Load;
        _state.LoadLevel.Timer = 0;

        _curMode = GameMode.LoadLevel;
    }

    private void UpdateLoadLevel()
    {
        UpdateLoadLevelInner(Game, ref _state.LoadLevel);
        return;

        static void UpdateLoadLevelInner(Game game, ref LoadLevelState state)
        {
            switch (state.Substate)
            {
                case LoadLevelState.Substates.Load:
                    state.Timer = LoadLevelState.StateTime;
                    state.Substate = LoadLevelState.Substates.Wait;

                    game.Sound.StopAll();
                    game.World._statusBarVisible = false;
                    game.World.LoadWorld(state.GameWorld, state.EntranceEntry);
                    break;

                case LoadLevelState.Substates.Wait when state.Timer == 0:
                    game.World.GotoUnfurl();
                    return;

                case LoadLevelState.Substates.Wait:
                    state.Timer--;
                    break;
            }
        }
    }

    private void DrawLoadLevel()
    {
        using var _ = Graphics.SetClip(0, 0, Global.StdViewWidth, Global.StdViewHeight);
        ClearScreen();
    }

    public void GotoUnfurl(EntranceHistoryEntry? entranceEntry = null)
    {
        _state.Unfurl.Substate = UnfurlState.Substates.Start;
        _state.Unfurl.Timer = UnfurlState.StateTime;
        _state.Unfurl.StepTimer = 0;
        _state.Unfurl.Left = 0x80;
        _state.Unfurl.Right = 0x80;
        _state.Unfurl.EntranceEntry = entranceEntry;

        ClearLevelData();

        _curMode = GameMode.Unfurl;
        _stateCleanup = MakePlayerNormalDrawingOrder;
    }

    private void UpdateUnfurl()
    {
        if (_state.Unfurl.Substate == UnfurlState.Substates.Start)
        {
            _state.Unfurl.Substate = UnfurlState.Substates.Unfurl;
            _statusBarVisible = true;
            _statusBar.EnableFeatures(StatusBarFeatures.All, false);

            var position = _state.Unfurl.EntranceEntry?.FromEntrance.ExitPosition;
            if (position != null)
            {
                Player.X = position.X;
                Player.Y = position.Y;
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
            DrawRoomNoObjects(true);
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
                GotoLoadOverworld();
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
            DrawRoomNoObjects(true);
        }

        DrawPlayerLiftingItem(ItemId.TriforcePiece);
    }

    public void GotoStairs(Entrance entrance, ObjectState state)
    {
        if (entrance == null) throw new Exception("Unable to locate stairs action object.");
        if (entrance.Destination == null) throw new Exception("Stairs do not target a proper location.");

        _state.Stairs.Substate = StairsState.Substates.Start;
        _state.Stairs.Entrance = entrance;
        _state.Stairs.ObjectState = state;

        Player.DrawOrder = DrawOrder.Player;

        _entranceHistory.Push(CurrentRoom, entrance);

        _curMode = GameMode.Stairs;
    }

    private void UpdateStairsState()
    {
        switch (_state.Stairs.Substate)
        {
            case StairsState.Substates.Start:
                if (IsOverworld()) Game.Sound.StopAll();

                if (_state.Stairs.Entrance.Animation == EntranceAnimation.Descend)
                {
                    Player.DrawOrder = DrawOrder.BehindBackground;
                    Player.Facing = Direction.Up;

                    _state.Stairs.TargetX = Player.X;
                    _state.Stairs.TargetY = Player.Y + (Game.Cheats.SpeedUp ? 0 : 0x10);
                    _state.Stairs.ScrollDir = Direction.Down;
                    _state.Stairs.PlayerSpeed = 0x40;
                    _state.Stairs.PlayerFraction = 0;

                    _state.Stairs.Substate = StairsState.Substates.WalkCave;
                    Game.Sound.PlayEffect(SoundEffect.Stairs);
                }
                else
                {
                    Player.Visible = false;

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
                GotoLoadLevel(entrance.DestinationType, entrance.Destination);
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
    private void LoadEntranceRoom(Entrance entrance, int? defaultX, int? defaultY, out int? destinationY)
    {
        var world = entrance.DestinationType switch
        {
            GameWorldType.OverworldCommon => GameWorld.Load(this, "Maps/OverworldCommon.world", 1),
            GameWorldType.UnderworldCommon => GameWorld.Load(this, "Maps/UnderworldCommon.world", 1),
            _ => throw new Exception($"Unsupported entrance type \"{entrance.DestinationType}\""),
        };

        var room = world.GetRoomByName(entrance.Destination);

        if (entrance.Arguments != null)
        {
            // This must happen before LoadMap creates the objects.
            room.InitializeInteractiveGameObjects(entrance.Arguments);
        }

        LoadMap(room);

        destinationY = null;
        var pos = entrance.EntryPosition;
        if (pos != null)
        {
            Player.X = pos.X;
            Player.Y = pos.Y;
            destinationY = pos.TargetY;
            if (pos.Facing != Direction.None) Player.Facing = pos.Facing;
        }
        else
        {
            if (defaultX != null) Player.X = defaultX.Value;
            if (defaultY != null) Player.Y = defaultY.Value;
        }
    }

    public void ReturnToPreviousEntrance()
    {
        ReturnToPreviousEntrance(Game.Player.Facing);
    }

    public void ReturnToPreviousEntrance(Direction facing)
    {
        if (_curMode is GameMode.PlayCellar or GameMode.PlayCave or GameMode.PlayShortcuts
            ) //|| !CurrentWorld.IsOverworld)
        {
            GotoLeaveCellar();
        }
        else
        {
            GotoLeave(facing);
        }
    }

    private void DrawStairsState()
    {
        using var _ = Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        DrawRoomNoObjects(false);
    }

    private void GotoPlayCellar(Entrance entrance, ObjectState? state)
    {
        _state.PlayCellar.Entrance = entrance;
        _state.PlayCellar.ObjectState = state;
        _state.PlayCellar.Substate = PlayCellarState.Substates.Start;

        Player.Visible = false;

        _curMode = GameMode.InitPlayCellar;
    }

    private void UpdatePlayCellar()
    {
        _stateCleanup = MakePlayerNormalDrawingOrder;
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
            game.World.LoadEntranceRoom(entrance, 0x30, 0x44, out var targetY);

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
            game.World.Player.Visible = true;
            game.World.Player.DrawOrder = DrawOrder.Player;

            _traceLog.Write($"PlayerCellarWalk: Game.Player.Y >= state.TargetY {game.Player.Y} >= {state.TargetY}");
            if (game.Player.Y >= state.TargetY)
            {
                game.World.Player.FromUnderground = true;
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
        DrawRoomNoObjects(false);
    }

    public void GotoLeaveCellar()
    {
        _state.LeaveCellar.Substate = LeaveCellarState.Substates.Start;
        _state.LeaveCellar.TargetEntrance = _entranceHistory.TakePreviousEntranceOrDefault();
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
            if (state.TargetEntrance.Room.GameWorld.IsOverworld)
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

            var palette = game.World.CurrentWorld.Settings.InCellarPalette;
            for (var i = 0; i < LevelInfoBlock.FadePals; i++)
            {
                var step = state.FadeStep;
                Graphics.SetPaletteIndexed((Palette)i + 2, palette[step][i]);
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
            var entry = state.TargetEntrance;

            // JOE: TODO: MAP REWRITE This is no longer used!!!
            var nextRoomId = game.Player.X < 0x80
                ? entry.FromEntrance.Arguments?.ExitLeft
                : entry.FromEntrance.Arguments?.ExitRight;

            if (nextRoomId == null)
            {
                throw new Exception($"Missing CellarStairs[Left/Right]RoomId attributes in room \"{entry.Room}\"");
            }

            game.World.LoadRoom(entry.Room);

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

            var palette = game.World.CurrentWorld.Settings.OutOfCellarPalette;
            for (var i = 0; i < LevelInfoBlock.FadePals; i++)
            {
                var step = state.FadeStep;
                Graphics.SetPaletteIndexed((Palette)i + 2, palette[step][i]);
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
            game.World.LoadRoom(state.TargetEntrance.Room);
            var exitPosition = state.TargetEntrance.FromEntrance.ExitPosition;
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
                DrawRoomNoObjects(true);
                break;
        }
    }

    private void GotoPlayCave(Entrance entrance, ObjectState? state)
    {
        _state.PlayCave.Substate = PlayCaveState.Substates.Start;
        _state.PlayCave.Entrance = entrance;
        _state.PlayCave.ObjectState = state;

        Player.Visible = true;

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
            var paletteSet = game.Data.CavePalette;
            // var caveLayout = FindSparseFlag(Sparse.Shortcut, CurrentRoom) ? CaveType.Shortcut : CaveType.Items;

            // LoadCaveRoom(caveLayout);
            var entrance = state.Entrance ?? throw new Exception();
            game.World.LoadEntranceRoom(entrance, 0x70, 0xDD, out var targetY);

            state.Substate = PlayCaveState.Substates.Walk;
            state.TargetY = targetY ?? 0xD5;

            game.Player.Facing = Direction.Up;

            for (var i = 0; i < 2; i++)
            {
                Graphics.SetPaletteIndexed((Palette)i + 2, paletteSet.GetByIndex(i));
            }
            Graphics.UpdatePalettes();
        }

        static void PlayCaveWalk(Game game, ref PlayCaveState state)
        {
            _traceLog.Write($"PlayCaveWalk: Game.Player.Y <= state.TargetY {game.Player.Y} <= {state.TargetY}");
            if (game.Player.Y <= state.TargetY)
            {
                game.World.Player.FromUnderground = true;
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
                DrawRoomNoObjects(false);
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
                DrawRoomNoObjects(true);
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
                        Player.FromUnderground = true;
                        Game.Player.Initialize();
                        Profile.Hearts = PlayerProfile.GetMaxHeartsValue(PersistedItems.DefaultHeartCount);
                        Unpause(); // It's easy for select+start to also pause the game, and that's confusing.
                        LoadOverworld();
                        GotoUnfurl();
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

    private void DrawRoomNoObjects(bool skipPlayer)
    {
        ClearScreen();

        DrawRoom();
        if (!skipPlayer) Game.Player.Draw();
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

        var activatedObj = Actor.AddFromType(type, this, x, y);
        activatedObj.ObjTimer = 0x40;

        return activatedObj;
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
                if (GetEffectiveDoorState(CurrentRoom, player.MovingDirection))
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

    public Actor AddItemActor(ItemId itemId, int x, int y, ItemObjectOptions options = ItemObjectOptions.None)
    {
        Actor actor = itemId == ItemId.Fairy
            ? new FairyActor(this, x, y)
            : new ItemObjActor(this, itemId, options, x, y);

        _objects.Add(actor);
        return actor;
    }

    private void MakePlayerNormalDrawingOrder()
    {
        // Player.DrawOrder = DrawOrder.Player;
        // Player.Visible = true;
    }
}