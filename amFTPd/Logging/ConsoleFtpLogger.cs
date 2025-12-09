/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ConsoleFtpLogger.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xCD1364D5
 *  
 *  Description:
 *      Provides a console-based implementation of the <see cref="IFtpLogger"/> interface for logging FTP-related messages.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





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
