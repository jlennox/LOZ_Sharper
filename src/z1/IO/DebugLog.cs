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

internal readonly record struct ScopedFunctionLog : IDisposable
{
    private static int _indentation = 1;
    public DebugLog DebugLog { get; init; }
    public string FunctionName { get; init; }
    private readonly LogLevel _level;

    public ScopedFunctionLog(DebugLog DebugLog, string FunctionName, LogLevel level = LogLevel.Debug)
    {
        _level = level;
        this.DebugLog = DebugLog;
        this.FunctionName = FunctionName;

        ++_indentation;
    }

    private static string Indentation => new('\t', _indentation);
    private static string IndentationEnter => new('\t', _indentation - 1);

    private bool ShouldWrite(LogLevel level) => level >= _level;

    public void Enter(string s)
    {
        if (ShouldWrite(LogLevel.Debug)) DebugLog.Write(FunctionName, $"{IndentationEnter}+ {s}");
    }

    public void Write(string s)
    {
        if (ShouldWrite(LogLevel.Debug)) DebugLog.Write(FunctionName, $"{Indentation}{s}");
    }

    public void Error(string s)
    {
        if (ShouldWrite(LogLevel.Error)) DebugLog.Error(FunctionName, $"{Indentation}{s}");
    }

    // Always write fatals (don't check ShouldWrite).
    public Exception Fatal(string s) => DebugLog.Fatal(FunctionName, $"{Indentation}{s}", s);

    public void Dispose()
    {
        --_indentation;
    }
}

internal enum LogLevel
{
    Debug,
    Error,
    Fatal,
}

internal sealed class DebugLogWriter : IDisposable
{
    private readonly record struct LogEntry(string Line, string? Prefix, LogLevel Level);

    private const int _maxLogSize = 200 * 1024 * 1024;

    private static readonly Lazy<string> _logFile = new(() => Path.Combine(Directories.Save, Path.Combine("logs.txt")));
    private static readonly Lazy<string> _logErrorFile = new(() => Path.Combine(Directories.Save, Path.Combine("logs-error.txt")));

    private readonly CancellationTokenSource _cts = new();
    private readonly AutoResetEvent _linesAvailableEvent = new(false);
    private readonly ConcurrentQueue<LogEntry> _entries = new();
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
        using var fs2 = File.Open(_logErrorFile.Value, FileMode.Create, FileAccess.Write, FileShare.Read);
        var reusedbuffer = new byte[5 * 1024];
        var waitEvents = new[] { _linesAvailableEvent, _cts.Token.WaitHandle };

        while (!_cts.IsCancellationRequested)
        {
            var wroteError = false;
            while (!_cts.IsCancellationRequested && _entries.TryDequeue(out var entry))
            {
                var line = entry.Line;
                var prefix = entry.Prefix;

                // 50 is a way overshot of what the datetime/etc characters will need.
                var maxbytes = Encoding.UTF8.GetMaxByteCount(line.Length + (prefix?.Length ?? 0) + 50);
                // Don't store the new buffer to prevent indefinite size creep.
                var buffer = maxbytes > reusedbuffer.Length ? new byte[maxbytes] : reusedbuffer;
                // Keep allocations low. $"{DateTime.Now}: {s}\n";
                if (!DateTime.Now.TryFormat(buffer, out var encodedBytes)) continue;
                encodedBytes += Encoding.UTF8.GetBytes(": ", buffer.AsSpan(encodedBytes));
                if (prefix != null)
                {
                    // encodedBytes += Encoding.UTF8.GetBytes("[", buffer.AsSpan(encodedBytes));
                    encodedBytes += Encoding.UTF8.GetBytes(prefix, buffer.AsSpan(encodedBytes));
                    // encodedBytes += Encoding.UTF8.GetBytes("] ", buffer.AsSpan(encodedBytes));
                }
                encodedBytes += Encoding.UTF8.GetBytes(line, buffer.AsSpan(encodedBytes));
                encodedBytes += Encoding.UTF8.GetBytes("\n", buffer.AsSpan(encodedBytes));

                fs.Write(buffer, 0, encodedBytes);

                if (entry.Level >= LogLevel.Error)
                {
                    fs2.Write(buffer, 0, encodedBytes);
                    wroteError = true;
                }
            }

            fs.Flush();
            if (wroteError) fs2.Flush();

            // TODO: Actual log rotation.
            if (fs.Length > _maxLogSize) fs.SetLength(0);
            if (fs2.Length > _maxLogSize) fs2.SetLength(0);

            WaitHandle.WaitAny(waitEvents);
        }

        _cts.TryDispose();
        _linesAvailableEvent.TryDispose();
    }

    public void Write(string s, LogLevel level)
    {
        _entries.Enqueue(new LogEntry(s, null, level));
        _linesAvailableEvent.Set();
    }

    public void Write(string s, string prefix, LogLevel level)
    {
        _entries.Enqueue(new LogEntry(s, prefix, level));
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
    private readonly LogLevel _level;

    public DebugLog(string name,  DebugLogDestination destination = DebugLogDestination.All, LogLevel level = LogLevel.Debug)
    {
        _name = name;
        _level = level;

        if (destination.HasFlag(DebugLogDestination.DebugBuildsOnly) && !Debugger.IsAttached)
        {
            destination &= ~DebugLogDestination.All;
        }

        _writefile = destination.HasFlag(DebugLogDestination.File);
        _writedebug = destination.HasFlag(DebugLogDestination.Debug);
        _disabled = !_writefile && !_writedebug;
    }

    public DebugLog(string name, string subname, DebugLogDestination destination = DebugLogDestination.All, LogLevel level = LogLevel.Debug)
        : this($"{name}[{subname}]", destination, level)
    {
    }

    public FunctionLog CreateFunctionLog([CallerMemberName] string functionName = "") => new(this, functionName);
    public ScopedFunctionLog CreateScopedFunctionLog(string scope, [CallerMemberName] string functionName = "", LogLevel level = LogLevel.Debug)
    {
        return new ScopedFunctionLog(this, $"{functionName}->{scope}", level);
    }

    internal void Write(string? namespaze, string s, LogLevel level)
    {
        if (_disabled) return;
        if (level < _level) return;

        var log = namespaze == null
            ? $"{_name}: {s}"
            : $"{_name}[{namespaze}]: {s}";

        if (_writedebug) Debug.WriteLine(log);
        if (_writefile) _writer.Write(log, level);
    }

    public void Write(string namespaze, string s) => Write(namespaze, s, LogLevel.Debug);
    public void Write(string s) => Write(null, s, LogLevel.Debug);

    public void Error(string namespaze, string s) => Write(namespaze, $"ERROR: 🚨🚨 {s}", LogLevel.Error);
    public void Error(string s) => Write(null, $"ERROR: 🚨🚨 {s}", LogLevel.Error);

    public Exception Fatal(string namespaze, string s)
    {
        Write(namespaze, s, LogLevel.Fatal);
        return new Exception(s.Trim());
    }

    public Exception Fatal(string namespaze, string s, string exception)
    {
        Write(namespaze, s, LogLevel.Fatal);
        return new Exception(exception);
    }

    public Exception Fatal(string s)
    {
        Write(null, s, LogLevel.Fatal);
        return new Exception(s.Trim());
    }
}