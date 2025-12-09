/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           CombinedFtpLogger.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x144CABF3
 *  
 *  Description:
 *      Represents a composite FTP logger that delegates logging operations to multiple underlying loggers.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Logging
{
    /// <summary>
    /// Represents a composite FTP logger that delegates logging operations to multiple underlying loggers.
    /// </summary>
    /// <remarks>This logger forwards all log messages to the provided <see cref="IFtpLogger"/> instances.  If
    /// an exception occurs while logging to one of the loggers, the exception is suppressed to ensure  that logging
    /// continues for the remaining loggers. This class is particularly useful for scenarios  where logs need to be
    /// written to multiple destinations, such as a file and a console.</remarks>
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
