using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

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

internal sealed class DebugLogWriter : IDisposable
{
    private const int MaxLogSize = 5 * 1024 * 1024;

    private static readonly Lazy<string> _logFile = new(() => Path.Combine(Directories.Save, Path.Combine("logs.txt")));

    private readonly CancellationTokenSource _cts = new();
    private readonly AutoResetEvent _linesAvailableEvent = new(false);
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly Thread _thread;

    public DebugLogWriter()
    {
        // Write on a background thread so that IO is not blocking the main thread.
        _thread = new Thread(WriterThread)
        {
            Name = nameof(DebugLogWriter),
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start();
    }

    private void WriterThread()
    {
        using var fs = File.Open(_logFile.Value, FileMode.Create, FileAccess.Write, FileShare.Read);
        var reusedbuffer = new byte[5 * 1024];
        var waitEvents = new[] { _linesAvailableEvent, _cts.Token.WaitHandle };

        while (!_cts.IsCancellationRequested)
        {
            while (!_cts.IsCancellationRequested && _lines.TryDequeue(out var line))
            {
                // 50 is a way overshot of what the datetime and other added characters will need.
                var maxbytes = Encoding.UTF8.GetMaxByteCount(line.Length + 50);
                // Don't store the new buffer to prevent indefinite size creep.
                var buffer = maxbytes > reusedbuffer.Length ? new byte[maxbytes] : reusedbuffer;
                // Keep allocations low. $"{DateTime.Now}: {s}\n";
                if (!DateTime.Now.TryFormat(buffer, out var encodedBytes)) continue;
                encodedBytes += Encoding.UTF8.GetBytes(": ", buffer.AsSpan(encodedBytes));
                encodedBytes += Encoding.UTF8.GetBytes(line, buffer.AsSpan(encodedBytes));
                encodedBytes += Encoding.UTF8.GetBytes("\n", buffer.AsSpan(encodedBytes));

                fs.Write(buffer, 0, encodedBytes);
                fs.Flush();
            }

            if (fs.Length > MaxLogSize)
            {
                fs.SetLength(0);
            }

            WaitHandle.WaitAny(waitEvents);
        }

        _cts.TryDispose();
        _linesAvailableEvent.TryDispose();
    }

    public void Write(string s)
    {
        _lines.Enqueue(s);
        _linesAvailableEvent.Set();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread.Join(); // Questionable but irrelevant.
        // Disposing is taken care of inside WriterThread to prevent use/cleanup races.
    }
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

    public void Error(string s)
    {
        // JOE: TODO
        Write($"ERROR: 🚨🚨 {s}");
    }
}