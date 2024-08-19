using System.Runtime.CompilerServices;
using SkiaSharp;
using z1.Actors;
using z1.IO;
using z1.UI;

namespace z1;

internal sealed class Game
{
    private static readonly DebugLog _log = new(nameof(Game));

    public Link Link;

    public readonly Sound Sound;

    public static class Cheats
    {
        public static bool SpeedUp = false;
        public static bool GodMode = false;
        public static bool NoClip = false;
    }

    public GameEnhancements Enhancements => Configuration.Enhancements;

    public World World;
    public Input Input;
    public GameCheats GameCheats;
    public GameConfiguration Configuration = SaveFolder.Configuration;
    public readonly OnScreenDisplay OnScreenDisplay = new();

    public int FrameCounter { get; private set; }

    public Game()
    {
        World = new World(this);
        Input = new Input(Configuration.Input);
        GameCheats = new GameCheats(this, Input);
        Sound = new Sound(Configuration.Audio);
    }

    public void Update()
    {
        ++FrameCounter;

        GameUpdate();
        World.Update();
        Sound.Update();
        GameCheats.Update();

        // Input comes last because it marks the buttons as old. We read them on a callback which happens async.
        Input.Update();
    }

    private void GameUpdate()
    {
        if (Input.IsButtonPressing(GameButton.AudioDecreaseVolume))
        {
            var volume = Sound.DecreaseVolume();
            Toast($"Volume:{volume}%");
        }

        if (Input.IsButtonPressing(GameButton.AudioIncreaseVolume))
        {
            var volume = Sound.IncreaseVolume();
            Toast($"Volume:{volume}%");
        }

        if (Input.IsButtonPressing(GameButton.AudioMuteToggle))
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

    // JOE: TODO: Move to TryShoot pattern?
    public ObjectSlot ShootFireball(ObjType type, int x, int y)
    {
        if (World.TryFindEmptyMonsterSlot(out var slot))
        {
            var fireball = new FireballProjectile(this, type, x + 4, y, 1.75f);
            World.SetObject(slot, fireball);
            return slot;
        }

        return ObjectSlot.NoneFound;
    }

    public void AutoSave(bool announce = true, [CallerMemberName] string functionName = "")
    {
        _log.Write("AutoSave", $"Saving from {functionName}");

        if (announce)
        {
            Toast("Auto-saving...");
        }

        SaveFolder.SaveProfiles();
    }

    public void Toast(string text) => OnScreenDisplay.Toast(text);
}
