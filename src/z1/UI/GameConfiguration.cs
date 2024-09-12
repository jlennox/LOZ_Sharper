using System.Text.Json;
using System.Text.Json.Serialization;
using Silk.NET.Input;
using z1.IO;
using KeyboardMap = System.Collections.Generic.Dictionary<z1.UI.KeyboardMapping, z1.GameButton>;
using GamepadMap = System.Collections.Generic.Dictionary<z1.UI.GamepadButton, z1.GameButton>;

namespace z1.UI;

// This is a pretty close clone of Silk.NET.Input.ButtonName. This is because the trigger's were not
// in the original enum. I left space after the last enum in-case they expand theirs in the future.
[JsonConverter(typeof(JsonStringEnumConverter))]
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
    StickLeftButton = 9,
    StickRightButton = 10,
    DPadUp = 11,
    DPadRight = 12,
    DPadDown = 13,
    DPadLeft = 14,

    // The numeric values of the earlier entries must remain the same to map with Silk.NET.Input.ButtonName.
    TriggerLeft = 100,
    TriggerRight,
    StickLeftUp,
    StickLeftRight,
    StickLeftDown,
    StickLeftLeft,
    StickRightUp,
    StickRightRight,
    StickRightDown,
    StickRightLeft,
}

internal sealed class GameEnhancements
{
    public const int TextSpeedMin = 1;
    public const int TextSpeedMax = 5;

    public bool AutoSave { get; set; }
    public bool RedCandleLightsDarkRooms { get; set; }
    public bool ImprovedMenus { get; set; }
    public bool EasySaveMenu { get; set; }
    public bool ReduceFlashing { get; set; }
    public int TextSpeed { get; set; } // 1 is normal, 5 is max speed.

    public static GameEnhancements MakeDefaults() => new()
    {
        AutoSave = true,
        RedCandleLightsDarkRooms = false,
        ImprovedMenus = true,
        EasySaveMenu = true,
        TextSpeed = 1,
    };

    public void Initialize()
    {
        TextSpeed = Math.Clamp(TextSpeed, TextSpeedMin, TextSpeedMax);
    }
}

internal sealed class AudioConfiguration
{
    public bool Mute { get; set; }
    public bool MuteMusic { get; set; }
    public int Volume { get; set; } = 80; // 0 to 100. Default to 80 because it sounds really loud at 100.

    public static AudioConfiguration MakeDefaults() => new();

    public void Initialize()
    {
        Volume = Math.Clamp(Volume, 0, 100);
    }
}

internal sealed class DebugInfoConfiguration
{
    public bool Enabled { get; set; }
    public bool RoomId { get; set; }
    public bool ActiveShots { get; set; }

    public static DebugInfoConfiguration MakeDefaults() => new();

    public void Initialize()
    {
    }
}

internal sealed class VideoConfiguration
{
    public int Width { get; set; }
    public int Height { get; set; }

    public static VideoConfiguration MakeDefaults() => new();

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
    public VideoConfiguration Video { get; set; }
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
        Video ??= VideoConfiguration.MakeDefaults();
        DebugInfo ??= DebugInfoConfiguration.MakeDefaults();

        Input.Initialize();
        Enhancements.Initialize();
        Audio.Initialize();
        Video.Initialize();
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

[JsonConverter(typeof(Converter))]
internal readonly record struct KeyboardMapping(Key Key, KeyboardModifiers Modifiers = KeyboardModifiers.None)
{
    public bool HasModifiers => Modifiers != KeyboardModifiers.None;

    public class Converter : JsonConverter<KeyboardMapping>
    {
        private static string GetJsonString(KeyboardMapping map)
        {
            return map.Modifiers == KeyboardModifiers.None
                ? map.Key.ToString()
                : $"{map.Key}+{map.Modifiers}";
        }

        private static KeyboardMapping ParseJsonString(string json)
        {
            var parts = json.IndexOf('+', 1);
            var key = Enum.Parse<Key>(parts == -1 ? json : json[..parts]);
            var modifiers = parts == -1
                ? KeyboardModifiers.None
                : Enum.Parse<KeyboardModifiers>(json[(parts + 1)..]);

            return new KeyboardMapping(key, modifiers);
        }

        public override KeyboardMapping Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return ParseJsonString(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, KeyboardMapping value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(GetJsonString(value));
        }

        public override KeyboardMapping ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return ParseJsonString(reader.GetString());
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, KeyboardMapping value, JsonSerializerOptions options)
        {
            writer.WritePropertyName(GetJsonString(value));
        }
    }
}

internal sealed class InputConfiguration
{
    public static InputConfiguration MakeDefaults() => new();

    private static readonly KeyboardMap _defaultKeyboardMap = new()
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

        { new KeyboardMapping(Key.Number1), GameButton.ItemBoomerang },
        { new KeyboardMapping(Key.Number2), GameButton.ItemBombs },
        { new KeyboardMapping(Key.Number3), GameButton.ItemArrow },
        { new KeyboardMapping(Key.Number4), GameButton.ItemCandle },
        { new KeyboardMapping(Key.Number5), GameButton.ItemRecorder },
        { new KeyboardMapping(Key.Number6), GameButton.ItemFood },
        { new KeyboardMapping(Key.Number7), GameButton.ItemLetter },
        { new KeyboardMapping(Key.Number8), GameButton.ItemRod },

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

    private static readonly GamepadMap _defaultGamepadMap = new()
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
        { GamepadButton.StickLeftUp, GameButton.None },
        { GamepadButton.StickLeftRight, GameButton.None },
        { GamepadButton.StickLeftDown, GameButton.None },
        { GamepadButton.StickLeftLeft, GameButton.None },
        { GamepadButton.StickRightUp, GameButton.None },
        { GamepadButton.StickRightRight, GameButton.None },
        { GamepadButton.StickRightDown, GameButton.None },
        { GamepadButton.StickRightLeft, GameButton.None },

#if DEBUG
        { GamepadButton.StickLeftButton, GameButton.CheatBeHoldClock },
        { GamepadButton.StickRightButton, GameButton.None },
        { GamepadButton.X, GameButton.CheatFullHealth },
        { GamepadButton.Y, GameButton.CheatClip },
        { GamepadButton.TriggerLeft, GameButton.CheatKillAll },
        { GamepadButton.TriggerRight, GameButton.CheatSpeedUp },
#else
        { GamepadButton.StickLeftButton, GameButton.None },
        { GamepadButton.StickRightButton, GameButton.None },
        { GamepadButton.X, GameButton.None },
        { GamepadButton.Y, GameButton.None },
        { GamepadButton.TriggerLeft, GameButton.None },
        { GamepadButton.TriggerRight, GameButton.None },
#endif
    };

    public KeyboardMap Keyboard { get; set; }
    public GamepadMap Gamepad { get; set; }

    public InputConfiguration()
    {
    }

    public void Initialize()
    {
        Keyboard ??= new KeyboardMap(_defaultKeyboardMap);
        Gamepad ??= new GamepadMap(_defaultGamepadMap);
    }
}