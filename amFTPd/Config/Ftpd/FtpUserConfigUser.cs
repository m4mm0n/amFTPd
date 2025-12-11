/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpUserConfigUser.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-11 02:49:55
 *  CRC32:          0x7D28BD4D
 *  
 *  Description:
 *      Raw config entry for a single user (as stored in DB / config).
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
    /// Raw config entry for a single user (as stored in DB / config).
    /// </summary>
    public sealed record FtpUserConfigUser
    {
        public string UserName { get; init; } = string.Empty;
        public string PasswordHash { get; init; } = string.Empty;
        public bool Disabled { get; init; }

        /// <summary>Physical home directory.</summary>
        public string HomeDir { get; init; } = string.Empty;

        public int MaxConcurrentLogins { get; init; }

        /// <summary>Primary group name.</summary>
        public string GroupName { get; init; } = string.Empty;

        /// <summary>Additional group names.</summary>
        public IReadOnlyList<string> SecondaryGroups { get; init; }
            = Array.Empty<string>();

        public bool IsAdmin { get; init; }

        /// <summary>
        /// Compatibility flag if some code uses "IsAdministrator".
        /// </summary>
        public bool IsAdministrator { get; init; }

        public bool AllowFxp { get; init; }
        public bool AllowUpload { get; init; } = true;
        public bool AllowDownload { get; init; } = true;
        public bool AllowActiveMode { get; init; } = true;

        public bool RequireIdentMatch { get; init; }
        public string AllowedIpMask { get; init; } = string.Empty;
        public string RequiredIdent { get; init; } = string.Empty;

        /// <summary>Idle timeout in seconds (0 = use default).</summary>
        public int IdleTimeoutSeconds { get; init; }

        public int MaxUploadKbps { get; init; }
        public int MaxDownloadKbps { get; init; }

        public long CreditsKb { get; init; }

        /// <summary>
        /// Optional override for global MaxConnectionsPerIp.
        /// </summary>
        public int? MaxConnectionsPerIpOverride { get; init; }

        /// <summary>
        /// Optional override for global MaxCommandsPerMinute.
        /// </summary>
        public int? MaxCommandsPerMinuteOverride { get; init; }

        /// <summary>
        /// Optional override for global MaxFailedLoginsPerIp.
        /// </summary>
        public int? MaxFailedLoginsPerIpOverride { get; init; }

        public FtpUserConfigUser()
        {
        }

        public FtpUserConfigUser(
            string UserName,
            string PasswordHash,
            bool Disabled = false,
            string HomeDir = "",
            string GroupName = "",
            IReadOnlyList<string>? SecondaryGroups = null,
            bool IsAdmin = false,
            bool IsAdministrator = false,
            bool AllowFxp = false,
            bool AllowUpload = true,
            bool AllowDownload = true,
            bool AllowActiveMode = true,
            bool RequireIdentMatch = false,
            string? AllowedIpMask = null,
            string? RequiredIdent = null,
            int IdleTimeoutSeconds = 0,
            int MaxUploadKbps = 0,
            int MaxDownloadKbps = 0,
            long CreditsKb = 0,
            int MaxConcurrentLogins = 0)
        {
            this.UserName = UserName;
            this.PasswordHash = PasswordHash;
            this.Disabled = Disabled;
            this.HomeDir = HomeDir;
            this.GroupName = GroupName;
            this.SecondaryGroups = SecondaryGroups ?? Array.Empty<string>();
            this.IsAdmin = IsAdmin;
            this.IsAdministrator = IsAdministrator;
            this.AllowFxp = AllowFxp;
            this.AllowUpload = AllowUpload;
            this.AllowDownload = AllowDownload;
            this.AllowActiveMode = AllowActiveMode;
            this.RequireIdentMatch = RequireIdentMatch;
            this.AllowedIpMask = AllowedIpMask ?? string.Empty;
            this.RequiredIdent = RequiredIdent ?? string.Empty;
            this.IdleTimeoutSeconds = IdleTimeoutSeconds;
            this.MaxUploadKbps = MaxUploadKbps;
            this.MaxDownloadKbps = MaxDownloadKbps;
            this.CreditsKb = CreditsKb;
            this.MaxConcurrentLogins = MaxConcurrentLogins;
        }
    }
}
