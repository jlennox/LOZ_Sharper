using SkiaSharp;
using z1.Actors;

namespace z1;

[Flags]
internal enum Direction
{
    None = 0,
    Right = 1,
    Left = 2,
    Down = 4,
    Up = 8,
    DirectionMask = 0x0F,
    FullMask = 0xFF,
    VerticalMask = Down | Up,
    HorizontalMask = Left | Right,
    OppositeVerticals = VerticalMask,
    OppositeHorizontals = HorizontalMask,
}

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

    // Use rect?
    public int MarginTop => 0x5D;
    public int MarginBottom => 0xCD;
    public int MarginLeft => 0x20;
    public int MarginRight => 0xD0;

    public readonly Sound Sound = new();
    public readonly PlayerProfile Profile = new();

    public static bool SpeedUp = true;

    public Game()
    {
        World = new World(this);
        Link = new(this);
        ChaseTarget = Link;
    }

    // TODO:
    public Actor ObservedPlayer => Link;

    public World World;

    public void SetKeys(Keys keys) => KeyCode = keys;
    public void UnsetKeys() => KeyCode = Keys.None;

    public int FrameCounter = 0;
    public int GetFrameCounter() => FrameCounter;

    public Direction GetXDirToPlayer(int x)
    {
        Point playerPos = World.GetObservedPlayerPos();

        if (playerPos.X < x)
            return Direction.Left;
        else
            return Direction.Right;
    }

    // JOE: TODO: This is a duplicate.
    public Direction GetYDirToPlayer(int y)
    {
        Point playerPos = World.GetObservedPlayerPos();

        if (playerPos.Y < y)
            return Direction.Up;
        else
            return Direction.Down;
    }

    public void UpdateScreenSize(SKSurface surface, SKImageInfo info)
    {
        // ++FrameCounter;
        const int NesResX = 256;
        const int NesResY = 240;

        var scale = Math.Min((float)info.Width / NesResX, (float)info.Height / NesResY);
        var offsetX = (info.Width - scale * NesResX) / 2;
        var offsetY = (info.Height - scale * NesResY) / 2;

        surface.Canvas.Translate(offsetX, offsetY);
        surface.Canvas.Scale(scale, scale);
    }

    public void ShootFireball(ObjType type, int x, int y)
    {
        var newSlot = World.FindEmptyMonsterSlot();
        if (newSlot >= 0)
        {
            var fireball = new Fireball(this, type, x + 4, y, 1.75f);
            World.SetObject(newSlot, fireball);
        }
    }
}
