using System.Diagnostics;
using System.Windows.Forms;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.OpenGL.Extensions.ImGui;
using SkiaSharp;
using z1.UI;
using Button = Silk.NET.Input.Button;

namespace z1.GUI;

internal sealed class GLWindow : IDisposable
{
    private static readonly DebugLog _log = new(nameof(GLWindow));

    private readonly Game _game;
    private readonly IWindow? _window;

    private GL? _gl;
    private IInputContext? _inputContext;

    private GRGlInterface? _glinterface;
    private GRContext? _grcontext;
    private SKSurface? _surface;
    private GRBackendRenderTarget? _rendertarget;
    private ImGuiController _controller;
    private System.Drawing.Rectangle _windowedRect;
    private string _initializationException;

    public GLWindow()
    {
        try
        {
            Asset.Initialize();
        }
        catch (Exception e)
        {
            _log.Write("Error initializing assets: " + e);
            _initializationException = e.ToString();
        }

        _game = new Game();

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

    private SKSurface CreateSkSurface()
    {
        var gl = _gl ?? throw new Exception();
        var window = _window ?? throw new Exception();

        _glinterface?.Dispose();
        _grcontext?.Dispose();
        _surface?.Dispose();
        _rendertarget?.Dispose();

        var framebuffer = gl.GetInteger(GLEnum.FramebufferBinding);

        _glinterface = GRGlInterface.Create();
        _grcontext = GRContext.CreateGl(_glinterface);

        var framebufferinfo = new GRGlFramebufferInfo((uint)framebuffer, SKColorType.Rgba8888.ToGlSizedFormat());
        _rendertarget = new GRBackendRenderTarget(window.Size.X, window.Size.Y, 0, 8, framebufferinfo);
        return _surface = SKSurface.Create(_grcontext, _rendertarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
    }

    private void ToggleFullscreen()
    {
        var window = _window ?? throw new Exception();

        var isFullscreen = window.WindowBorder == WindowBorder.Hidden;

        if (isFullscreen)
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

        var surface = CreateSkSurface();
        _game.UpdateScreenSize(surface);

        _controller = new ImGuiController(_gl, window, _inputContext);

        // ImGui.CreateContext();
        // ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        // ImGui.StyleColorsDark();
    }

    private void OnConnectionChanged(IInputDevice device, bool connected)
    {
        if (!connected)
        {
            _log.Write($"Input: Device disconnected {device.Name} ({device.GetType().Name})");
            return;
        }

        switch (device)
        {
            case IKeyboard kb:
                BindKeyboard(kb);
                break;
            case IGamepad gamepad:
                BindGamepad(gamepad);
                break;
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
    }

    private void OnGamepadButtonDown(IGamepad gamepad, Button button)
    {
        _game.Input.SetGamepadButton(button.Name);
    }

    private void OnGamepadButtonUp(IGamepad gamepad, Button button)
    {
        _game.Input.UnsetGamepadButton(button.Name);
    }

    private void OnGamePadTriggerMoved(IGamepad gamepad, Trigger trigger)
    {
        var set = Math.Abs(trigger.Position - 1) > .01;
        switch (trigger.Index)
        {
            case 0: _game.Input.ToggleGamepadButton(GamepadButton.TriggerLeft, set); break;
            case 1: _game.Input.ToggleGamepadButton(GamepadButton.TriggerRight, set); break;
        }
    }

    private void OnFocusChanged(bool focused)
    {
        // This is to prevent keys from getting stuck due to the lack of focus causing an OnKeyUp event to be missed.
        if (!focused) _game.Input.UnsetAllInput();
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

        _game.Input.SetKey(mapping);
        _game.GameCheats.OnKeyPressed(key);

        if (_game.Input.IsButtonDown(GameButton.FullScreenToggle))
        {
            ToggleFullscreen();
        }
    }

    private void OnKeyUp(IKeyboard kb, Key key, int whoknows)
    {
        _game.Input.UnsetKey(GetKeyMapping(kb, key));
    }

    private void OnFramebufferResize(Vector2D<int> s)
    {
        var gl = _gl ?? throw new Exception();

        gl.Viewport(s);

        var surface = CreateSkSurface();
        _game.UpdateScreenSize(surface);
    }

    private readonly Stopwatch _starttime = Stopwatch.StartNew();
    private TimeSpan _renderedTime = TimeSpan.Zero;

    private void Render(double deltaSeconds)
    {
        var surface = _surface ?? throw new Exception();
        var gl = _gl ?? throw new Exception();

        _controller.Update((float)deltaSeconds);

        var updated = false;
        var frameTime = TimeSpan.FromSeconds(1 / 60d);

        var delta = TimeSpan.FromSeconds(deltaSeconds);

        Graphics.SetSurface(surface);

        // JOE: TODO: Port this over to `delta`
        // while (_starttime.Elapsed - _renderedTime >= frameTime)
        {
            _game.FrameCounter++;

            _game.Update();

            _renderedTime += frameTime;
            updated = true;
        }

        if (updated)
        {
            // JOE: TODO: Fix that clearing the surface causes flicker.
            _game.Draw();
            surface.Flush();
        }

        return;
        // https://github.com/ocornut/imgui/blob/master/imgui_demo.cpp#L644
        // https://github.com/dotnet/Silk.NET/blob/main/examples/CSharp/OpenGL%20Demos/ImGui/Program.cs
        ImGui.ShowDemoWindow();
        ImGui.NewFrame();
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Open", "Ctrl+O"))
                {
                    _log.Write("OPEN");
                }
                if (ImGui.MenuItem("Save", "Ctrl+S")) { /* Handle save */ }
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("File2"))
            {
                if (ImGui.MenuItem("Open", "Ctrl+O")) { /* Handle open */ }
                if (ImGui.MenuItem("Save", "Ctrl+S")) { /* Handle save */ }
                ImGui.EndMenu();
            }
            ImGui.EndMainMenuBar();
        }

        _controller.Render();
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
