/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpSessionReputation.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-11 03:12:44
 *  Last Modified:  2025-12-11 03:12:59
 *  CRC32:          0xE7DC70B6
 *  
 *  Description:
 *      Specifies the reputation status of an FTP session, indicating whether the session is considered safe, suspicious, or...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */

namespace amFTPd.Config.Ftpd
{
    /// <summary>
    /// Specifies the reputation status of an FTP session, indicating whether the session is considered safe,
    /// suspicious, or blocked.
    /// </summary>
    /// <remarks>Use this enumeration to assess or assign the trust level of an FTP session. The reputation
    /// may affect access control or monitoring decisions within FTP-related operations.</remarks>
    public enum FtpSessionReputation
    {
        Good,
        Suspect,
        Blocked
    }
}
