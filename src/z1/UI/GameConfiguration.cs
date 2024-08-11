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

internal sealed class GameConfiguration : IInitializable
{
    public static GameConfiguration MakeDefaults() => new();

    [JsonIgnore]
    public InputConfiguration Input => _input ?? InputConfiguration.MakeDefaults();

    public int Version { get; set; } = 1;
    public bool EnableEnhancements { get; set; } = true;
    public bool MuteAudio { get; set; } = false;
    public bool? IsJoe { get; set; }
    public int TextSpeed { get; set; } = 1; // 1 is normal, 5 is max speed.
    public int AudioVolume { get; set; } = 80; // 0 to 100. Default to 80 because it sounds really loud at 100.

    [JsonPropertyName("Input")]
    private readonly InputConfiguration? _input;

    public void Save()
    {
        SaveFolder.SaveConfiguration(this);
    }

    public void Initialize()
    {
        TextSpeed = Math.Clamp(TextSpeed, 1, 5);
        AudioVolume = Math.Clamp(AudioVolume, 0, 100);
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
    };

    private static readonly GamepadMap _defaultGamepadMap = new Dictionary<GamepadButton, GameButton>
    {
        { GamepadButton.Start, GameButton.Start },
        { GamepadButton.Back, GameButton.Select },
        { GamepadButton.B, GameButton.A }, // A/B are spatially B/A on NES.
        { GamepadButton.A, GameButton.B },
        { GamepadButton.X, GameButton.None },
        { GamepadButton.Y, GameButton.None },
        { GamepadButton.DPadLeft, GameButton.Left },
        { GamepadButton.DPadRight, GameButton.Right },
        { GamepadButton.DPadDown, GameButton.Down },
        { GamepadButton.DPadUp, GameButton.Up },
        { GamepadButton.Home, GameButton.None },
        { GamepadButton.BumperLeft, GameButton.ItemPrevious },
        { GamepadButton.BumperRight, GameButton.ItemNext },
        { GamepadButton.StickLeft, GameButton.None },
        { GamepadButton.StickRight, GameButton.None },
        { GamepadButton.TriggerLeft, GameButton.CheatKillAll },
        { GamepadButton.TriggerRight, GameButton.CheatSpeedUp },
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