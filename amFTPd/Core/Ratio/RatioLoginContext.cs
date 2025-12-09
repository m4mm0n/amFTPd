/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           RatioLoginContext.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-05 04:00:10
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x06ABA1DD
 *  
 *  Description:
 *      Context for ratio/login rules when a user logs in.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







using amFTPd.Config.Ftpd;

namespace amFTPd.Core.Ratio
{
    /// <summary>
    /// Context for ratio/login rules when a user logs in.
    /// </summary>
    public sealed record RatioLoginContext
    {
        /// <summary>Username.</summary>
        public string Username { get; init; } = string.Empty;

        /// <summary>Compatibility alias.</summary>
        public string UserName
        {
            get => Username;
            init => Username = value;
        }

        /// <summary>User's primary group name.</summary>
        public string GroupName { get; init; } = string.Empty;

        /// <summary>Remote endpoint (IP/host).</summary>
        public string RemoteAddress { get; init; } = string.Empty;

        /// <summary>Compatibility alias for scripts.</summary>
        public string RemoteHost
        {
            get => RemoteAddress;
            init => RemoteAddress = value;
        }

        /// <summary>UTC timestamp for login.</summary>
        public DateTime NowUtc { get; init; } = DateTime.UtcNow;

        /// <summary>Real name / GECOS / comment if available.</summary>
        public string RealName { get; init; } = string.Empty;

        /// <summary>True if anonymous/guest.</summary>
        public bool IsAnonymous { get; init; }

        /// <summary>Resolved user object.</summary>
        public FtpUser? User { get; init; }
    }
}
