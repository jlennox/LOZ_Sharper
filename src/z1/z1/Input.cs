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

    public readonly bool Has(Button value) => Buttons.HasFlag(value);
    public void Mask(Button value) => Buttons &= value;
    public void Clear(Button value) => Buttons = (Button)((int)Buttons ^ (int)value);
}

internal sealed class Input
{
    private InputButtons oldInputState;
    private InputButtons inputState;

    public InputButtons GetButtons()
    {
        var buttons = (oldInputState.ButtonsInt ^ inputState.ButtonsInt)
            & inputState.ButtonsInt
            | (inputState.ButtonsInt & 0xF);

        return new() { Buttons = (Button)buttons };
    }

    private static readonly Dictionary<Keys, Button> _map = new()
    {
        { Keys.Z, Button.A },
        { Keys.X, Button.B },
        { Keys.Q, Button.Select },
        { Keys.Space, Button.Start },
        { Keys.Up, Button.Up },
        { Keys.Down, Button.Down },
        { Keys.Left, Button.Left },
        { Keys.Right, Button.Right }
    };

    public bool SetKey(Keys keys)
    {
        if (_map.TryGetValue(keys, out var button))
        {
            oldInputState = new InputButtons { Buttons = inputState.Buttons };
            inputState.Buttons |= button;
            return true;
        }

        return false;
    }

    public void UnsetKey(Keys keys)
    {
        if (_map.TryGetValue(keys, out var button))
        {
            oldInputState = new InputButtons { Buttons = inputState.Buttons };
            inputState.Buttons &= ~button;
        }
    }

    public bool IsKeyDown(int keyCode) => throw new NotImplementedException();
    public bool IsKeyPressing(int keyCode) => throw new NotImplementedException();
    public ButtonState GetKey(int keyCode) => throw new NotImplementedException();

    public bool IsButtonDown(Button buttonCode) => inputState.Has(buttonCode);
    public bool IsButtonPressing(Button buttonCode) => GetButton(buttonCode) == ButtonState.Pressing;

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

    public void Update()
    {
    }
}