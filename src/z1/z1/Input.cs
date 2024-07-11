namespace z1;

internal enum ButtonState
{
    Lifted = 0,
    Pressing = 1,
    Releasing = 2,
    Held = 3,
}

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
    Right = 1
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

internal class InputButtons(Button button)
{
    public Button Buttons = button;
    public int ButtonsInt => (int)Buttons;

    public bool Has(Button value) => (Buttons & value) != 0;
    public void Mask(int value) => Buttons &= (Button)value;
    public void Clear(Button value) => Buttons = (Button)((int)Buttons ^ (int)value);
}

internal static class Input
{
    private static InputButtons oldInputState = new(0);
    private static InputButtons inputState = new(0);

    public static InputButtons GetButtons()
    {
        var buttons = 0;

        buttons = (oldInputState.ButtonsInt ^ inputState.ButtonsInt)
            & inputState.ButtonsInt
            | (inputState.ButtonsInt & 0xF);

        return new((Button)buttons);
    }

    public static bool IsKeyDown(int keyCode) => throw new NotImplementedException();
    public static bool IsKeyPressing(int keyCode) => throw new NotImplementedException();
    public static ButtonState GetKey(int keyCode) => throw new NotImplementedException();

    public static bool IsButtonDown(Button buttonCode) => inputState.Has(buttonCode);
    public static bool IsButtonPressing(Button buttonCode) => GetButton(buttonCode) == ButtonState.Pressing;

    public static ButtonState GetButton(Button buttonCode)
    {
        int isDown = inputState.Has(buttonCode) ? 1 : 0;
        int wasDown = oldInputState.Has(buttonCode) ? 1 : 0;

        return (ButtonState)((wasDown << 1) | isDown);
    }

    public static Direction GetInputDirection()
    {
        if (IsButtonDown(Button.Left)) return Direction.Left;
        else if (IsButtonDown(Button.Right)) return Direction.Right;
        else if (IsButtonDown(Button.Up)) return Direction.Up;
        else if (IsButtonDown(Button.Down)) return Direction.Down;
        return Direction.None;
    }

    public static void Update() => throw new NotImplementedException();
}