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

internal static class Input
{
    private static InputButtons oldInputState;
    private static InputButtons inputState;

    public static InputButtons GetButtons()
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

    public static bool SetKey(Keys keys)
    {
        if (_map.TryGetValue(keys, out var button))
        {
            oldInputState = new InputButtons { Buttons = inputState.Buttons };
            inputState.Buttons |= button;
            return true;
        }

        return false;
    }

    public static void UnsetKey(Keys keys)
    {
        if (_map.TryGetValue(keys, out var button))
        {
            oldInputState = new InputButtons { Buttons = inputState.Buttons };
            inputState.Buttons &= ~button;
        }
    }

    public static bool IsKeyDown(int keyCode) => throw new NotImplementedException();
    public static bool IsKeyPressing(int keyCode) => throw new NotImplementedException();
    public static ButtonState GetKey(int keyCode) => throw new NotImplementedException();

    public static bool IsButtonDown(Button buttonCode) => inputState.Has(buttonCode);
    public static bool IsButtonPressing(Button buttonCode) => GetButton(buttonCode) == ButtonState.Pressing;
    // public static bool IsButtonPressing(Button buttonCode) => IsButtonDown(buttonCode);

    public static ButtonState GetButton(Button buttonCode)
    {
        var isDown = inputState.Has(buttonCode) ? 1 : 0;
        var wasDown = oldInputState.Has(buttonCode) ? 1 : 0;

        return (ButtonState)((wasDown << 1) | isDown);
    }

    public static Direction GetInputDirection()
    {
        if (IsButtonDown(Button.Left)) return Direction.Left;
        if (IsButtonDown(Button.Right)) return Direction.Right;
        if (IsButtonDown(Button.Up)) return Direction.Up;
        if (IsButtonDown(Button.Down)) return Direction.Down;
        return Direction.None;
    }

    public static void Update()
    {
    }
}