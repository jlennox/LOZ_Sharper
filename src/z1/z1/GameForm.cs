using System.Diagnostics;
using System.Reflection;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Timer = System.Windows.Forms.Timer;

namespace z1;

internal sealed class FpsCalculator
{
    public double FramesPerSecond { get; private set; }

    private int _tickindex = 0;
    private long _ticksum = 0;
    private readonly long[] _ticklist = new long[100];

    public bool Add(long newtick)
    {
        _ticksum -= _ticklist[_tickindex];
        _ticksum += newtick;
        _ticklist[_tickindex] = newtick;
        _tickindex = (_tickindex + 1) % _ticklist.Length;

        FramesPerSecond = (double)_ticksum / _ticklist.Length;
        return _tickindex == 0;
    }
}

// TODO:
// * The refactor to CurrentUWRoomAttrs (and maybe the other?) screwed up a bunch of stuff that used
//   an argument not curRoomId.
// * Look up interfaces and fix all that are not properly applied.

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
