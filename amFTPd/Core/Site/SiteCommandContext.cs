/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteCommandContext.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-24 23:59:17
 *  Last Modified:  2025-12-14 21:26:07
 *  CRC32:          0x9E6F581D
 *  
 *  Description:
 *      Context passed to all SITE commands.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

using amFTPd.Config.Daemon;
using amFTPd.Config.Ftpd;
using amFTPd.Core.Monitoring;
using amFTPd.Core.Race;
using amFTPd.Core.Scene;
using amFTPd.Core.Services;
using amFTPd.Core.Stats;
using amFTPd.Credits;
using amFTPd.Db;
using amFTPd.Logging;

namespace amFTPd.Core.Site
{
    /// <summary>
    /// Canonical context object passed to all SITE commands.
    ///
    /// This context represents the complete execution environment for a SITE command,
    /// including identity, authorization, runtime state, filesystem access, and
    /// controlled service capabilities.
    ///
    /// SITE commands and SITE scripts MUST rely exclusively on this context and must
    /// not access server internals, singletons, or global state directly.
    /// </summary>
    public sealed class SiteCommandContext
    {
        // =====================================================================
        // Core routing / session
        // =====================================================================

        public FtpCommandRouter Router { get; }

        public FtpSession Session => Router.Session;

        public FtpServer Server => Router.Server;

        public IFtpLogger Log => Router.Log;

        // =====================================================================
        // Identity & authorization
        // =====================================================================

        public FtpUser? User => Session.Account;

        public string? PrimaryGroup => Session.Account?.PrimaryGroup;

        public IReadOnlyList<string?> SecondaryGroups =>
            Session.Account?.SecondaryGroups ?? [];

        public bool IsAuthenticated => Session.Account is not null;

        /// <summary>
        /// SITEOP semantics: explicit SITEOP or admin implies SITEOP.
        /// </summary>
        public bool IsSiteop =>
            Session.Account?.IsSiteop == true ||
            Session.Account?.IsAdmin == true;

        public string IpAddress => Session.RemoteEndPoint.Address.ToString();

        // =====================================================================
        // Runtime truth & observability
        // =====================================================================

        /// <summary>
        /// Authoritative runtime statistics snapshot.
        /// This is the ONLY approved runtime truth source for SITE logic.
        /// </summary>
        public StatsSnapshot Stats => Router.StatsSnapshot;

        public StatusEndpoint? StatusEndpoint => Router.StatusEndpoint;

        // =====================================================================
        // Virtual filesystem & scene structure (transitional)
        // =====================================================================

        /// <remarks>
        /// Transitional access. Future SITE logic should prefer higher-level
        /// abstractions and services.
        /// </remarks>
        public FtpFileSystem FileSystem => Router.FileSystem;

        public SectionManager Sections => Router.Sections;

        // =====================================================================
        // Persistence & configuration
        // =====================================================================

        public IUserStore Users => Router.Users;

        public IGroupStore Groups => Router.Groups;

        public AmFtpdRuntimeConfig Runtime => Router.Runtime;

        public DatabaseManager? Database => Router.Runtime.Database;

        // =====================================================================
        // Scene engines (legacy / transitional)
        // =====================================================================

        public ICreditService CreditService => Router.CreditService;

        public RaceEngine RaceEngine => Router.RaceEngine;

        public SceneStateRegistry SceneRegistry => Router.Server.SceneRegistry;

        // =====================================================================
        // SITE command invocation data
        // =====================================================================

        public string Verb { get; }

        public string Arguments { get; }

        // =====================================================================
        // Construction
        // =====================================================================

        public SiteCommandContext(FtpCommandRouter router)
            : this(router, string.Empty, string.Empty)
        {
        }

        public SiteCommandContext(FtpCommandRouter router, string verb, string arguments)
        {
            Router = router ?? throw new ArgumentNullException(nameof(router));
            Verb = verb ?? string.Empty;
            Arguments = arguments ?? string.Empty;
        }
    }
}
