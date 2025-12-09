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

using System.Net;

namespace amFTPd.Config.Ftpd
{
    /// <summary>
    /// Top-level FTP configuration.
    /// </summary>
    public enum DataChannelProtectionLevel
    {
        Clear,
        Safe,
        Confidential,
        Private
    }
    /// <summary>
    /// Represents the configuration settings for an FTP server.
    /// </summary>
    /// <remarks>This class provides a comprehensive set of options for configuring an FTP server, including
    /// network settings, authentication requirements, transfer modes, and user/section configurations. It supports both
    /// default values and customization through named arguments in the constructor.</remarks>
    public sealed record FtpConfig
    {
        /// <summary>
        /// Optional simple user -> home directory mapping for quick in-memory setups.
        /// </summary>
        public IReadOnlyDictionary<string, string> HomeDirs { get; init; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Optional IP/interface to bind to. Null/empty = all.</summary>
        public string? BindAddress { get; init; }

        /// <summary>TCP port for control connection.</summary>
        public int Port { get; init; } = 21;

        /// <summary>Maximum simultaneous logins.</summary>
        public int MaxClients { get; init; } = 100;

        /// <summary>Root local path the server uses as base.</summary>
        public string RootPath { get; init; } = "/";

        /// <summary>Passive port range specification (e.g. "50000-50100").</summary>
        public string? PassivePorts { get; init; }

        /// <summary>If true, TLS is required for AUTH (no clear-text logins).</summary>
        public bool RequireTlsForAuth { get; init; }

        /// <summary>If true, anonymous logins are allowed.</summary>
        public bool AllowAnonymous { get; init; }

        /// <summary>If true, FXP (server to server) transfers are allowed.</summary>
        public bool AllowFxp { get; init; }

        /// <summary>If true, PORT/active mode is allowed.</summary>
        public bool AllowActiveMode { get; init; } = true;

        /// <summary>If true, explicit TLS (AUTH TLS) is enabled.</summary>
        public bool EnableExplicitTls { get; init; }

        /// <summary>Default data channel protection level (PROT).</summary>
        public DataChannelProtectionLevel DataChannelProtectionDefault { get; init; }
            = DataChannelProtectionLevel.Clear;

        /// <summary>Default nuke multiplier if section doesn't override.</summary>
        public int DefaultNukeMultiplier { get; init; } = 1;

        /// <summary>User database.</summary>
        public FtpUserConfig UserConfig { get; init; } = FtpUserConfig.Empty;

        /// <summary>Section configuration.</summary>
        public FtpSectionConfig SectionConfig { get; init; } = FtpSectionConfig.Empty;

        /// <summary>Whether SSL/TLS is enabled at all.</summary>
        public bool EnableSsl { get; init; }

        /// <summary>Banner sent right after connection.</summary>
        public string? BannerMessage { get; init; }

        /// <summary>Welcome message on successful login.</summary>
        public string? WelcomeMessage { get; init; }

        public FtpConfig()
        {
        }

        /// <summary>
        /// Compatibility ctor – supports all named arguments seen in error list.
        /// Callers may use any subset of named args.
        /// </summary>
        public FtpConfig(
            string? BindAddress = null,
            int Port = 21,
            int MaxClients = 100,
            FtpUserConfig? UserConfig = null,
            FtpSectionConfig? SectionConfig = null,
            bool EnableSsl = false,
            string? BannerMessage = null,
            string RootPath = "/",
            string? PassivePorts = null,
            bool RequireTlsForAuth = false,
            bool AllowAnonymous = false,
            bool AllowFxp = false,
            bool AllowActiveMode = true,
            bool EnableExplicitTls = false,
            int DefaultNukeMultiplier = 1,
            DataChannelProtectionLevel DataChannelProtectionDefault = DataChannelProtectionLevel.Clear,
            string? WelcomeMessage = null,
            IReadOnlyDictionary<string, string>? HomeDirs = null)
        {
            this.BindAddress = BindAddress;
            this.Port = Port;
            this.MaxClients = MaxClients;
            this.UserConfig = UserConfig ?? FtpUserConfig.Empty;
            this.SectionConfig = SectionConfig ?? FtpSectionConfig.Empty;
            this.EnableSsl = EnableSsl;
            this.BannerMessage = BannerMessage;
            this.RootPath = RootPath;
            this.PassivePorts = PassivePorts;
            this.RequireTlsForAuth = RequireTlsForAuth;
            this.AllowAnonymous = AllowAnonymous;
            this.AllowFxp = AllowFxp;
            this.AllowActiveMode = AllowActiveMode;
            this.EnableExplicitTls = EnableExplicitTls;
            this.DefaultNukeMultiplier = DefaultNukeMultiplier;
            this.DataChannelProtectionDefault = DataChannelProtectionDefault;
            this.WelcomeMessage = WelcomeMessage;
            this.HomeDirs = HomeDirs ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
