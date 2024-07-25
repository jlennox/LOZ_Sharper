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

internal sealed class Game
{
    const float AspectRatio = 16 / 9;

    public Link Link;

    public readonly Sound Sound = new();

    public static class Cheats
    {
        public static bool SpeedUp = true;
        public static bool GodMode = true;
        public static bool NoClip = true;
    }

    public bool Enhancements = true;

    public World World;
    public Input Input = new();
    public GameCheats GameCheats;

    public int FrameCounter = 0;
    public int GetFrameCounter() => FrameCounter;

    public Game()
    {
        World = new World(this);
        GameCheats = new GameCheats(this);
    }

    public void UpdateScreenSize(SKSurface surface)
    {
        const int NesResX = 256;
        const int NesResY = 240;

        surface.Canvas.GetLocalClipBounds(out var bounds);

        var scale = Math.Min(bounds.Width / NesResX, bounds.Height / NesResY);
        var offsetX = (bounds.Width - scale * NesResX) / 2;
        var offsetY = (bounds.Height - scale * NesResY) / 2;

        surface.Canvas.Translate(offsetX, offsetY);
        surface.Canvas.Scale(scale, scale);
    }

    public void ShootFireball(ObjType type, int x, int y)
    {
        var newSlot = World.FindEmptyMonsterSlot();
        if (newSlot >= 0)
        {
            var fireball = new FireballProjectile(this, type, x + 4, y, 1.75f);
            World.SetObject(newSlot, fireball);
        }
    }

    public void Toast(string text) => World.OnScreenDisplay.Toast(text);
}
