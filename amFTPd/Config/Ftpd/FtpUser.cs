namespace amFTPd.Config.Ftpd
{
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
