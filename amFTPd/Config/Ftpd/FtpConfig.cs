using System.Net;

namespace amFTPd.Config.Ftpd
{
    /// <summary>
    /// Represents the configuration settings for an FTP server.
    /// </summary>
    /// <remarks>This record encapsulates various parameters required to configure an FTP server, including
    /// network settings, authentication policies, TLS requirements, and directory mappings. It is designed to provide a
    /// comprehensive and immutable configuration for initializing and managing an FTP server instance.</remarks>
    /// <param name="BindAddress"></param>
    /// <param name="Port"></param>
    /// <param name="PassivePorts"></param>
    /// <param name="RootPath"></param>
    /// <param name="WelcomeMessage"></param>
    /// <param name="AllowAnonymous"></param>
    /// <param name="HomeDirs"></param>
    /// <param name="EnableExplicitTls"></param>
    /// <param name="RequireTlsForAuth"></param>
    /// <param name="DataChannelProtectionDefault"></param>
    /// <param name="AllowActiveMode"></param>
    /// <param name="AllowFxp"></param>
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
