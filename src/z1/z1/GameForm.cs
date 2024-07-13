using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp.Views.Desktop;
using Timer = System.Windows.Forms.Timer;

namespace z1;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct TileMapxx
{
    public const int Size = World.Rows * World.Columns;

    public fixed byte tileRefs[World.Rows * World.Columns];
    public fixed byte tileBehaviors[World.Rows * World.Columns];

    public byte Num => tileRefs[4];
    public byte[] ToArray()
    {
        fixed (byte* p = tileRefs)
        {
            return new Span<byte>(p, Size).ToArray();
        }
    }

    public ref byte Refs(int index) => ref tileRefs[index];
    public ref byte Refs(int row, int col) => ref tileRefs[row * World.Columns + col];
    public ref byte Behaviors(int row, int col) => ref tileBehaviors[row * World.Columns + col];
    public ref byte Behaviors(int index) => ref tileBehaviors[index];
    public TileBehavior AsBehaviors(int row, int col) => (TileBehavior)tileBehaviors[row * World.Columns + col];
}


public partial class GameForm : Form
{
    private readonly SKControl _skControl;
    private readonly Game _game = new();
    private readonly Timer _timer = new();

    public GameForm()
    {
        InitializeComponent();


        var asd = new TileMapxx();
        asd.Refs(4) = 3;
        var asdasdasd = asd.ToArray();

        _skControl = new() { Dock = DockStyle.Fill };
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

        // _game.World.GotoLoadLevel(0, true);

        // for (var i = 0; i < 5; ++i)
        // {
        //     var x = Random.Shared.Next(0, 50);
        //     var y = Random.Shared.Next(0, 50);
        //     _game._actors.Add(new OctorokActor(_game, ActorColor.Red, false, x, y));
        // }
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

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        // _game.UpdateScreenSize(e.Surface, e.Info);
        // _game.UpdateActors();

        bool updated = false;

        var watch = Stopwatch.StartNew();
        var offset = TimeSpan.Zero;

        var frameTime = TimeSpan.FromSeconds(1 / 60d);
        _game.UpdateScreenSize(e.Surface, e.Info);
        Graphics.SetSurface(e.Surface, e.Info);

        //while (watch.Elapsed + offset >= frameTime)
        {
            _game.FrameCounter++;

            // Input::Update();
            // World::Update();
            // Sound::Update();

            _game.World.Update();
            _game.Sound.Update();

            offset += frameTime;
            updated = true;
        }

        if (updated)
        {
            _game.World.Draw();

            // al_flip_display();
        }

        var timeLeft = (watch.Elapsed + offset) + frameTime;
        // if (timeLeft >= .002)
        //     waitSpan = timeLeft - .001;
        // else
        //     waitSpan = 0;
    }
}
