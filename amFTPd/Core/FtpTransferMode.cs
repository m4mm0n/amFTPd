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