using System.Runtime.CompilerServices;
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

    public World World { get; private set; }
    public Input Input { get; }
    public GameCheats GameCheats { get; }
    public GameConfiguration Configuration { get; } = SaveFolder.Configuration;
    public Random Random { get; } = new(); // Do not use for things like particle effects, this is the seedable random.
    public OnScreenDisplay OnScreenDisplay { get; } = new();
    public DebugInfo DebugInfo { get; }
    public PregameMenu Menu { get; }

    public int FrameCounter { get; private set; }

    public Game()
    {
        World = new World(this);
        Input = new Input(Configuration.Input);
        GameCheats = new GameCheats(this, Input);
        Sound = new Sound(Configuration.Audio);
        DebugInfo = new DebugInfo(this, Configuration.DebugInfo);
        Menu = new PregameMenu(this, SaveFolder.Profiles.Profiles);
    }

    public void Start(PlayerProfile profile)
    {
        World = new World(this, profile);
    }

    public void Update()
    {
        ++FrameCounter;

        CheckInput();
        if (!Menu.UpdateIfActive()) World.Update();
        Sound.Update();
        GameCheats.Update();

        // Input comes last because it marks the buttons as old. We read them on a callback which happens async.
        Input.Update();
    }

    private void CheckInput()
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

        if (Input.IsButtonPressing(GameButton.ToggleDebugInfo))
        {
            Configuration.DebugInfo.Enabled = !Configuration.DebugInfo.Enabled;
        }
    }

    public void Draw()
    {
        if (!Menu.DrawIfActive()) World.Draw();
        OnScreenDisplay.Draw();
        DebugInfo.Draw();
    }

    // JOE: TODO: Move to TryShoot pattern?
    public FireballProjectile? ShootFireball(ObjType type, int x, int y, int? offset = null)
    {
        // JOE: TODO: Need to limit amount of projectiles.
        var fireball = new FireballProjectile(this, type, x + 4, y, 1.75f, offset);
        World.AddObject(fireball);
        return fireball;
    }

    public void AutoSave(bool check = true, bool announce = true, [CallerMemberName] string functionName = "")
    {
        if (check && !Configuration.Enhancements.AutoSave) return;

        _log.Write("AutoSave", $"Saving from {functionName}");

        if (announce) Toast("Auto-saving...");

        SaveFolder.SaveProfiles();
    }

    public void Toast(string text) => OnScreenDisplay.Toast(text);
}
