using System.Diagnostics;
using System.Reflection;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Timer = System.Windows.Forms.Timer;

namespace z1.GUI;

// Bugs:
// * Can't hit wizzrobes with sword.
// * Celler pushblocks dont work.
// * Traps move slow?
// * No spawn clouds?
// * Doors in dungeons and drawing priority.
// * Likelike's don't hold correctly.
// * Power bracelet does not work. At least in NE location.

// To check:
// * Check `IsReoccuring` is proper.
// * Consider refactoring monster projectile creation into generic CreateProjectile?
// * Do bubbles work properly?

// TODO:
// * SIMD the palettes?
// * Eliminate paramterless SpriteImage constructor?

// Enhancements:
// * Having the red candle causes dark rooms to auto fade in.
//   Blue candle does not because it can only be used once per room, this would be too strong of a buf as a weapon for it.

// Monsters:
// * Manhandla:              W8, u1
// * Gleeok:                 W8, u4, l2
// * Cellar:                 W8, l2
// * Ruppee Boss:            W8, u5, l1
// * Crab:                   W8, u3, l1
// * Moldorm:                W7, r1
// * Lamnola:                W9: u2, l1
// * Patra (expand, type 2): W9: u5, r1

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

        if (_game.Input.SetKey(keyData) || _game.Input.SetLetter(keyData.GetKeyCharacter()))
        {
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Form1_KeyUp(object? sender, KeyEventArgs e)
    {
        _game.Input.UnsetKey(e.KeyCode);
        _game.Input.SetLetter(e.KeyCode.GetKeyCharacter());
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

            _game.World.Update();
            _game.Sound.Update();
            _game.Input.Update();

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
