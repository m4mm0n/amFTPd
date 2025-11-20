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

namespace amFTPd.Config.Ftpd
{
    /// <summary>
    /// Represents an FTP user with configurable permissions, resource limits, and access restrictions.
    /// </summary>
    /// <remarks>This record encapsulates the configuration and permissions for an FTP user, including
    /// authentication details, directory access, bandwidth limits, and security settings. It is designed to be
    /// immutable and thread-safe.</remarks>
    /// <param name="UserName"></param>
    /// <param name="PasswordHash"></param>
    /// <param name="HomeDir"></param>
    /// <param name="IsAdmin"></param>
    /// <param name="AllowFxp"></param>
    /// <param name="AllowUpload"></param>
    /// <param name="AllowDownload"></param>
    /// <param name="AllowActiveMode"></param>
    /// <param name="MaxConcurrentLogins"></param>
    /// <param name="IdleTimeout"></param>
    /// <param name="MaxUploadKbps"></param>
    /// <param name="MaxDownloadKbps"></param>
    /// <param name="GroupName"></param>
    /// <param name="CreditsKb"></param>
    /// <param name="AllowedIpMask"></param>
    /// <param name="RequireIdentMatch"></param>
    /// <param name="RequiredIdent"></param>
    public sealed record FtpUser(
        string UserName,
        string PasswordHash,
        string HomeDir,
        bool IsAdmin,
        bool AllowFxp,
        bool AllowUpload,
        bool AllowDownload,
        bool AllowActiveMode,
        int MaxConcurrentLogins,
        TimeSpan IdleTimeout,
        int MaxUploadKbps,
        int MaxDownloadKbps,
        string? GroupName,
        long CreditsKb,
        string? AllowedIpMask,     // e.g. "1.2.3.*" or "10.0.0.0/8"
        bool RequireIdentMatch,    // enforce ident
        string? RequiredIdent      // required ident username (lowercase or mixed)
    );
}
