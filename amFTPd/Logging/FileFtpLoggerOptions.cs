namespace amFTPd.Logging;

/// <summary>
/// Represents configuration options for the <see cref="FileFtpLogger"/>.
/// </summary>
/// <remarks>This class provides settings to control the behavior of the <see cref="FileFtpLogger"/>,  including
/// the log file path, the minimum log level, and an optional custom log message formatter.</remarks>
public sealed class FileFtpLoggerOptions
{
    /// <summary>
    /// Path to the log file. Required.
    /// </summary>
    public string FilePath { get; set; } = "ftp.log";

    /// <summary>
    /// Minimum log level that will be written.
    /// </summary>
    public FtpLogLevel MinLevel { get; set; } = FtpLogLevel.Info;

    /// <summary>
    /// Optional custom formatter. If null, a default formatter is used.
    /// </summary>
    public Func<FtpLogLevel, string, Exception?, DateTime, string>? Formatter { get; set; }
}