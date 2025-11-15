namespace amFTPd.Config.Ftpd
{
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
