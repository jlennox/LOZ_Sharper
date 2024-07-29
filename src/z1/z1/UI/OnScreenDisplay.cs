using System.Diagnostics;

namespace z1.UI;

internal sealed class OnScreenDisplay
{
    private record Entry(string Text, int X, int Y, TimeSpan Duration, Stopwatch Timer, bool IsToast = false);

    private readonly List<Entry> _osds = new();

    public void Toast(string text)
    {
        var toastCount = _osds.Count(t => t.IsToast);
        _osds.Add(new Entry(text, 1, 10 + toastCount * 10, TimeSpan.FromSeconds(5), Stopwatch.StartNew(), true));
    }

    public void Draw()
    {
        for (var i = _osds.Count - 1; i >= 0; i--)
        {
            var osd = _osds[i];
            if (osd.Timer.Elapsed > osd.Duration)
            {
                _osds.RemoveAt(i);
                continue;
            }

            GlobalFunctions.DrawString(osd.Text, osd.X, osd.Y, 0, DrawingFlags.None);
        }
    }
}