using System.Diagnostics;
using z1.Render;
using z1.IO;

namespace z1.UI;

internal sealed class OnScreenDisplay
{
    private record Entry(string Text, TimeSpan Duration, Stopwatch Timer);

    private static readonly DebugLog _log = new(nameof(OnScreenDisplay));

    private readonly List<Entry> _osds = new();

    public void Toast(string text)
    {
        _log.Write("Toast", text);
        Console.WriteLine(text);
        _osds.Add(new Entry(text, TimeSpan.FromSeconds(3), Stopwatch.StartNew()));
    }

    public void Draw(Graphics graphics)
    {
        var y = 10;
        for (var i = _osds.Count - 1; i >= 0; i--)
        {
            var osd = _osds[i];
            if (osd.Timer.Elapsed > osd.Duration)
            {
                _osds.RemoveAt(i);
                continue;
            }

            graphics.DrawString(osd.Text, 0, y - 1, Palette.Red, DrawingFlags.None, DrawOrder.OverlayForeground);
            graphics.DrawString(osd.Text, 1, y, 0, DrawingFlags.None, DrawOrder.Overlay);
            y += 10;
        }
    }
}