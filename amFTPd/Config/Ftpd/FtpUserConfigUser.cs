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
    /// Represents the configuration settings for an FTP user, including authentication details, permissions, and
    /// resource limits.
    /// </summary>
    /// <remarks>This record encapsulates various properties that define an FTP user's access and behavior
    /// within the FTP server. It includes authentication credentials, directory access, permissions for specific
    /// operations, and resource constraints.</remarks>
    /// <param name="UserName"></param>
    /// <param name="PasswordHash"></param>
    /// <param name="HomeDir"></param>
    /// <param name="IsAdmin"></param>
    /// <param name="AllowFxp"></param>
    /// <param name="AllowUpload"></param>
    /// <param name="AllowDownload"></param>
    /// <param name="AllowActiveMode"></param>
    /// <param name="MaxConcurrentLogins"></param>
    /// <param name="IdleTimeoutSeconds"></param>
    /// <param name="MaxUploadKbps"></param>
    /// <param name="MaxDownloadKbps"></param>
    /// <param name="GroupName"></param>
    /// <param name="CreditsKb"></param>
    /// <param name="AllowedIpMask"></param>
    /// <param name="RequireIdentMatch"></param>
    /// <param name="RequiredIdent"></param>
    public sealed record FtpUserConfigUser(
        string UserName,
        string PasswordHash,
        string HomeDir,
        bool IsAdmin,
        bool AllowFxp,
        bool AllowUpload,
        bool AllowDownload,
        bool AllowActiveMode,
        int MaxConcurrentLogins,
        int IdleTimeoutSeconds,
        int MaxUploadKbps,
        int MaxDownloadKbps,
        string? GroupName,
        long CreditsKb,
        string? AllowedIpMask,
        bool RequireIdentMatch,
        string? RequiredIdent
    );
}
