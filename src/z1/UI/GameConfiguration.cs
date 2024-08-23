using System.Text.Json.Serialization;
using Silk.NET.Input;
using z1.IO;
using KeyboardMap = System.Collections.Generic.IReadOnlyDictionary<z1.UI.KeyboardMapping, z1.GameButton>;
using GamepadMap = System.Collections.Generic.IReadOnlyDictionary<z1.UI.GamepadButton, z1.GameButton>;

namespace z1.UI;

// This is a pretty close clone of Silk.NET.Input.ButtonName. This is because the trigger's were not
// in the original enum. I left space after the last enum in-case they expand theirs in the future.
internal enum  GamepadButton
{
    A = 0,
    B = 1,
    X = 2,
    Y = 3,
    BumperLeft = 4,
    BumperRight = 5,
    Back = 6,
    Start = 7,
    Home = 8,
    StickLeft = 9,
    StickRight = 10,
    DPadUp = 11,
    DPadRight = 12,
    DPadDown = 13,
    DPadLeft = 14,

    TriggerLeft = 100,
    TriggerRight = 101,
}

internal sealed class GameEnhancements
{
    public bool AutoSave { get; set; }
    public bool RedCandleLightsDarkRooms { get; set; }
    public bool ImprovedMenus { get; set; }
    public bool EasySaveMenu { get; set; }
    public int TextSpeed { get; set; } = 1; // 1 is normal, 5 is max speed.

    public static GameEnhancements MakeDefaults() => new()
    {
        AutoSave = false,
        RedCandleLightsDarkRooms = false,
        ImprovedMenus = true,
        EasySaveMenu = true,
        TextSpeed = 1,
    };

    public void Initialize()
    {
        TextSpeed = Math.Clamp(TextSpeed, 1, 5);
    }
}

internal sealed class AudioConfiguration
{
    public bool Mute { get; set; } = false;
    public bool MuteMusic { get; set; } = true;
    public int Volume { get; set; } = 80; // 0 to 100. Default to 80 because it sounds really loud at 100.

    public static AudioConfiguration MakeDefaults() => new();

    public void Initialize()
    {
        Volume = Math.Clamp(Volume, 0, 100);
    }
}

internal sealed class DebugInfoConfiguration
{
    public bool Enabled { get; set; } = true;
    public bool RoomId { get; set; } = true;

    public static DebugInfoConfiguration MakeDefaults() => new();

    public void Initialize()
    {
    }
}

internal sealed class GameConfiguration : IInitializable
{
    public static GameConfiguration MakeDefaults()
    {
        var def = new GameConfiguration();
        def.Initialize();
        return def;
    }

    public int Version { get; set; } = 1;
    public InputConfiguration Input { get; set; }
    public GameEnhancements Enhancements { get; set; }
    public AudioConfiguration Audio { get; set; }
    public DebugInfoConfiguration DebugInfo { get; set; }

    public void Save()
    {
        SaveFolder.SaveConfiguration(this);
    }

    public void Initialize()
    {
        Input ??= InputConfiguration.MakeDefaults();
        Enhancements ??= GameEnhancements.MakeDefaults();
        Audio ??= AudioConfiguration.MakeDefaults();
        DebugInfo ??= DebugInfoConfiguration.MakeDefaults();

        Enhancements.Initialize();
        Audio.Initialize();
        DebugInfo.Initialize();
    }
}

[Flags]
internal enum KeyboardModifiers
{
    None,
    Shift = 1,
    Control = 2,
    Alt = 4,
}

internal readonly record struct KeyboardMapping(Key Key, KeyboardModifiers Modifiers = KeyboardModifiers.None)
{
    public bool HasModifiers => Modifiers != KeyboardModifiers.None;
}

internal sealed class InputConfiguration
{
    public static InputConfiguration MakeDefaults() => new(_defaultKeyboardMap, _defaultGamepadMap);

    private static readonly KeyboardMap _defaultKeyboardMap = new Dictionary<KeyboardMapping, GameButton>
    {
        { new KeyboardMapping(Key.Z), GameButton.B },
        { new KeyboardMapping(Key.X), GameButton.A },
        { new KeyboardMapping(Key.LeftBracket), GameButton.ItemPrevious },
        { new KeyboardMapping(Key.RightBracket), GameButton.ItemNext },
        { new KeyboardMapping(Key.Q), GameButton.Select },
        { new KeyboardMapping(Key.Space), GameButton.Start },
        { new KeyboardMapping(Key.Up), GameButton.Up },
        { new KeyboardMapping(Key.Down), GameButton.Down },
        { new KeyboardMapping(Key.Left), GameButton.Left },
        { new KeyboardMapping(Key.Right), GameButton.Right },

        { new KeyboardMapping(Key.Enter, KeyboardModifiers.Alt), GameButton.FullScreenToggle },
        { new KeyboardMapping(Key.F, KeyboardModifiers.Control), GameButton.FullScreenToggle },
        { new KeyboardMapping(Key.M, KeyboardModifiers.Control), GameButton.AudioMuteToggle },

        { new KeyboardMapping(Key.Pause), GameButton.Pause },
        { new KeyboardMapping(Key.Minus), GameButton.AudioDecreaseVolume },
        { new KeyboardMapping(Key.Equal), GameButton.AudioIncreaseVolume },
        { new KeyboardMapping(Key.Equal, KeyboardModifiers.Shift), GameButton.AudioIncreaseVolume },

#if DEBUG
        { new KeyboardMapping(Key.X, KeyboardModifiers.Control | KeyboardModifiers.Alt), GameButton.CheatKillAll },
        { new KeyboardMapping(Key.S, KeyboardModifiers.Control | KeyboardModifiers.Alt), GameButton.CheatSpeedUp },
        { new KeyboardMapping(Key.C, KeyboardModifiers.Control | KeyboardModifiers.Alt), GameButton.CheatClip },
        { new KeyboardMapping(Key.B, KeyboardModifiers.Control | KeyboardModifiers.Alt), GameButton.CheatBeHoldClock },
        { new KeyboardMapping(Key.F, KeyboardModifiers.Control | KeyboardModifiers.Alt), GameButton.CheatFullHealth },
        { new KeyboardMapping(Key.Q, KeyboardModifiers.Control | KeyboardModifiers.Alt), GameButton.CheatGodMode },
        { new KeyboardMapping(Key.D, KeyboardModifiers.Control | KeyboardModifiers.Alt), GameButton.ToggleDebugInfo },
#endif
    };

    private static readonly GamepadMap _defaultGamepadMap = new Dictionary<GamepadButton, GameButton>
    {
        { GamepadButton.Start, GameButton.Start },
        { GamepadButton.Back, GameButton.Select },
        { GamepadButton.B, GameButton.A }, // A/B are spatially B/A on NES.
        { GamepadButton.A, GameButton.B },
        { GamepadButton.DPadLeft, GameButton.Left },
        { GamepadButton.DPadRight, GameButton.Right },
        { GamepadButton.DPadDown, GameButton.Down },
        { GamepadButton.DPadUp, GameButton.Up },
        { GamepadButton.Home, GameButton.None },
        { GamepadButton.BumperLeft, GameButton.ItemPrevious },
        { GamepadButton.BumperRight, GameButton.ItemNext },
        { GamepadButton.StickLeft, GameButton.None },
        { GamepadButton.StickRight, GameButton.None },

#if DEBUG
        { GamepadButton.X, GameButton.CheatFullHealth },
        { GamepadButton.Y, GameButton.CheatClip },
        { GamepadButton.TriggerLeft, GameButton.CheatKillAll },
        { GamepadButton.TriggerRight, GameButton.CheatSpeedUp },
#endif
    };

    [JsonIgnore]
    public KeyboardMap Keyboard => _keyboard ?? _defaultKeyboardMap;

    [JsonIgnore]
    public GamepadMap Gamepad => _gamepad ?? _defaultGamepadMap;

    [JsonPropertyName("Keyboard")]
    private readonly KeyboardMap? _keyboard;

    [JsonPropertyName("Gamepad")]
    private readonly GamepadMap? _gamepad;

    public InputConfiguration(KeyboardMap keyboard, GamepadMap gamepad)
    {
        _keyboard = keyboard;
        _gamepad = gamepad;
    }
}