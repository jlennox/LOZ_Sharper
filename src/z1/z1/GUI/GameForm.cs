using System.Diagnostics;
using System.Reflection;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Timer = System.Windows.Forms.Timer;

namespace z1.GUI;

// TODO:
// * Lanmola is busted. Easy path to one in 9 from bombhole.
// * Gleeok is borked.
// * celler pushblocks dont work.

public partial class GameForm : Form
{
    private readonly SKControl _skControl;
    private readonly Game _game = new();
    private readonly Timer _timer = new();
    private readonly GameCheats _cheats;
    private readonly FpsCalculator _fps = new();

    public GameForm()
    {
        InitializeComponent();

        _cheats = new GameCheats(_game);

        _skControl = new SKControl { Dock = DockStyle.Fill };
        _skControl.PaintSurface += OnPaintSurface;
        Controls.Add(_skControl);

        _timer.Interval = 16;
        _timer.Tick += Timer_Tick;
        _timer.Start();

        KeyPreview = true;
        KeyUp += Form1_KeyUp;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        _cheats.OnKeyPressed(keyData);

        if (_game.Input.SetKey(keyData))
        {
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Form1_KeyUp(object? sender, KeyEventArgs e)
    {
        _game.Input.UnsetKey(e.KeyCode);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _skControl.Invalidate();
    }

    private readonly Stopwatch _starttime = Stopwatch.StartNew();
    private TimeSpan _renderedTime = TimeSpan.Zero;

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var updated = false;
        var frameTime = TimeSpan.FromSeconds(1 / 60d);

        var elapsedTicks = _starttime.ElapsedMilliseconds;
        if (_fps.Add(elapsedTicks))
        {
            Text = $"Z1 - {Assembly.GetExecutingAssembly().GetName().Version} - {_fps.FramesPerSecond:F2} FPS";
        }

        // JOE: TODO: Should this instead use new SKPictureRecorder()?

        using var offScreenSurface = SKSurface.Create(e.Info);
        _game.UpdateScreenSize(offScreenSurface, e.Info);
        Graphics.SetSurface(offScreenSurface, e.Info);

        while (_starttime.Elapsed - _renderedTime >= frameTime)
        {
            _game.FrameCounter++;

            _game.Input.Update();
            _game.World.Update();
            _game.Sound.Update();

            _renderedTime += frameTime;
            updated = true;
        }

        if (updated)
        {
            _game.World.Draw();

            using var image = offScreenSurface.Snapshot();
            e.Surface.Canvas.DrawImage(image, new SKRect(0, 0, e.Info.Width, e.Info.Height));
        }
    }
}
