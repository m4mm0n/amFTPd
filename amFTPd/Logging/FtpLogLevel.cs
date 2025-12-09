/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpLogLevel.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x4B6B0A20
 *  
 *  Description:
 *      Specifies the logging levels available for FTP operations.
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
/// Specifies the logging levels available for FTP operations.
/// </summary>
/// <remarks>The logging level determines the granularity of log messages generated during FTP operations. Use
/// higher levels, such as <see cref="Critical"/> or <see cref="Error"/>, for minimal logging,  and lower levels, such
/// as <see cref="Trace"/> or <see cref="Debug"/>, for detailed diagnostic information.</remarks>
public enum FtpLogLevel { Trace, Debug, Info, Warn, Error, Critical }