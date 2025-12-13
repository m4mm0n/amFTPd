/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpUser.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-13 04:39:15
 *  CRC32:          0x90403351
 *  
 *  Description:
 *      Runtime FTP user model, shaped to match how the stores/loaders construct it.
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
    /// Runtime FTP user model, shaped to match how the stores/loaders construct it.
    /// </summary>
    public sealed record FtpUser
    {
        public string UserName { get; init; } = string.Empty;

        /// <summary>Compatibility alias if any code uses "Username".</summary>
        public string Username
        {
            get => UserName;
            init => UserName = value;
        }

        public string PasswordHash { get; init; } = string.Empty;

        public bool Disabled { get; init; }

        /// <summary>Physical home directory for this user.</summary>
        public string? HomeDir { get; init; } = string.Empty;

        /// <summary>Primary group name.</summary>
        public string? PrimaryGroup { get; init; } = string.Empty;

        /// <summary>Compatibility alias for older code using GroupName.</summary>
        public string? GroupName
        {
            get => PrimaryGroup;
            init => PrimaryGroup = value;
        }

        /// <summary>Secondary groups.</summary>
        public IReadOnlyList<string?> SecondaryGroups { get; init; }
            = [];

        public bool IsAdmin { get; init; }

        public bool IsSiteop { get; init; }

        public bool AllowFxp { get; init; }
        public bool AllowUpload { get; init; } = true;
        public bool AllowDownload { get; init; } = true;
        public bool AllowActiveMode { get; init; } = true;

        public bool RequireIdentMatch { get; init; }
        public string AllowedIpMask { get; init; } = string.Empty;
        public string RequiredIdent { get; init; } = string.Empty;

        /// <summary>
        /// Per-user idle timeout; null means "use server default".
        /// </summary>
        public TimeSpan? IdleTimeout { get; init; }

        public int MaxUploadKbps { get; init; }
        public int MaxDownloadKbps { get; init; }

        /// <summary>User credits in KiB.</summary>
        public long CreditsKb { get; init; }

        public int MaxConcurrentLogins { get; init; }

        public bool IsNoRatio { get; init; }

        public string FlagsRaw { get; init; } = string.Empty;

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

        public IReadOnlyList<string?> AllGroups
        {
            get
            {
                if (string.IsNullOrEmpty(PrimaryGroup) && (SecondaryGroups == null || SecondaryGroups.Count == 0))
                    return [];

                if (SecondaryGroups == null || SecondaryGroups.Count == 0)
                    return [PrimaryGroup];

                var all = new List<string>(1 + SecondaryGroups.Count);
                if (!string.IsNullOrEmpty(PrimaryGroup))
                    all.Add(PrimaryGroup);
                all.AddRange(SecondaryGroups!);
                return all;
            }
        }


        /// <summary>
        /// Sections this user can see/use. The semantics depend on real code,
        /// but the type is what the stores already pass in.
        /// </summary>
        public IReadOnlyList<FtpSection> Sections { get; init; }
            = [];

        public FtpUser()
        {
        }

        /// <summary>
        /// Main ctor used by BinaryUserStore, InMemoryUserStore, DatabaseManager, etc.
        /// Uses named arguments like "PrimaryGroup".
        /// </summary>
        public FtpUser(
            string UserName,
            string PasswordHash,
            bool Disabled,
            string? HomeDir,
            string? PrimaryGroup,
            IReadOnlyList<string?> SecondaryGroups,
            bool IsAdmin,
            bool AllowFxp,
            bool AllowUpload,
            bool AllowDownload,
            bool AllowActiveMode,
            bool RequireIdentMatch,
            string AllowedIpMask,
            string RequiredIdent,
            TimeSpan? IdleTimeout,
            int MaxUploadKbps,
            int MaxDownloadKbps,
            long CreditsKb,
            IReadOnlyList<FtpSection> Sections,
            int MaxConcurrentLogins = 0,
            bool IsNoRatio = false,
            string? FlagsRaw = null)
        {
            this.UserName = UserName;
            this.PasswordHash = PasswordHash;
            this.Disabled = Disabled;
            this.HomeDir = HomeDir;
            this.PrimaryGroup = PrimaryGroup;
            this.SecondaryGroups = SecondaryGroups ?? [];
            this.IsAdmin = IsAdmin;
            this.AllowFxp = AllowFxp;
            this.AllowUpload = AllowUpload;
            this.AllowDownload = AllowDownload;
            this.AllowActiveMode = AllowActiveMode;
            this.RequireIdentMatch = RequireIdentMatch;
            this.AllowedIpMask = AllowedIpMask;
            this.RequiredIdent = RequiredIdent;
            this.IdleTimeout = IdleTimeout;
            this.MaxUploadKbps = MaxUploadKbps;
            this.MaxDownloadKbps = MaxDownloadKbps;
            this.CreditsKb = CreditsKb;
            this.Sections = Sections ?? [];
            this.MaxConcurrentLogins = MaxConcurrentLogins;
            this.IsNoRatio = IsNoRatio;
            this.FlagsRaw = FlagsRaw ?? string.Empty;
        }

        public FtpUser(
            string UserName,
            string PasswordHash,
            string HomeDir,
            string? PrimaryGroup,
            IReadOnlyList<string> SecondaryGroups,
            bool IsAdmin,
            bool AllowFxp,
            bool AllowUpload,
            bool AllowDownload,
            bool AllowActiveMode,
            bool RequireIdentMatch,
            string? AllowedIpMask,
            string? RequiredIdent,
            TimeSpan? IdleTimeout,
            int MaxUploadKbps,
            int MaxDownloadKbps,
            long CreditsKb,
            int MaxConcurrentLogins = 0,
            bool IsNoRatio = false,
            string? FlagsRaw = null)
            : this(
                UserName: UserName,
                PasswordHash: PasswordHash,
                Disabled: false,                            // default
                HomeDir: HomeDir,
                PrimaryGroup: PrimaryGroup,
                SecondaryGroups: SecondaryGroups,
                IsAdmin: IsAdmin,
                AllowFxp: AllowFxp,
                AllowUpload: AllowUpload,
                AllowDownload: AllowDownload,
                AllowActiveMode: AllowActiveMode,
                RequireIdentMatch: RequireIdentMatch,
                AllowedIpMask: AllowedIpMask ?? string.Empty,
                RequiredIdent: RequiredIdent ?? string.Empty,
                IdleTimeout: IdleTimeout,
                MaxUploadKbps: MaxUploadKbps,
                MaxDownloadKbps: MaxDownloadKbps,
                CreditsKb: CreditsKb,
                Sections: [],        // default: no sections
                MaxConcurrentLogins: MaxConcurrentLogins,
                IsNoRatio: IsNoRatio,
                FlagsRaw: FlagsRaw)
        {
        }
    }
}
