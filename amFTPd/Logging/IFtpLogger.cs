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