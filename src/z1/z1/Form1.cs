using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Timer = System.Windows.Forms.Timer;

namespace z1;

public partial class Form1 : Form
{
    private readonly SKControl _skControl;
    private readonly Game _game = new();
    private readonly Timer _timer = new();

    public Form1()
    {
        InitializeComponent();
        _skControl = new SKControl();
        _skControl.Dock = DockStyle.Fill;
        _skControl.PaintSurface += OnPaintSurface;
        Controls.Add(_skControl);

        _timer.Interval = 16; // Approximately 60fps
        _timer.Tick += Timer_Tick;
        _timer.Start();

        KeyPreview = true;
        KeyUp += Form1_KeyUp;
        KeyDown += new KeyEventHandler(Form1_KeyDown);
        _skControl.KeyUp += new KeyEventHandler(Form1_KeyUp);
        _skControl.KeyDown += new KeyEventHandler(Form1_KeyDown);

        _game._actors.Add(new Octorok());
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Ensure the key events are processed
        if (keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Left || keyData == Keys.Right)
        {
            Form1_KeyDown(this, new KeyEventArgs(keyData));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Form1_KeyUp(object? sender, KeyEventArgs e)
    {
        _game.UnsetKeys();
    }

    private void Form1_KeyDown(object? sender, KeyEventArgs e)
    {
        _game.SetKeys(e.KeyData);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _skControl.Invalidate();
    }

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        _game.UpdateScreenSize(e.Surface, e.Info);
        _game.UpdateActors();
    }
}
