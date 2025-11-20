/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-20
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

namespace amFTPd.Logging;

/// <summary>
/// Defines a logger for FTP operations, allowing messages and exceptions to be logged at a specified log level.
/// </summary>
/// <remarks>This interface is designed to provide a flexible logging mechanism for FTP-related activities.
/// Implementations can determine how log messages are processed, such as writing to a file, console, or external
/// logging system.</remarks>
public interface IFtpLogger
{
    /// <summary>
    /// Logs a message with the specified log level and an optional exception.
    /// </summary>
    /// <param name="level">The severity level of the log entry.</param>
    /// <param name="message">The message to log. Cannot be null or empty.</param>
    /// <param name="ex">An optional exception to include in the log entry. Can be null.</param>
    void Log(FtpLogLevel level, string message, Exception? ex = null);
}