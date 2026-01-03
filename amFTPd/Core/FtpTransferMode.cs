/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpTransferMode.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x49BD0D4F
 *  
 *  Description:
 *      Specifies the transfer mode to be used for an FTP connection.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */

namespace amFTPd.Core;

/// <summary>
/// Specifies the transfer mode to be used for an FTP connection.
/// </summary>
/// <remarks>The transfer mode determines how the FTP client establishes the data connection: <list type="bullet">
/// <item> <term><see cref="None"/></term> <description>No transfer mode is specified.</description> </item> <item>
/// <term><see cref="Active"/></term> <description>The server establishes the data connection to the
/// client.</description> </item> <item> <term><see cref="Passive"/></term> <description>The client establishes the data
/// connection to the server.</description> </item> </list></remarks>
internal enum FtpTransferMode
{
    None,
    Active,
    Passive
}