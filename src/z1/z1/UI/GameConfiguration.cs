using System.Text.Json.Serialization;
using Silk.NET.Input;
using KeyboardMap = System.Collections.Generic.IReadOnlyDictionary<Silk.NET.Input.Key, z1.GameButton>;
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

internal sealed class GameConfiguration
{
    public static GameConfiguration MakeDefaults() => new();

    [JsonIgnore]
    public InputConfiguration Input => _input ?? InputConfiguration.MakeDefaults();

    public int Version { get; set; } = 1;
    public bool EnableEnhancements { get; set; } = true;
    public bool EnableAudio { get; set; } = true;
    public bool? IsJoe { get; set; }
    public int AudioVolume { get; set; } = 100;

    [JsonPropertyName("Input")]
    private readonly InputConfiguration? _input;
}

internal sealed class InputConfiguration
{
    public static InputConfiguration MakeDefaults() => new(_defaultKeyboardMap, _defaultGamepadMap);

    private static readonly KeyboardMap _defaultKeyboardMap = new Dictionary<Key, GameButton>
    {
        { Key.Z, GameButton.B },
        { Key.X, GameButton.A },
        { Key.A, GameButton.PreviousItem },
        { Key.S, GameButton.NextItem },
        { Key.Q, GameButton.Select },
        { Key.Space, GameButton.Start },
        { Key.Up, GameButton.Up },
        { Key.Down, GameButton.Down },
        { Key.Left, GameButton.Left },
        { Key.Right, GameButton.Right },
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
        { GamepadButton.BumperLeft, GameButton.PreviousItem },
        { GamepadButton.BumperRight, GameButton.NextItem },
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