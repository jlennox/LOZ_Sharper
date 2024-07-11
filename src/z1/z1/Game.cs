using SkiaSharp;
using z1.Actors;

namespace z1;

[Flags]
internal enum Direction { None = 0, Right = 1, Left = 2, Down = 4, Up = 8, Mask = 0x0F, FullMask = 0xFF }
internal enum TileSheet { Background, PlayerAndItems, Npcs, Boss, Font, Max }

internal enum LevelBlock
{
    Width = 16,
    Height = 8,
    Rooms = 128,
}

enum GameMode
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

internal sealed class Game
{
    const float AspectRatio = 16 / 9;
    const int MaxProjectiles = 11;

    public static readonly SKColor[][] Palettes = z1.Palettes.GetPalettes();

    public Keys KeyCode { get; private set; } = Keys.None;

    public Link Link;
    public Actor ChaseTarget { get; set; }

    private SKSurface? _surface;
    public readonly List<Actor> _actors = new();
    public readonly List<Actor> _projectiles = new();

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

    // Use rect?
    public int MarginTop => 0x5D;
    public int MarginBottom => 0xCD;
    public int MarginLeft => 0x20;
    public int MarginRight => 0xD0;

    public int CurColorSeqNum;
    public int DarkRoomFadeStep;
    public int CurMazeStep;
    public int SpotIndex;
    public int TempShutterRoomId;
    public int TempShutterDoorDir;
    public int TempShuttersRoomId;
    public bool TempShutters;
    public bool PrevRoomWasCellar;
    public int SavedOWRoomId;
    public int EdgeX;
    public int EdgeY;
    public int NextRoomHistorySlot;    // 620
    public int RoomObjCount;           // 34E
    public int RoomObjId;              // 35F
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
    public byte[] LevelKillCounts = new byte[(int)LevelBlock.Rooms];
    public byte[] RoomHistory = new byte[RoomHistoryLength];

    public GameMode LastMode;
    public GameMode CurrentMode;

    public readonly Sound Sound = new();
    public readonly PlayerProfile Profile = new();
    public readonly WorldState State = new();

    public bool IsOverworld => true; // TODO

    public SKSurface Surface => _surface ?? throw new InvalidOperationException("Surface not set");

    public Game()
    {
        World = new World(this);
        Link = new(this);
        ChaseTarget = Link;
        _actors.Add(Link);
    }

    // TODO:
    public Actor ObservedPlayer => Link;

    public World World;

    public Direction GetXDirToPlayer(int x)
    {
        var playerPos = ObservedPlayer.Position;
        return playerPos.X < x ? Direction.Left : Direction.Right;
    }

    public Direction GetYDirToPlayer(int y)
    {
        var playerPos = ObservedPlayer.Position;
        return playerPos.Y < y ? Direction.Up : Direction.Down;
    }

    public void SetKeys(Keys keys) => KeyCode = keys;
    public void UnsetKeys() => KeyCode = Keys.None;

    public void UpdateActors()
    {
        var surface = Surface;
        surface.Canvas.Clear();
        Link.Move(this);
        foreach (var actor in _actors) actor.Update();
        foreach (var actor in _actors) actor.Draw();
    }

    public bool AddProjectile(Projectile projectile)
    {
        if (_projectiles.Count >= MaxProjectiles) return false;
        _projectiles.Add(projectile);
        return true;
    }

    public TileCollision CollidesWithTileMoving(int x, int y, Direction dir, bool isPlayer)
    {
        // TODO:
        return new TileCollision();
    }

    private readonly Dictionary<ObjectSlot, Actor> _objects = new();
    private readonly List<Actor> _objectsToDelete = new();
    // private int _objectsToDeleteCount;
    // private int _objectTimers[MaxObjects];
    public ObjectSlot CurrentObjectSlot;
    // private int _longTimer;
    // private int _stunTimers[MaxObjects];
    // private uint8_t _placeholderTypes[MaxObjects];

    public int GetItem(ItemSlot slot) => Profile.Items.GetValueOrDefault(slot);
    public int DecrementItem(ItemSlot slot) => Profile.Items[slot] = GetItem(slot) - 1;
    public Actor? GetObject(ObjectSlot slot) => _objects.GetValueOrDefault(slot);
    public Actor SetObject(ObjectSlot slot, Actor obj) => _objects[slot] = obj;
    public T? GetObject<T>(ObjectSlot slot) where T : Actor => (T)GetObject(slot);

    private int _frameCounter = 0;
    public int GetFrameCounter() => _frameCounter;

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

    public void GotoDie()
    {
        State.Death.Substate = DeathState.Substates.Start;
        CurrentMode = GameMode.Death;
    }

    public void UpdateScreenSize(SKSurface surface, SKImageInfo info)
    {
        ++_frameCounter;
        const int NesResX = 256;
        const int NesResY = 240;

        _surface = surface;

        var scale = Math.Min((float)info.Width / NesResX, (float)info.Height / NesResY);
        var offsetX = (info.Width - scale * NesResX) / 2;
        var offsetY = (info.Height - scale * NesResY) / 2;

        surface.Canvas.Translate(offsetX, offsetY);
        surface.Canvas.Scale(scale, scale);
    }

    public void DrawBitmap(TileSheet sheet, SKBitmap bitmap, Point point)
    {
        DrawBitmap(sheet, bitmap, point.X, point.Y);
    }

    public void DrawBitmap(TileSheet sheet, SKBitmap bitmap, int x, int y)
    {
        var surface = _surface ?? throw new InvalidOperationException("Surface not set");
        var canvas = surface.Canvas;
        canvas.DrawBitmap(bitmap, x, y);
    }
}
