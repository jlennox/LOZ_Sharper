using SkiaSharp;
using z1.Actors;
using z1.IO;
using z1.UI;

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
    public Link Link;

    public readonly Sound Sound;

    public static class Cheats
    {
        public static bool SpeedUp = false;
        public static bool GodMode = false;
        public static bool NoClip = false;
    }

    public bool Enhancements => Configuration.EnableEnhancements;

    public World World;
    public Input Input;
    public GameCheats GameCheats;
    public GameConfiguration Configuration = SaveFolder.Configuration;
    public readonly OnScreenDisplay OnScreenDisplay = new();

    public int FrameCounter = 0;
    public int GetFrameCounter() => FrameCounter;

    public Game()
    {
        World = new World(this);
        Input = new Input(Configuration.Input);
        GameCheats = new GameCheats(this, Input);
        Sound = new Sound(Configuration.AudioVolume);
    }

    public void Update()
    {
        WorldUpdate();
        World.Update();
        Sound.Update();
        GameCheats.Update();

        // Input comes last because it marks the buttons as old.
        Input.Update();
    }

    private void WorldUpdate()
    {
        if (Input.IsButtonPressing(GameButton.AudioDecreaseVolume))
        {
            var volume = Sound.DecreaseVolume();
            Toast($"Volume: {volume}%");
        }
        else if (Input.IsButtonPressing(GameButton.AudioIncreaseVolume))
        {
            var volume = Sound.IncreaseVolume();
            Toast($"Volume: {volume}%");
        }
        else if (Input.IsButtonPressing(GameButton.AudioMuteToggle))
        {
            var isMuted = Sound.ToggleMute();
            Toast(isMuted ? "Sound muted" : "Sound unmuted");
        }
    }

    public void Draw()
    {
        World.Draw();
        OnScreenDisplay.Draw();
    }

    // JOE: TODO: This function is a bit weird now.
    public void UpdateScreenSize(SKSurface surface)
    {
        const int nesWidth = 256;
        const int nesHeight = 240;

        surface.Canvas.GetLocalClipBounds(out var bounds);

        var scale = Math.Min(bounds.Width / nesWidth, bounds.Height / nesHeight);
        var offsetX = (bounds.Width - scale * nesWidth) / 2;
        var offsetY = (bounds.Height - scale * nesHeight) / 2;

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

    public void Toast(string text) => OnScreenDisplay.Toast(text);
}
