using System.Diagnostics;
using Silk.NET.Windowing;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Maths;
using SkiaSharp;

namespace z1.GUI;

internal sealed class GLWindow : IDisposable
{
    private readonly Game _game = new();
    private readonly IWindow? _window;

    private GL? _gl;
    private IInputContext? _inputContext;

    private GRGlInterface? _glinterface;
    private GRContext? _grcontext;
    private SKSurface? _surface;
    private GRBackendRenderTarget? _rendertarget;

    public GLWindow()
    {
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
        _window.Run();
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

    private void OnLoad()
    {
        var window = _window ?? throw new Exception();

        _gl = window.CreateOpenGL();
        _inputContext = window.CreateInput();

        // JOE: TODO: Use _inputContext.ConnectionChanged and yadda yadda yadda.
        var targetkb = _inputContext.Keyboards[0];
        targetkb.KeyDown += OnKeyDown;
        targetkb.KeyUp += OnKeyUp;

        var surface = CreateSkSurface();
        _game.UpdateScreenSize(surface);
    }

    private void OnFocusChanged(bool focused)
    {
        if (!focused) _game.Input.UnsetAllKeys();
    }

    private void OnKeyDown(IKeyboard kb, Key key, int whoknows)
    {
        if (!_game.Input.SetKey(key)) _game.Input.SetLetter(key.GetKeyCharacter());
        _game.GameCheats.OnKeyPressed(key);
    }

    private void OnKeyUp(IKeyboard kb, Key key, int whoknows)
    {
        if (!_game.Input.UnsetKey(key)) _game.Input.UnsetLetter(key.GetKeyCharacter());
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

        var updated = false;
        var frameTime = TimeSpan.FromSeconds(1 / 60d);

        var delta = TimeSpan.FromSeconds(deltaSeconds);

        Graphics.SetSurface(surface);

        // JOE: TODO: Port this over to `delta`
        // while (_starttime.Elapsed - _renderedTime >= frameTime)
        {
            _game.FrameCounter++;

            _game.World.Update();
            _game.Sound.Update();
            _game.Input.Update();

            _renderedTime += frameTime;
            updated = true;
        }

        if (updated)
        {
            _game.World.Draw();
            surface.Flush();
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
