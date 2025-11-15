namespace amFTPd.Logging
{

    public sealed class CombinedFtpLogger : IFtpLogger, IDisposable
    {
        private readonly IReadOnlyList<IFtpLogger> _loggers;
        private bool _disposed;

        public CombinedFtpLogger(params IFtpLogger[] loggers)
        {
            if (loggers == null) throw new ArgumentNullException(nameof(loggers));
            if (loggers.Length == 0)
                throw new ArgumentException("At least one logger must be provided.", nameof(loggers));

            _loggers = loggers;
        }

        public void Log(FtpLogLevel level, string message, Exception? ex = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CombinedFtpLogger));

            foreach (var logger in _loggers)
            {
                try
                {
                    logger.Log(level, message, ex);
                }
                catch
                {
                    // Intentionally swallow to avoid cascading failures in logging.
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var logger in _loggers)
            {
                if (logger is IDisposable d)
                {
                    try
                    {
                        d.Dispose();
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }
    }
}
