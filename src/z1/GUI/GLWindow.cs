using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using z1.IO;
using z1.Render;
using z1.UI;
using Button = Silk.NET.Input.Button;
using GamepadButton = z1.UI.GamepadButton;

namespace z1.GUI;

internal sealed class GLWindow : IDisposable
{
    private readonly bool _headless;
    private const float AnalogThreshold = .8f;

    private static readonly DebugLog _log = new(nameof(GLWindow));

    public Game Game = null!;

    public WaitHandle OnloadEvent => _onloadEvent.WaitHandle;
    private readonly ManualResetEventSlim _onloadEvent = new();

    public bool IsFullScreen => _window?.WindowBorder == WindowBorder.Hidden;

    private readonly IWindow? _window;
    private readonly FpsCalculator _framesPerSecond = new();
    private readonly FpsCalculator _updatesPerSecond = new();
    private readonly FpsCalculator _rendersPerSecond = new();

    public GL? _gl;
    private IInputContext? _inputContext;

    private ImGuiController _controller;
    private System.Drawing.Rectangle _windowedRect;
    private bool _showMenu = false;
    private bool _lastShowMenu = false;
    private bool _lastKeyWasAlt = false;

    public GLWindow(bool headless = false)
    {
        _headless = headless;
        try
        {
            Asset.Initialize();
        }
        catch (Exception e)
        {
            _log.Error("Error initializing assets: " + e);
            MessageBox.Show(e.ToString());
            throw;
            Environment.Exit(1);
        }

        var options = WindowOptions.Default with
        {
            FramesPerSecond = 60,
            UpdatesPerSecond = 60,
            Size = new Vector2D<int>(1150, 1050),
            Title = "The Legend of Form1"
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.FramebufferResize += OnFramebufferResize;
        _window.Render += Render;
        _window.Closing += OnClosing;
        _window.FocusChanged += OnFocusChanged;
        _window.Initialize();
        _window.SetWindowIcon([EmbeddedResource.GetWindowIcon()]);
        _windowedRect = _window.GetRect();

        if (!headless) _window.Run();
    }

    private void OnLoad()
    {
        var window = _window ?? throw new Exception();

        _gl = window.CreateOpenGL();
        _inputContext = window.CreateInput();

        _inputContext.ConnectionChanged += OnConnectionChanged;
        foreach (var targetkb in _inputContext.Keyboards)
        {
            BindKeyboard(targetkb);
        }

        var gamepad = _inputContext.Gamepads.FirstOrDefault();
        if (gamepad != null)
        {
            BindGamepad(gamepad);
        }

        Graphics.Initialize(_gl);
        Game = new Game(new GameIO());

        var fontConfig = new ImGuiFontConfig(StaticAssets.GuiFont, 30);
        _controller = new ImGuiController(_gl, window, _inputContext, fontConfig);

        UpdateViewport();

        _onloadEvent.Set();
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        UpdateViewport();
    }

    public void ToggleFullscreen()
    {
        var window = _window ?? throw new Exception();

        if (IsFullScreen)
        {
            var width = _windowedRect.Width;
            var height = _windowedRect.Height;
            if (width < 20) width = 400;
            if (height < 20) height = 400;
            window.WindowBorder = WindowBorder.Resizable;
            window.Size = new Vector2D<int>(width, height);
            window.Position = new Vector2D<int>(_windowedRect.X, _windowedRect.Y);
        }
        else
        {
            _windowedRect = window.GetRect();

            var screen = Screen.FromRectangle(window.GetRect()).Bounds;
            window.WindowBorder = WindowBorder.Hidden;
            window.Size = new Vector2D<int>(screen.Width, screen.Height);
            window.Position = new Vector2D<int>(screen.X, screen.Y);
        }
    }

    #region Input
    private void OnConnectionChanged(IInputDevice device, bool connected)
    {
        if (!connected)
        {
            _log.Write($"Input: Device disconnected {device.Name} ({device.GetType().Name})");
            return;
        }

        switch (device)
        {
            case IKeyboard kb: BindKeyboard(kb); break;
            case IGamepad gamepad: BindGamepad(gamepad); break;
            default:
                _log.Write($"Input: Unsupported device connected {device.Name} ({device.GetType().Name})");
                break;
        }
    }

    private void BindKeyboard(IKeyboard kb)
    {
        _log.Write($"Input: Binding keyboard {kb.Name}");
        kb.KeyDown += OnKeyDown;
        kb.KeyUp += OnKeyUp;
    }

    private void BindGamepad(IGamepad gamepad)
    {
        _log.Write($"Input: Binding gamepad {gamepad.Name}");
        gamepad.ButtonDown += OnGamepadButtonDown;
        gamepad.ButtonUp += OnGamepadButtonUp;
        gamepad.TriggerMoved += OnGamePadTriggerMoved;
        gamepad.ThumbstickMoved += OnGamePadThumbstickMoved;
    }

    private void OnGamepadButtonDown(IGamepad gamepad, Button button)
    {
        Game.Input.SetGamepadButton(button.Name);
    }

    private void OnGamepadButtonUp(IGamepad gamepad, Button button)
    {
        Game.Input.UnsetGamepadButton(button.Name);
    }

    private void OnGamePadTriggerMoved(IGamepad gamepad, Trigger trigger)
    {
        // They trigger when the program starts up at -1. They range from -1 to 1, passing 0 I presume in the middle.
        // At least, this is true on my xbox controller.

        var set = trigger.Position >= AnalogThreshold;
        switch (trigger.Index)
        {
            case 0: Game.Input.ToggleGamepadButton(GamepadButton.TriggerLeft, set); break;
            case 1: Game.Input.ToggleGamepadButton(GamepadButton.TriggerRight, set); break;
        }
    }

    private void OnGamePadThumbstickMoved(IGamepad gamepad, Thumbstick thumbstick)
    {
        if (thumbstick.Index is < 0 or > 1)
        {
            _log.Error($"Unknown stick index {thumbstick.Index}");
            return;
        }

        var (up, right, down, left) = thumbstick.Index switch
        {
            0 => (GamepadButton.StickLeftUp, GamepadButton.StickLeftRight, GamepadButton.StickLeftDown, GamepadButton.StickLeftLeft),
            1 => (GamepadButton.StickRightUp, GamepadButton.StickRightRight, GamepadButton.StickRightDown, GamepadButton.StickRightLeft),
            _ => throw new ArgumentOutOfRangeException()
        };

        switch (thumbstick.X)
        {
            case < -AnalogThreshold:
                Game.Input.ToggleGamepadButton(left, true);
                Game.Input.ToggleGamepadButton(right, false);
                break;
            case > AnalogThreshold:
                Game.Input.ToggleGamepadButton(left, false);
                Game.Input.ToggleGamepadButton(right, true);
                break;
            default:
                Game.Input.ToggleGamepadButton(left, false);
                Game.Input.ToggleGamepadButton(right, false);
                break;
        }

        switch (thumbstick.Y)
        {
            case < -AnalogThreshold:
                Game.Input.ToggleGamepadButton(up, true);
                Game.Input.ToggleGamepadButton(down, false);
                break;
            case > AnalogThreshold:
                Game.Input.ToggleGamepadButton(up, false);
                Game.Input.ToggleGamepadButton(down, true);
                break;
            default:
                Game.Input.ToggleGamepadButton(up, false);
                Game.Input.ToggleGamepadButton(down, false);
                break;
        }
    }

    private void OnFocusChanged(bool focused)
    {
        // This is to prevent keys from getting stuck due to the lack of focus causing an OnKeyUp event to be missed.
        if (!focused) Game.Input.UnsetAllInput();
    }

    private static KeyboardMapping GetKeyMapping(IKeyboard kb, Key key)
    {
        var isShiftPressed = kb.IsKeyPressed(Key.ShiftLeft) || kb.IsKeyPressed(Key.ShiftRight);
        var isAltPressed = kb.IsKeyPressed(Key.AltLeft) || kb.IsKeyPressed(Key.AltRight);
        var isCtrlPressed = kb.IsKeyPressed(Key.ControlLeft) || kb.IsKeyPressed(Key.ControlRight);

        var isShift = key is Key.ShiftLeft or Key.ShiftRight;
        var isAlt = key is Key.AltLeft or Key.AltRight;
        var isCtrl = key is Key.ControlLeft or Key.ControlRight;

        var shiftModifier = isShiftPressed || isShift ? KeyboardModifiers.Shift : KeyboardModifiers.None;
        var altModifier = isAltPressed || isAlt? KeyboardModifiers.Alt : KeyboardModifiers.None;
        var ctrlModifier = isCtrlPressed || isCtrl ? KeyboardModifiers.Control : KeyboardModifiers.None;

        return  new KeyboardMapping(key, shiftModifier | altModifier | ctrlModifier);
    }

    private void OnKeyDown(IKeyboard kb, Key key, int whoknows)
    {
        var mapping = GetKeyMapping(kb, key);

        Game.Input.SetKey(mapping);
        Game.GameCheats.OnKeyPressed(key);

        _lastKeyWasAlt = key is Key.AltLeft or Key.AltRight;

        if (Game.Input.IsButtonPressing(GameButton.FullScreenToggle)) ToggleFullscreen();
    }

    private void OnKeyUp(IKeyboard kb, Key key, int whoknows)
    {
        _showMenu = _lastKeyWasAlt && key is Key.AltLeft or Key.AltRight && !_showMenu;

        if (_showMenu && !_lastShowMenu)
        {
            GLWindowGui.Update();
            _lastShowMenu = _showMenu;
        }

        Game.Input.UnsetKey(GetKeyMapping(kb, key));
    }
    #endregion

    private readonly Stopwatch _starttime = Stopwatch.StartNew();
    private readonly Stopwatch _updateTimer = new Stopwatch();
    private TimeSpan _renderedTime = TimeSpan.Zero;

    private Rectangle _viewport = Rectangle.Empty;

    private void UpdateViewport()
    {
        var window = _window ?? throw new Exception();

        const float nesWidth = 256f;
        const float nesHeight = 240f;

        // Annoyingly, it's possible for sprite coordinates to land on pixel boundaries,
        // which causes it to incorrectly round down for one, then round up on the next.
        // Even though they'll sum to the correct width, individually one will be a pixel
        // to short and the next will contain a pixel row/column from the adjacent sprite.
        const int multiple = 1;
        var windowSize = window.Size;
        var windowWidth = windowSize.X;
        var windowHeight = windowSize.Y;

        if (windowWidth == 0 || windowHeight == 0) return;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Clamp(int i) => i - (i % multiple);

        var scale = Math.Min(windowWidth / nesWidth, windowHeight / nesHeight);
        // This math appears wrong? "- scale"

        var newWidth = (int)(nesWidth * scale);
        var newHeight = (int)(nesHeight * scale);

        var offsetX = (windowWidth - newWidth) / 2;
        var offsetY = (windowHeight - newHeight) / 2;

        _viewport = new Rectangle(
            Clamp(offsetX) + (offsetX % multiple) / 2,
            Clamp(offsetY) + (offsetY % multiple) / 2,
            Clamp(newWidth), Clamp(newHeight));

        Graphics.SetWindowSize(windowSize.X, windowSize.Y);
    }

    private void Render(double deltaSeconds)
    {
        if (_headless) return;

        var gl = _gl ?? throw new Exception();
        var window = _window ?? throw new Exception();

        Graphics.StartRender();
        gl.Viewport(_viewport.X, _viewport.Y, (uint)_viewport.Width, (uint)_viewport.Height);

        _controller.Update((float)deltaSeconds);

        var updated = false;
        var frameTime = TimeSpan.FromSeconds(1 / 60d);

        var delta = TimeSpan.FromSeconds(deltaSeconds);

        double ups = 0;
        double rps = 0;

        // JOE: TODO: Port this over to `delta`
        // JOE: TODO: MAke sure this updates() at the fastest of 60fps.
        // while (_starttime.Elapsed - _renderedTime >= frameTime)
        {
            _updateTimer.Restart();
            Game.Update();
            _updateTimer.Stop();
            ups = _updatesPerSecond.Add(_updateTimer.ElapsedMilliseconds / 1000.0f);

            _renderedTime += frameTime;
            updated = true;
        }

        if (updated)
        {
            _updateTimer.Restart();
            Game.Draw();
            _updateTimer.Stop();
            rps = _rendersPerSecond.Add(_updateTimer.ElapsedMilliseconds / 1000.0f);
        }

        if (Game.FrameCounter % 20 == 0)
        {
            var fps = _framesPerSecond.Add(deltaSeconds);
            window.Title = $"The Legend of Form1 - FPS:{fps:0.0}/UPS:{ups:0.0}/RPS:{rps:0.0}";
        }

        if (_showMenu)
        {
            gl.Viewport(0, 0, (uint)window.Size.X, (uint)window.Size.Y);
            GLWindowGui.DrawMenu(this);
            _controller.Render();
        }
    }

    public void OnClosing()
    {
        Environment.Exit(0);
    }

    public void Dispose()
    {
        _window?.Dispose();
        _gl?.Dispose();
        _inputContext?.Dispose();
    }
}