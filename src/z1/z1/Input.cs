using Silk.NET.Input;

namespace z1;

internal enum ButtonState
{
    Lifted = 0,
    Pressing = 1,
    Releasing = 2,
    Held = 3,
}

[Flags]
internal enum Button
{
    None = 0,
    A = 0x80,
    B = 0x40,
    Select = 0x20,
    Start = 0x10,
    Up = 8,
    Down = 4,
    Left = 2,
    Right = 1,

    MovingMask = 0xF,
}

internal enum InputAxis { None, Horizontal, Vertical }

internal struct ButtonMapping
{
    public byte SrcCode;
    public byte DstCode;
    public string SettingName;
}

internal struct AxisMapping
{
    public byte Stick;
    public byte SrcAxis;
    public byte DstAxis;
    public string StickSettingName;
    public string AxisSettingName;
}

internal struct InputButtons
{
    public Button Buttons;
    public readonly int ButtonsInt => (int)Buttons;

    public HashSet<char> Characters;

    public InputButtons()
    {
        Buttons = Button.None;
        Characters = new();
    }

    public readonly bool Has(Button value) => Buttons.HasFlag(value);
    public void Mask(Button value) => Buttons &= value;
    public void Clear(Button value) => Buttons = (Button)((int)Buttons ^ (int)value);
}

internal sealed class Input
{
    private InputButtons oldInputState = new();
    private InputButtons inputState = new();

    public InputButtons GetButtons()
    {
        var buttons = (oldInputState.ButtonsInt ^ inputState.ButtonsInt)
            & inputState.ButtonsInt
            | (inputState.ButtonsInt & 0xF);

        return new InputButtons { Buttons = (Button)buttons };
    }

    private static readonly Dictionary<Key, Button> _map = new()
    {
        { Key.Z, Button.A },
        { Key.X, Button.B },
        { Key.Q, Button.Select },
        { Key.Space, Button.Start },
        { Key.Up, Button.Up },
        { Key.Down, Button.Down },
        { Key.Left, Button.Left },
        { Key.Right, Button.Right }
    };

    public bool SetKey(Key keys)
    {
        if (_map.TryGetValue(keys, out var button))
        {

            inputState.Buttons |= button;
            return true;
        }

        return false;
    }

    public bool SetLetter(char letter)
    {
        if (char.IsLetter(letter))
        {
            inputState.Characters.Add(letter);
            return true;
        }

        return false;
    }

    public bool UnsetKey(Key keys)
    {
        if (_map.TryGetValue(keys, out var button))
        {
            // oldInputState = new InputButtons { Buttons = inputState.Buttons };
            inputState.Buttons &= ~button;
            return true;
        }

        return false;
    }

    public void UnsetAllKeys()
    {
        inputState.Buttons = Button.None;
    }

    public void UnsetLetter(char letter)
    {
        inputState.Characters.Remove(letter);
    }

    public bool IsKeyDown(int keyCode) => throw new NotImplementedException();
    public bool IsKeyPressing(int keyCode) => throw new NotImplementedException();
    public ButtonState GetKey(int keyCode) => throw new NotImplementedException();

    public bool IsButtonDown(Button buttonCode) => inputState.Has(buttonCode);
    public bool IsButtonPressing(Button buttonCode) => GetButton(buttonCode) == ButtonState.Pressing;
    public IEnumerable<char> GetCharactersPressing()
    {
        foreach (var c in inputState.Characters)
        {
            if (!oldInputState.Characters.Contains(c))
            {
                yield return c;
            }
        }
    }

    private ButtonState GetButton(Button buttonCode)
    {
        var isDown = inputState.Has(buttonCode);
        var wasDown = oldInputState.Has(buttonCode);

        return (isDown, wasDown) switch {
            (false, false) => ButtonState.Lifted,
            (true, false) => ButtonState.Pressing,
            (false, true) => ButtonState.Releasing,
            (true, true) => ButtonState.Held,
        };
    }

    public Direction GetDirectionPressing()
    {
        if (IsButtonPressing(Button.Up)) return Direction.Up;
        if (IsButtonPressing(Button.Down)) return Direction.Down;
        if (IsButtonPressing(Button.Left)) return Direction.Left;
        if (IsButtonPressing(Button.Right)) return Direction.Right;
        return Direction.None;
    }

    public void Update()
    {
        oldInputState = new InputButtons {
            Buttons = inputState.Buttons,
            Characters = inputState.Characters == null ? new() : new(inputState.Characters)
        };
    }
}