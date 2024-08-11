using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace z1.IO;

[Flags]
internal enum DebugLogDestination
{
    None = 0,
    Debug = 1,
    File = 2,
    DebugBuildsOnly = 4 | All,

    All = Debug | File,
}

internal sealed class DebugLog
{
    private const int MaxLogSize = 5 * 1024 * 1024;

    private static readonly Lazy<string> _logFile = new(() => Path.Combine(Directories.Save, Path.Combine("logs.txt")));

    private static readonly FileStream? _fs;
    private static readonly byte[] _buffer = new byte[5 * 1024];

    private static readonly CancellationTokenSource _cts = new();
    private static readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(200)
    {
        SingleReader = true,
        SingleWriter = true,
    });

    private readonly string _name;
    private readonly bool _writefile;
    private readonly bool _writedebug;
    private readonly bool _disabled;

    static DebugLog()
    {
        _fs = File.Open(_logFile.Value, FileMode.Create, FileAccess.Write, FileShare.Read);

        // Write on a background thread so that IO is not blocking the main thread.
        // May want to move this over to a Task.Factory.StartNew(() => ... TaskCreationOptions.LongRunning)
        // Or move this off the async-only Channel. With a single reader/writer, a normal thread is ideal here.
        // And if we want to go really ham, reduce the string allocations.
        new Thread(WriterThread)
        {
            Name = nameof(DebugLog),
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
        }.Start();
    }

    public DebugLog(string name, DebugLogDestination destination = DebugLogDestination.All)
    {
        _name = name;

        if (destination.HasFlag(DebugLogDestination.DebugBuildsOnly) && !Debugger.IsAttached)
        {
            destination &= ~DebugLogDestination.All;
        }

        _writefile = destination.HasFlag(DebugLogDestination.File);
        _writedebug = destination.HasFlag(DebugLogDestination.Debug);
        _disabled = !_writefile && !_writedebug;
    }

    public DebugLog(string name, string subname, DebugLogDestination destination = DebugLogDestination.All)
        : this($"{name}[{subname}]", destination)
    {
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

    public void Write(string namespaze, string s)
    {
        if (_disabled) return;

        var log = $"{_name}[{namespaze}]: {s}";

        if (_writedebug) Debug.WriteLine(log);
        if (_writefile) _queue.Writer.TryWrite(log);
    }

    public void Write(string s)
    {
        if (_disabled) return;

        var log = $"{_name}: {s}";

        if (_writedebug) Debug.WriteLine(log);
        if (_writefile) _queue.Writer.TryWrite(log);
    }
}