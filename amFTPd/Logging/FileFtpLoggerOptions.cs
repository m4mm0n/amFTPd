namespace amFTPd.Logging;

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