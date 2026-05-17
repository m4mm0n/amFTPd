using QuickLog;
using QuickLog.Loggers;

namespace amFTPd.Logging;

/// <summary>
/// Bridges amFTPd's logger abstraction to QuickLog.
/// </summary>
public sealed class QuickLogFtpLogger : IFtpLogger, IQuickLogModeController, IDisposable
{
    private readonly IQuickLog _quickLog;
    private readonly Action? _shutdown;
    private readonly object _modeSync = new();
    private bool _disposed;
    private QuickLogMode _mode;

    public QuickLogFtpLogger(
        IQuickLog quickLog,
        QuickLogMode mode,
        Action? shutdown = null)
    {
        _quickLog = quickLog ?? throw new ArgumentNullException(nameof(quickLog));
        _mode = mode;
        _shutdown = shutdown;
    }

    public QuickLogMode Mode
    {
        get
        {
            lock (_modeSync)
            {
                return _mode;
            }
        }
    }

    public void SetMode(QuickLogMode mode)
    {
        lock (_modeSync)
        {
            _mode = mode;
        }
    }

    public void Log(FtpLogLevel level, string message, Exception? ex = null)
    {
        if (_disposed || !ShouldLog(level, message))
            return;

        var quickLogLevel = MapLevel(level);
        if (ex is null)
        {
            _quickLog.Log(quickLogLevel, message, "amFTPd", string.Empty, 0);
        }
        else
        {
            _quickLog.Log(quickLogLevel, message, ex, "amFTPd", string.Empty, 0);
        }
    }

    public void Flush()
    {
        if (_quickLog is QuickLogger quickLogger)
            quickLogger.Flush();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            Flush();
        }
        finally
        {
            _shutdown?.Invoke();
            if (_shutdown is null && _quickLog is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private bool ShouldLog(FtpLogLevel level, string message)
    {
        return Mode switch
        {
            QuickLogMode.Everything => true,
            QuickLogMode.Something => level >= FtpLogLevel.Info,
            QuickLogMode.Quiet => level >= FtpLogLevel.Error || IsLifecycleMessage(message),
            _ => level >= FtpLogLevel.Info
        };
    }

    private static bool IsLifecycleMessage(string message)
    {
        return message.Contains("[amFTPd]", StringComparison.OrdinalIgnoreCase) &&
               (message.Contains("start", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("stop", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("shutdown", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("crash", StringComparison.OrdinalIgnoreCase));
    }

    private static LogType MapLevel(FtpLogLevel level) => level switch
    {
        FtpLogLevel.Trace => LogType.Trace,
        FtpLogLevel.Debug => LogType.Debug,
        FtpLogLevel.Info => LogType.Info,
        FtpLogLevel.Warn => LogType.Warn,
        FtpLogLevel.Error => LogType.Error,
        FtpLogLevel.Critical => LogType.Crit,
        _ => LogType.Info
    };
}
