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

    ToggleMhzDisaster,
    IncreaseMhzDisaster,
    DecreaseMhzDisaster,

    AudioMuteToggle,
    AudioIncreaseVolume,
    AudioDecreaseVolume,
}

internal enum FunctionButton
{
    BeginRecording,
    WriteRecording,
    WriteRecordingAssert,
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
    public readonly HashSet<GameButton> Buttons = new();
    public readonly HashSet<char> Characters = new();
    public readonly HashSet<FunctionButton> Functions = new();

    public InputButtons() { }

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
        other.Functions.Clear();
        other.Functions.UnionWith(Functions);
    }
}

internal sealed class Input
{
    public delegate void OnKeyPressedDelegate(KeyboardMapping mapping);

    private static readonly DebugLog _traceLog = new(nameof(Input), DebugLogDestination.None);

    public event OnKeyPressedDelegate OnKeyPressed;

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

    public HashSet<GameButton> GetButtonsUnsafe()
    {
        // JOE: This is a massive simplification of the C++ code. I may have borked something?
        return _inputState.Buttons;
    }

    private bool SetGameButton<TKey>(IReadOnlyDictionary<TKey, GameButton> map, TKey key)
        where TKey : notnull
    {
        return SetGameButton(map, _inputState.Buttons, key);
    }

    private bool SetGameButton<TKey, TButton>(IReadOnlyDictionary<TKey, TButton> map, HashSet<TButton> buttons, TKey key)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var button)) return false;
        var didSet = buttons.Add(button);
        _traceLog.Write($"{nameof(SetGameButton)} button:{button} didSet:{didSet}");
        return true;
    }

    private bool UnsetGameButton<TKey, TButton>(IReadOnlyDictionary<TKey, TButton> map, HashSet<TButton> buttons, TKey key)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var button)) return false;
        var didRemove = buttons.Remove(button);
        _traceLog.Write($"{nameof(UnsetGameButton)} button:{button} didRemove:{didRemove}");
        return true;
    }

    public bool SetKey(KeyboardMapping map)
    {
        // Always perform the callback, regardless of if it's a game input or not. Perhaps down the road some callers
        // will want an argument letting them know, or setup a second binding.
        OnKeyPressed?.Invoke(map);

        if (SetGameButton(_configuration.Keyboard, _inputState.Buttons, map)) return true;
        if (SetGameButton(_configuration.Functions, _inputState.Functions, map)) return true;
        SetLetter(map.Key.GetKeyCharacter());

        return false;
    }

    public bool UnsetKey(KeyboardMapping input)
    {
        var found = false;

        // TODO: Fix copy pasta.
        foreach (var (test, button) in _configuration.Keyboard)
        {
            if (test.ShouldUnset(input))
            {
                var didRemove = _inputState.Remove(button);
                _traceLog.Write($"{nameof(UnsetKey)} button:{button} didRemove:{didRemove}");
                found = true;
            }
        }

        foreach (var (test, button) in _configuration.Functions)
        {
            if (test.ShouldUnset(input))
            {
                var didRemove = _inputState.Functions.Remove(button);
                _traceLog.Write($"{nameof(UnsetKey)} button:{button} didRemove:{didRemove}");
                found = true;
            }
        }

        // Always unset the character, as it's always being released regardless of being a game input.
        UnsetLetter(input.Key.GetKeyCharacter());
        return found;
    }

    public bool SetGamepadButton(ButtonName button) => SetGameButton(_configuration.Gamepad, _inputState.Buttons, (GamepadButton)button);
    public bool UnsetGamepadButton(ButtonName button) => UnsetGameButton(_configuration.Gamepad, _inputState.Buttons, (GamepadButton)button);

    public bool SetGamepadButton(GamepadButton button) => SetGameButton(_configuration.Gamepad, _inputState.Buttons, button);
    public bool UnsetGamepadButton(GamepadButton button) => UnsetGameButton(_configuration.Gamepad, _inputState.Buttons, button);

    public bool ToggleGamepadButton(GamepadButton button, bool set) => set ? SetGamepadButton(button) : UnsetGamepadButton(button);

    private bool SetLetter(char letter)
    {
        if (GameString.ByteFromChar(letter) == 0) return false;
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

    public bool SetFunction(FunctionButton function) => _inputState.Functions.Add(function);

    public bool IsFunctionPressing(FunctionButton function) => GetFunctionButton(function) == ButtonState.Pressing;

    private ButtonState GetButton(GameButton button)
    {
        return GetButtonState(_inputState.Buttons, _oldInputState.Buttons, button);
    }

    private ButtonState GetFunctionButton(FunctionButton button)
    {
        return GetButtonState(_inputState.Functions, _oldInputState.Functions, button);
    }

    private static ButtonState GetButtonState<T>(HashSet<T> current, HashSet<T> old, T button)
    {
        var isDown = current.Contains(button);
        var wasDown = old.Contains(button);

        return (isDown, wasDown) switch
        {
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