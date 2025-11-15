namespace amFTPd.Config.Daemon
{
    public sealed record AmFtpdServerConfig(
        string BindAddress,
        int Port,
        int PassivePortStart,
        int PassivePortEnd,
        string RootPath,
        string WelcomeMessage,
        bool AllowAnonymous,
        bool RequireTlsForAuth,
        string DataChannelProtectionDefault,   // "C" or "P"
        bool AllowActiveMode,
        bool AllowFxp
    );
}
