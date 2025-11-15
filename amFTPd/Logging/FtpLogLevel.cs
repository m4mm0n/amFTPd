namespace amFTPd.Logging;

/// <summary>
/// Specifies the logging levels available for FTP operations.
/// </summary>
/// <remarks>The logging level determines the granularity of log messages generated during FTP operations. Use
/// higher levels, such as <see cref="Critical"/> or <see cref="Error"/>, for minimal logging,  and lower levels, such
/// as <see cref="Trace"/> or <see cref="Debug"/>, for detailed diagnostic information.</remarks>
public enum FtpLogLevel { Trace, Debug, Info, Warn, Error, Critical }