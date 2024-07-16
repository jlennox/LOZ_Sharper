using System.Diagnostics;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Timer = System.Windows.Forms.Timer;

namespace z1;

public partial class GameForm : Form
{
    private readonly SKControl _skControl;
    private readonly Game _game = new();
    private readonly Timer _timer = new();

    public GameForm()
    {
        InitializeComponent();

        _skControl = new SKControl { Dock = DockStyle.Fill };
        _skControl.PaintSurface += OnPaintSurface;
        Controls.Add(_skControl);

        _timer.Interval = 16;
        _timer.Tick += Timer_Tick;
        _timer.Start();

        KeyPreview = true;
        KeyUp += Form1_KeyUp;
        KeyDown += Form1_KeyDown;
        _skControl.KeyUp += Form1_KeyUp;
        _skControl.KeyDown += Form1_KeyDown;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Input.SetKey(keyData))
        {
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Form1_KeyUp(object? sender, KeyEventArgs e)
    {
        Input.UnsetKey(e.KeyCode);
    }

    private void Form1_KeyDown(object? sender, KeyEventArgs e)
    {
        _game.SetKeys(e.KeyData);
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

        using var offScreenSurface = SKSurface.Create(e.Info);
        _game.UpdateScreenSize(offScreenSurface, e.Info);
        Graphics.SetSurface(offScreenSurface, e.Info);

        while (_starttime.Elapsed - _renderedTime >= frameTime)
        {
            _game.FrameCounter++;

            Input.Update();
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
