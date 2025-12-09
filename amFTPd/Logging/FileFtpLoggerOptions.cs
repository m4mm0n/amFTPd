/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FileFtpLoggerOptions.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xFFF43626
 *  
 *  Description:
 *      Represents configuration options for the <see cref="FileFtpLogger"/>.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





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