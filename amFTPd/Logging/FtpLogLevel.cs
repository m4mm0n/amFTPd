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
/// Specifies the logging levels available for FTP operations.
/// </summary>
/// <remarks>The logging level determines the granularity of log messages generated during FTP operations. Use
/// higher levels, such as <see cref="Critical"/> or <see cref="Error"/>, for minimal logging,  and lower levels, such
/// as <see cref="Trace"/> or <see cref="Debug"/>, for detailed diagnostic information.</remarks>
public enum FtpLogLevel { Trace, Debug, Info, Warn, Error, Critical }