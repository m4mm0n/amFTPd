using amFTPd.Utils;

namespace amFTPd.Logging
{
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
