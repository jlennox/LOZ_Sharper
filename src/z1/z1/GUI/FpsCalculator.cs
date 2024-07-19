namespace z1.GUI;

// TODO: Fix :)
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