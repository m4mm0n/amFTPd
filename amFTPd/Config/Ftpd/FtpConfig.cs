using System.Net;

namespace amFTPd.Config.Ftpd
{
    public sealed record FtpConfig(
        IPAddress BindAddress,
        int Port,
        (int Start, int End) PassivePorts,
        string RootPath,
        string WelcomeMessage,
        bool AllowAnonymous,
        IReadOnlyDictionary<string, string> HomeDirs,
        bool EnableExplicitTls,
        bool RequireTlsForAuth,
        string DataChannelProtectionDefault,   // "C" or "P"
        bool AllowActiveMode = true,          // default global policy
        bool AllowFxp = false          // default global FXP policy
    );
}
