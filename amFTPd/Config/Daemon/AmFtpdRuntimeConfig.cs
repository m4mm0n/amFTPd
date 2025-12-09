/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AmFtpdRuntimeConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-28 22:06:08
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xFF93F62D
 *  
 *  Description:
 *      Represents the runtime configuration for the amFTPd daemon.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using amFTPd.Config.Ftpd;
using amFTPd.Config.Ftpd.RatioRules;
using amFTPd.Config.Ident;
using amFTPd.Config.Irc;
using amFTPd.Config.Vfs;
using amFTPd.Core.Dupe;
using amFTPd.Core.Events;
using amFTPd.Core.Fxp;
using amFTPd.Core.Race;
using amFTPd.Core.Ratio;
using amFTPd.Core.Zipscript;
using amFTPd.Credits;
using amFTPd.Db;
using amFTPd.Security;
using SectionResolver = amFTPd.Core.Sections.SectionResolver;

namespace amFTPd.Config.Daemon
{
    /// <summary>
    /// Represents the runtime configuration for the amFTPd daemon.
    /// </summary>
    /// <remarks>
    /// This class encapsulates the configuration required for the FTP server, 
    /// including FTP settings, user store, section management, and TLS configuration.
    /// </remarks>
    public sealed class AmFtpdRuntimeConfig
    {
        public required FtpConfig FtpConfig { get; init; }
        public required IUserStore UserStore { get; init; }
        public required SectionManager Sections { get; init; }
        public required TlsConfig TlsConfig { get; init; }
        public required IdentConfig IdentConfig { get; init; }
        public required VfsConfig VfsConfig { get; init; }

        public DatabaseManager? Database { get; init; }

        // Ratio system (SectionRule-based)
        public required Dictionary<string, SectionRule> SectionRules { get; init; }
        public required Dictionary<string, DirectoryRule> DirectoryRules { get; init; }
        public required Dictionary<string, RatioRule> RatioRules { get; init; }
        public required Dictionary<string, GroupConfig> Groups { get; init; }

        public required RatioEngine RatioEngine { get; init; }
        public required RatioResolutionPipeline RatioPipeline { get; init; }
        public required DirectoryRuleEngine DirectoryRuleEngine { get; init; }
        public required RaceEngine RaceEngine { get; init; }

        // VFS system (FtpSection-based) — CORRECT resolver
        public required SectionResolver SectionResolver { get; init; }

        public IGroupStore? GroupStore { get; init; }
        public ISectionStore? SectionStore { get; init; }

        public IDupeStore? DupeStore { get; init; }
        public ZipscriptEngine? Zipscript { get; init; }

        public EventBus EventBus { get; init; } = new();

        public FxpPolicyEngine? FxpPolicy { get; init; }

        public IrcConfig? IrcConfig { get; init; }

        public CreditEngine? CreditEngine { get; init; }
    }
}
