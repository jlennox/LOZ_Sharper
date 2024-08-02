using Silk.NET.Input;
using z1.UI;

namespace z1;

internal enum ButtonState
{
    Lifted = 0,
    Pressing = 1,
    Releasing = 2,
    Held = 3,
}

[Flags]
internal enum GameButton
{
    None = 0,
    Right = 1 << 0,
    Left = 1 << 1,
    Down = 1 << 2,
    Up = 1 << 3,
    Start = 1 << 4,
    Select = 1 << 5,
    B = 1 << 6,
    A = 1 << 7,

    PreviousItem = 1 << 8,
    NextItem = 1 << 9,

    CheatKillAll = 1 << 20,
    CheatSpeedUp = 1 << 21,

    MovingMask = 0xF,
}

internal struct InputButtons
{
    public GameButton Buttons;
    public readonly int ButtonsInt => (int)Buttons;

    public HashSet<char> Characters;

    public InputButtons()
    {
        Buttons = GameButton.None;
        Characters = new();
    }

    public readonly bool Has(GameButton value) => Buttons.HasFlag(value);
    public void Mask(GameButton value) => Buttons &= value;
    public void Clear(GameButton value) => Buttons = (GameButton)((int)Buttons ^ (int)value);
}

internal sealed class Input
{
    private InputButtons _oldInputState = new();
    private InputButtons _inputState = new();

    private readonly InputConfiguration _configuration;

    public Input(InputConfiguration configuration)
    {
        _configuration = configuration;
    }

    public InputButtons GetButtons()
    {
        var buttons = (_oldInputState.ButtonsInt ^ _inputState.ButtonsInt)
            & _inputState.ButtonsInt
            | (_inputState.ButtonsInt & 0xF);

        return new InputButtons { Buttons = (GameButton)buttons };
    }

    private bool SetGameButton<TKey>(IReadOnlyDictionary<TKey, GameButton> map, TKey key)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var button)) return false;
        _inputState.Buttons |= button;
        return true;
    }

    private bool UnsetGameButton<TKey>(IReadOnlyDictionary<TKey, GameButton> map, TKey key)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var button)) return false;
        _inputState.Buttons &= ~button;
        return true;
    }

    public bool SetKey(Key key)
    {
        if (SetGameButton(_configuration.Keyboard, key)) return true;
        SetLetter(key.GetKeyCharacter());
        return false;
    }

    public bool UnsetKey(Key key)
    {
        if (UnsetGameButton(_configuration.Keyboard, key)) return true;
        UnsetLetter(key.GetKeyCharacter());
        return false;
    }

    public bool SetGamepadButton(ButtonName button) => SetGameButton(_configuration.Gamepad, (GamepadButton)button);
    public bool UnsetGamepadButton(ButtonName button) => UnsetGameButton(_configuration.Gamepad, (GamepadButton)button);

    public bool SetGamepadButton(GamepadButton button) => SetGameButton(_configuration.Gamepad, button);
    public bool UnsetGamepadButton(GamepadButton button) => UnsetGameButton(_configuration.Gamepad, button);

    public bool ToggleGamepadButton(GamepadButton button, bool set) => set ? SetGamepadButton(button) : UnsetGamepadButton(button);

    private bool SetLetter(char letter)
    {
        // JOE: TODO: Add ZeldaString.IsValid or what have you.
        if (!char.IsLetterOrDigit(letter)) return false;
        _inputState.Characters.Add(letter);
        return true;
    }

    private void UnsetLetter(char letter)
    {
        _inputState.Characters.Remove(letter);
    }

    public void UnsetAllKeys()
    {
        _inputState.Buttons = GameButton.None;
    }

    public bool IsButtonDown(GameButton buttonCode) => _inputState.Has(buttonCode);
    public bool IsButtonPressing(GameButton buttonCode) => GetButton(buttonCode) == ButtonState.Pressing;
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

    private ButtonState GetButton(GameButton buttonCode)
    {
        var isDown = _inputState.Has(buttonCode);
        var wasDown = _oldInputState.Has(buttonCode);

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
        _oldInputState = new InputButtons {
            Buttons = _inputState.Buttons,
            Characters = _inputState.Characters == null ? new() : new(_inputState.Characters)
        };
    }
}