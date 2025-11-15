using amFTPd.Utils;

namespace amFTPd.Logging
{
    /// <summary>
    /// Provides a console-based implementation of the <see cref="IFtpLogger"/> interface for logging FTP-related
    /// messages.
    /// </summary>
    /// <remarks>This logger outputs log messages to the console, formatted with a timestamp, log level, and
    /// message content. In debug builds, all log levels are output. In release builds, only messages with a log level
    /// of <see cref="FtpLogLevel.Info"/> are logged.</remarks>
    public sealed class ConsoleFtpLogger : IFtpLogger
    {
        public void Log(FtpLogLevel level, string message, Exception? ex = null)
        {
#if DEBUG
            var tag = level.ToString().ToUpperInvariant();
            $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {tag}: {message}".WriteStyledLogLine();
            if (ex != null) ex.ToString().WriteStyledLogLine();
#else
            if (level != FtpLogLevel.Info)
                return;
            var tag = level.ToString().ToUpperInvariant();
            $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {tag}: {message}".WriteStyledLogLine();
            if (ex != null) ex.ToString().WriteStyledLogLine();
#endif
        }
    }
}
