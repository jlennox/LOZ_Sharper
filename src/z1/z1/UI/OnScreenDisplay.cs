using System.Diagnostics;

namespace z1.UI;

internal sealed class OnScreenDisplay
{
    private record Entry(string Text, TimeSpan Duration, Stopwatch Timer);

    private readonly List<Entry> _osds = new();

    public void Toast(string text)
    {
        _osds.Add(new Entry(text, TimeSpan.FromSeconds(5), Stopwatch.StartNew()));
    }

    public void Draw()
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

            GlobalFunctions.DrawString(osd.Text, 1, y, 0, DrawingFlags.None);
            y += 10;
        }
    }
}