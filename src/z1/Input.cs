using System.Text.Json.Serialization;
using Silk.NET.Input;
using z1.IO;
using z1.UI;

namespace z1;

internal enum ButtonState
{
    Lifted = 0,
    Pressing = 1,
    Releasing = 2,
    Held = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum GameButton
{
    None,
    Right,
    Left,
    Down,
    Up,
    Start,
    Select,
    Pause,
    B,
    A,

    ItemPrevious,
    ItemNext,

    ItemBoomerang,
    ItemBombs,
    ItemArrow,
    ItemCandle,
    ItemRecorder,
    ItemFood,
    ItemLetter,
    ItemRod,

    FullScreenToggle,

    CheatKillAll,
    CheatSpeedUp,
    CheatBeHoldClock,
    CheatFullHealth,
    CheatGodMode,
    CheatClip,
    ToggleDebugInfo,

    AudioMuteToggle,
    AudioIncreaseVolume,
    AudioDecreaseVolume,
}

internal static class InputButtonsExtensions
{
    public static Direction GetDirection(this HashSet<GameButton> buttons)
    {
        var direction = Direction.None;
        if (buttons.Contains(GameButton.Up)) direction |= Direction.Up;
        if (buttons.Contains(GameButton.Down)) direction |= Direction.Down;
        if (buttons.Contains(GameButton.Left)) direction |= Direction.Left;
        if (buttons.Contains(GameButton.Right)) direction |= Direction.Right;
        return direction;
    }

    public static void Mask(this HashSet<GameButton> buttons, GameButton value)
    {
        var had = buttons.Contains(value);
        buttons.Clear();
        if (had) buttons.Add(value);
    }
}

internal readonly struct InputButtons
{
    public readonly HashSet<GameButton> Buttons;
    public readonly HashSet<char> Characters;

    public InputButtons()
    {
        Buttons = new HashSet<GameButton>();
        Characters = new HashSet<char>();
    }

    public bool Set(GameButton value) => Buttons.Add(value);
    public bool Has(GameButton value) => Buttons.Contains(value);
    public bool Remove(GameButton value) => Buttons.Remove(value);
    public void ClearButtons() => Buttons.Clear();
    public void ClearAll()
    {
        Buttons.Clear();
        Characters.Clear();
    }

    public void CloneTo(InputButtons other)
    {
        other.Buttons.Clear();
        other.Buttons.UnionWith(Buttons);
        other.Characters.Clear();
        other.Characters.UnionWith(Characters);
    }
}

internal sealed class Input
{
    private static readonly DebugLog _traceLog = new(nameof(Input), DebugLogDestination.None);

    private readonly InputButtons _oldInputState = new();
    private readonly InputButtons _inputState = new();

    private readonly InputConfiguration _configuration;

    public Input(InputConfiguration configuration)
    {
        _configuration = configuration;
    }

    public HashSet<GameButton> GetButtons()
    {
        // JOE: This is a massive simplification of the C++ code. I may have borked something?
        return new HashSet<GameButton>(_inputState.Buttons);
    }

    private bool SetGameButton<TKey>(IReadOnlyDictionary<TKey, GameButton> map, TKey key)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var button)) return false;
        var didSet = _inputState.Set(button);
        _traceLog.Write($"{nameof(SetGameButton)} button:{button} didSet:{didSet}");
        return true;
    }

    private bool UnsetGameButton<TKey>(IReadOnlyDictionary<TKey, GameButton> map, TKey key)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var button)) return false;
        var didRemove = _inputState.Remove(button);
        _traceLog.Write($"{nameof(UnsetGameButton)} button:{button} didRemove:{didRemove}");
        return true;
    }

    public bool SetKey(KeyboardMapping map)
    {
        if (SetGameButton(_configuration.Keyboard, map)) return true;
        SetLetter(map.Key.GetKeyCharacter());
        return false;
    }

    public bool UnsetKey(KeyboardMapping map)
    {
        // We need to clear any game input that's using this key. IE, if mute is set to control+m,
        // then the user releases ctrl, then the user releases m, we're not seeing "control+m", we're
        // seeing each action.
        var found = false;
        foreach (var kv in _configuration.Keyboard)
        {
            if (kv.Key.Key == map.Key || (kv.Key.HasModifiers && kv.Key.Modifiers.HasFlag(map.Modifiers)))
            {
                var didRemove = _inputState.Remove(kv.Value);
                _traceLog.Write($"{nameof(UnsetKey)} button:{kv.Value} didRemove:{didRemove}");
                found = true;
            }
        }

        // Always unset the character, as it's always being released regardless of being a game input.
        UnsetLetter(map.Key.GetKeyCharacter());
        return found;
    }

    public bool SetGamepadButton(ButtonName button) => SetGameButton(_configuration.Gamepad, (GamepadButton)button);
    public bool UnsetGamepadButton(ButtonName button) => UnsetGameButton(_configuration.Gamepad, (GamepadButton)button);

    public bool SetGamepadButton(GamepadButton button) => SetGameButton(_configuration.Gamepad, button);
    public bool UnsetGamepadButton(GamepadButton button) => UnsetGameButton(_configuration.Gamepad, button);

    public bool ToggleGamepadButton(GamepadButton button, bool set) => set ? SetGamepadButton(button) : UnsetGamepadButton(button);

    private bool SetLetter(char letter)
    {
        if (ZeldaString.ByteFromChar(letter) == 0) return false;
        _inputState.Characters.Add(letter);
        return true;
    }
    private void UnsetLetter(char letter) => _inputState.Characters.Remove(letter);

    public void UnsetAllInput()
    {
        _traceLog.Write($"{nameof(SetGameButton)} UnsetAllInput");
        _inputState.ClearAll();
    }

    public bool IsButtonDown(GameButton button) => _inputState.Has(button);
    public bool AreBothButtonsDown(GameButton a, GameButton b) => IsButtonDown(a) && IsButtonDown(b);
    public bool IsButtonPressing(GameButton button) => GetButton(button) == ButtonState.Pressing;
    public bool IsAnyButtonPressing(GameButton a, GameButton b) => IsButtonPressing(a) || IsButtonPressing(b);
    public IEnumerable<char> GetCharactersPressing()
    {
        foreach (var c in _inputState.Characters)
        {
            if (!_oldInputState.Characters.Contains(c))
            {
                yield return c;
            }
        }
    }

    private ButtonState GetButton(GameButton button)
    {
        var isDown = _inputState.Has(button);
        var wasDown = _oldInputState.Has(button);

        return (isDown, wasDown) switch {
            (false, false) => ButtonState.Lifted,
            (true, false) => ButtonState.Pressing,
            (false, true) => ButtonState.Releasing,
            (true, true) => ButtonState.Held,
        };
    }

    public Direction GetDirectionPressing()
    {
        if (IsButtonPressing(GameButton.Up)) return Direction.Up;
        if (IsButtonPressing(GameButton.Down)) return Direction.Down;
        if (IsButtonPressing(GameButton.Left)) return Direction.Left;
        if (IsButtonPressing(GameButton.Right)) return Direction.Right;
        return Direction.None;
    }

    public void Update()
    {
        _inputState.CloneTo(_oldInputState);
    }
}