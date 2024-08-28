using System.Diagnostics;
using System.Windows.Forms;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.OpenGL.Extensions.ImGui;
using SkiaSharp;
using z1.IO;
using z1.UI;
using Button = Silk.NET.Input.Button;
using GamepadButton = z1.UI.GamepadButton;

namespace z1.GUI;

internal sealed class GLWindow : IDisposable
{
    private const float AnalogThreshold = .8f;

    private static readonly DebugLog _log = new(nameof(GLWindow));

    public readonly Game Game;

    public bool IsFullScreen => _window?.WindowBorder == WindowBorder.Hidden;

    private readonly IWindow? _window;
    private readonly FpsCalculator _framesPerSecond = new();
    private readonly FpsCalculator _updatesPerSecond = new();
    private readonly FpsCalculator _rendersPerSecond = new();

    private GL? _gl;
    private IInputContext? _inputContext;

    private GRGlInterface? _glinterface;
    private GRContext? _grcontext;
    private SKSurface? _surface;
    private GRBackendRenderTarget? _rendertarget;
    private ImGuiController _controller;
    private System.Drawing.Rectangle _windowedRect;
    private bool _showMenu = false;
    private bool _lastKeyWasAlt = false;

    public GLWindow()
    {
        try
        {
            Asset.Initialize();
        }
        catch (Exception e)
        {
            _log.Error("Error initializing assets: " + e);
            MessageBox.Show(e.ToString());
            Environment.Exit(1);
        }

        Game = new Game();

        var options = WindowOptions.Default with
        {
            FramesPerSecond = 60,
            UpdatesPerSecond = 60,
            Size = new Vector2D<int>(1200, 1100),
            Title = "The Legend of Form1"
        };
        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.FramebufferResize += OnFramebufferResize;
        _window.Render += Render;
        _window.Closing += OnClosing;
        _window.FocusChanged += OnFocusChanged;
        _window.Initialize();
        _window.SetWindowIcon([EmbeddedResource.RawImageIconFromResource("icon.ico")]);
        _window.Run();
        _windowedRect = _window.GetRect();
    }

    private void OnLoad()
    {
        var window = _window ?? throw new Exception();

        _gl = window.CreateOpenGL();
        _inputContext = window.CreateInput();

        _glinterface = GRGlInterface.Create() ?? throw new Exception("GRGlInterface.Create() failed.");
        _grcontext = GRContext.CreateGl(_glinterface) ?? throw new Exception("GRContext.CreateGl() failed.");

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

        var surface = CreateSkSurface();
        Game.UpdateScreenSize(surface);

        var fontPath = IncludedResources.GetFont();
        var font = fontPath == null ? null : (ImGuiFontConfig?)new ImGuiFontConfig(fontPath, 30);
        _controller = new ImGuiController(_gl, window, _inputContext, font);
    }

    private void OnFramebufferResize(Vector2D<int> s)
    {
        var gl = _gl ?? throw new Exception();

        gl.Viewport(s);

        var surface = CreateSkSurface();
        Game.UpdateScreenSize(surface);
    }

    private SKSurface CreateSkSurface()
    {
        var gl = _gl ?? throw new Exception();
        var window = _window ?? throw new Exception();

        _surface?.Dispose();
        _rendertarget?.Dispose();

        var framebuffer = gl.GetInteger(GLEnum.FramebufferBinding);

        var framebufferinfo = new GRGlFramebufferInfo((uint)framebuffer, SKColorType.Rgba8888.ToGlSizedFormat());
        _rendertarget = new GRBackendRenderTarget(window.Size.X, window.Size.Y, 0, 8, framebufferinfo);
        return _surface = SKSurface.Create(_grcontext, _rendertarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
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

        Game.Input.UnsetKey(GetKeyMapping(kb, key));
    }
    #endregion

    private readonly Stopwatch _starttime = Stopwatch.StartNew();
    private readonly Stopwatch _updateTimer = new Stopwatch();
    private TimeSpan _renderedTime = TimeSpan.Zero;

    private void Render(double deltaSeconds)
    {
        var surface = _surface ?? throw new Exception();
        var window = _window ?? throw new Exception();

        surface.Canvas.Clear(SKColors.Black);

        _controller.Update((float)deltaSeconds);

        var updated = false;
        var frameTime = TimeSpan.FromSeconds(1 / 60d);

        var delta = TimeSpan.FromSeconds(deltaSeconds);

        Graphics.SetSurface(surface);

        double ups = 0;
        double rps = 0;

        // JOE: TODO: Port this over to `delta`
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
            surface.Canvas.Flush();
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
        _glinterface?.Dispose();
        _grcontext?.Dispose();
        _surface?.Dispose();
        _rendertarget?.Dispose();
        _window?.Dispose();
        _gl?.Dispose();
        _inputContext?.Dispose();
    }
}