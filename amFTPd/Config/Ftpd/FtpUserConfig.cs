/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpUserConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-13 04:32:32
 *  CRC32:          0xB0DC912D
 *  
 *  Description:
 *      Container for configured users + helper to build runtime FtpUser objects.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







namespace amFTPd.Config.Ftpd;

/// <summary>
/// Container for configured users + helper to build runtime FtpUser objects.
/// </summary>
public sealed record FtpUserConfig
{
    public static FtpUserConfig Empty { get; } = new();

    public IReadOnlyList<FtpUserConfigUser> Users { get; init; }
        = [];

    public FtpUserConfig()
    {
    }

    public FtpUserConfig(IReadOnlyList<FtpUserConfigUser> users)
    {
        Users = users ?? [];
    }

    public FtpUserConfigUser? FindConfigUser(string username)
        => Users.FirstOrDefault(u =>
            string.Equals(u.UserName, username, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Build a runtime <see cref="FtpUser"/> from a config entry.
    /// </summary>
    public static FtpUser ToRuntimeUser(
        FtpUserConfigUser cfg,
        IReadOnlyList<FtpSection> sections,
        string defaultHomeDir)
    {
        if (cfg is null) throw new ArgumentNullException(nameof(cfg));

        var idleTimeout =
            cfg.IdleTimeoutSeconds > 0
                ? TimeSpan.FromSeconds(cfg.IdleTimeoutSeconds)
                : (TimeSpan?)null;

        var homeDir =
            !string.IsNullOrWhiteSpace(cfg.HomeDir)
                ? cfg.HomeDir
                : defaultHomeDir;

        return new FtpUser(
            UserName: cfg.UserName,
            PasswordHash: cfg.PasswordHash,
            Disabled: cfg.Disabled,
            HomeDir: homeDir,
            PrimaryGroup: cfg.GroupName,
            SecondaryGroups: cfg.SecondaryGroups ?? [],
            IsAdmin: cfg.IsAdmin || cfg.IsAdministrator,
            AllowFxp: cfg.AllowFxp,
            AllowUpload: cfg.AllowUpload,
            AllowDownload: cfg.AllowDownload,
            AllowActiveMode: cfg.AllowActiveMode,
            RequireIdentMatch: cfg.RequireIdentMatch,
            AllowedIpMask: cfg.AllowedIpMask,
            RequiredIdent: cfg.RequiredIdent,
            IdleTimeout: idleTimeout,
            MaxUploadKbps: cfg.MaxUploadKbps,
            MaxDownloadKbps: cfg.MaxDownloadKbps,
            CreditsKb: cfg.CreditsKb,
            Sections: sections,
            MaxConcurrentLogins: cfg.MaxConcurrentLogins);
    }
}