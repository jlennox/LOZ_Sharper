using System.Runtime.CompilerServices;
using z1.Common.IO;
using z1.IO;
using z1.Render;
using z1.UI;

namespace z1;

internal sealed class GameIO
{
    public GameConfiguration Configuration { get; } = SaveFolder.Configuration;
    public ISound Sound { get; set; }
    public Input Input { get; }
    public Random Random { get; private set; } // Do not use for things like particle effects, this is the seedable random.

    public GameIO()
    {
        Input = new Input(Configuration.Input);
        Sound = new Sound(Configuration.Audio);
        var seed = Random.Shared.Next();
        Random = new Random(seed);
    }
}

internal enum GameControllerState
{
    ProfileSelection,
    Game
}

internal sealed class GameController
{
    public GameIO IO { get; }

    private GameControllerState _state;

    public GameController()
    {
        IO = new GameIO();

        _state = GameControllerState.ProfileSelection;
    }
}

internal sealed class Game
{
    private static readonly DebugLog _log = new(nameof(Game));

    public static class Cheats
    {
        public static bool SpeedUp = false;
        public static bool GodMode = false;
        public static bool NoClip = false;
        public static bool ToggleMhzDisaster = false;
        public static bool EnableMhzDisaster = false;
        public static int MhzDisaster = 3;
    }

    public GameEnhancements Enhancements => Configuration.Enhancements;

    public World World { get; }
    public Player Player { get; set; }
    public GameConfiguration Configuration => _io.Configuration;
    public ISound Sound => _io.Sound;
    public Input Input => _io.Input;
    public Random Random => _io.Random;
    public GameCheats GameCheats { get; }
    public OnScreenDisplay OnScreenDisplay { get; } = new();
    public DebugInfo DebugInfo { get; }
    public PregameMenu Menu { get; }
    public GameRecording Recording { get; }
    public GamePlayback? Playback { get; }
    public bool Headless { get; init; }
    public GameData Data { get; }

    public int FrameCounter { get; private set; }

    private readonly GameIO _io;

    public Game(GameIO io)
    {
        _io = io;
        Data = new Asset(Filenames.GameData).ReadJson<GameData>();

        World = new World(this);
        Player = new Player(World);
        GameCheats = new GameCheats(this, Input);
        DebugInfo = new DebugInfo(this, Configuration.DebugInfo);
        Menu = new PregameMenu(this, SaveFolder.Profiles.Profiles);
        var seed = Random.Shared.Next();
        Recording = new GameRecording(this, seed);

        Menu.OnProfileSelected += SetProfile;
    }

    public Game(GameIO io, PlayerProfile playerProfile)
    {
        _io = io;
        Data = new Asset(Filenames.GameData).ReadJson<GameData>();

        World = new World(this);
        Player = new Player(World);
        GameCheats = new GameCheats(this, Input);
        DebugInfo = new DebugInfo(this, Configuration.DebugInfo);
        Menu = new PregameMenu(this, SaveFolder.Profiles.Profiles);
        var seed = Random.Shared.Next();
        Recording = new GameRecording(this, seed);

        SetProfile(playerProfile);
    }

    public Game(GameIO io, GameRecordingState playback, bool headless = false) : this(io)
    {
        Headless = headless;
        if (headless) io.Sound = new NullSound();

        Playback = new GamePlayback(this, playback);
        Menu.StartWorld(PlayerProfile.CreateForRecording());
    }

    private void SetProfile(PlayerProfile profile)
    {
        Player.Profile = profile;
        profile.Start();
        World.Start();

        if (profile.RandomizerSeed != null)
        {
            Randomizer.Randomizer.Create(World.CurrentWorld, new RandomizerState(profile.RandomizerSeed.Value, new()));
        }
    }

    public void Update()
    {
        ++FrameCounter;

        if (Playback is { Enabled: true } && !Playback.Playback(this))
        {
            Playback.Enabled = false;
            Toast("Recording: Playback ended.");
        }

        CheckInput();
        if (!Menu.UpdateIfActive()) World.Update();
        Sound.Update();
        GameCheats.Update();
        Recording.Record();

        // Input comes last because it marks the buttons as old. We read them on a callback which happens async.
        Input.Update();
    }

    private void CheckFunctions()
    {
        if (Input.IsFunctionPressing(FunctionButton.BeginRecording))
        {
            Recording.BeginRecording();
            Toast("Recording: Enabled");
        }

        if (Input.IsFunctionPressing(FunctionButton.WriteRecording))
        {
            Recording.WriteRecording();
            Toast("Recording: Saved");
        }

        if (Input.IsFunctionPressing(FunctionButton.WriteRecordingAssert))
        {
            Recording.AddAssertion();
            Toast("Recording: Created assertion");
        }
    }

    private void CheckInput()
    {
        CheckFunctions();

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

        Cheats.ToggleMhzDisaster = Input.IsButtonDown(GameButton.ToggleMhzDisaster);
        if (Input.IsButtonPressing(GameButton.IncreaseMhzDisaster)) Cheats.MhzDisaster++;
        if (Input.IsButtonPressing(GameButton.DecreaseMhzDisaster)) Cheats.MhzDisaster--;
    }

    public void Draw()
    {
        if (Headless) return;

        if (!Menu.DrawIfActive()) World.Draw();
        OnScreenDisplay.Draw();
        DebugInfo.Draw();
        Graphics.FinishRender();
    }

    // JOE: TODO: Move to TryShoot pattern?
    public FireballProjectile? ShootFireball(ObjType type, int x, int y, int? offset = null)
    {
        // JOE: TODO: Need to limit amount of projectiles.
        var fireball = new FireballProjectile(World, type, x + 4, y, 1.75f, offset);
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
