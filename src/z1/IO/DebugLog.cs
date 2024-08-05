using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace z1.IO;

internal sealed class DebugLog
{
    private const int MaxLogSize = 5 * 1024 * 1024;

    private static readonly Lazy<string> _logFile = new(() => Path.Combine(Directories.Save, Path.Combine("logs.txt")));

    private static FileStream? _fs;
    private static readonly byte[] _buffer = new byte[5 * 1024];

    private static readonly CancellationTokenSource _cts = new();
    private static readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = true,
    });

    private readonly string _name;

    static DebugLog()
    {
        _fs = File.Open(_logFile.Value, FileMode.Create, FileAccess.Write, FileShare.Read);

        new Thread(WriterThread)
        {
            Name = nameof(DebugLog),
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
        }.Start();
    }

    public DebugLog(string name)
    {
        _name = name;
    }

    private static void WriterThread()
    {
        var fs = _fs ?? throw new InvalidOperationException("DebugLog not initialized.");

        while (!_cts.IsCancellationRequested)
        {
            if (fs.Length > MaxLogSize)
            {
                fs.SetLength(0);
            }

            var s = _queue.Reader.ReadAsync(_cts.Token).AsTask().Result;

            var line = FormatLine(s);
            var maxbytes = Encoding.UTF8.GetMaxByteCount(line.Length);
            var buffer = maxbytes > _buffer.Length ? new byte[maxbytes] : _buffer;
            var encodedBytes = Encoding.UTF8.GetBytes(FormatLine(s), buffer);

            fs.Write(buffer, 0, encodedBytes);
            fs.Flush();
        }
    }

    internal static string FormatLine(string s) => $"{DateTime.Now}: {s}\n";

    public void Write(string s) => WriteLog($"{_name}: {s}");

    public static void WriteLog(string s)
    {
        Debug.WriteLine(s);
        _queue.Writer.TryWrite(s);
    }
}