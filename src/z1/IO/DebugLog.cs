using System.Diagnostics;
using System.Runtime.CompilerServices;
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

internal readonly record struct FunctionLog(DebugLog DebugLog, string FunctionName)
{
    public void Write(string s) => DebugLog.Write(FunctionName, s);
}

internal sealed class DebugLogWriter
{
    private const int MaxLogSize = 5 * 1024 * 1024;

    private static readonly Lazy<string> _logFile = new(() => Path.Combine(Directories.Save, Path.Combine("logs.txt")));

    private readonly FileStream? _fs;
    private readonly byte[] _buffer = new byte[5 * 1024];

    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(200)
    {
        SingleReader = true,
        SingleWriter = true,
    });

    public DebugLogWriter()
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

    private static string FormatLine(string s) => $"{DateTime.Now}: {s}\n";

    private void WriterThread()
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
            var encodedBytes = Encoding.UTF8.GetBytes(line, buffer);

            fs.Write(buffer, 0, encodedBytes);
            fs.Flush();
        }

        try
        {
            fs.Close();
            fs.Dispose();
        }
        catch { }
    }

    public void Write(string s) => _queue.Writer.TryWrite(s);
}

internal sealed class DebugLog
{
    private static readonly DebugLogWriter _writer = new();

    private readonly string _name;
    private readonly bool _writefile;
    private readonly bool _writedebug;
    private readonly bool _disabled;

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

    public FunctionLog CreateFunctionLog([CallerMemberName] string functionName = "") => new(this, functionName);

    public void Write(string namespaze, string s)
    {
        if (_disabled) return;

        var log = $"{_name}[{namespaze}]: {s}";

        if (_writedebug) Debug.WriteLine(log);
        if (_writefile) _writer.Write(log);
    }

    public void Write(string s)
    {
        if (_disabled) return;

        var log = $"{_name}: {s}";

        if (_writedebug) Debug.WriteLine(log);
        if (_writefile) _writer.Write(log);
    }
}