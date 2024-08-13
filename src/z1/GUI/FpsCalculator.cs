namespace z1.GUI;

internal class FpsCalculator
{
    private readonly double[] _buffer = new double[100];
    private int _bufferIndex = 0;
    private int _bufferCount = 0;

    public double Add(double deltaSeconds)
    {
        var currentFps = 1.0 / deltaSeconds;

        _buffer[_bufferIndex] = currentFps;
        _bufferIndex = (_bufferIndex + 1) % _buffer.Length;
        if (_bufferCount < _buffer.Length)
        {
            _bufferCount++;
        }

        var weightedFpsSum = 0.0;
        var weightSum = 0.0;
        for (var i = 0; i < _bufferCount; i++)
        {
            var weight = (double)(i + 1) / _bufferCount;
            var index = (_bufferIndex - 1 - i + _buffer.Length) % _buffer.Length;
            weightedFpsSum += _buffer[index] * weight;
            weightSum += weight;
        }

        return weightedFpsSum / weightSum;
    }
}