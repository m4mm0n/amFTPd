/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-22
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

using System.Collections.Immutable;

namespace amFTPd.Config.Ftpd
{
    /// <summary>
    /// Represents an FTP user account, including authentication credentials, permissions, group memberships, transfer
    /// limits, and access restrictions.
    /// </summary>
    /// <remarks>This record encapsulates all configuration and access control information for an FTP user.
    /// Group memberships, permissions, and transfer limits are enforced according to the properties specified. User
    /// flags in <paramref name="FlagsRaw"/> may enable additional features such as VIP status or site operator
    /// privileges. Thread safety is guaranteed for immutable properties.</remarks>
    /// <param name="UserName">The unique user name used to identify and authenticate the FTP user. Cannot be null or empty.</param>
    /// <param name="PasswordHash">The hashed password for the user account. Used for authentication; must be a valid hash string.</param>
    /// <param name="HomeDir">The root directory assigned to the user. All file operations are relative to this directory. Cannot be null or
    /// empty.</param>
    /// <param name="IsAdmin">Indicates whether the user has administrative privileges. If <see langword="true"/>, the user can perform
    /// administrative actions.</param>
    /// <param name="AllowFxp">Indicates whether the user is permitted to use FXP (server-to-server file transfers).</param>
    /// <param name="AllowUpload">Indicates whether the user is allowed to upload files to the server.</param>
    /// <param name="AllowDownload">Indicates whether the user is allowed to download files from the server.</param>
    /// <param name="AllowActiveMode">Indicates whether the user is permitted to use active FTP mode for data transfers.</param>
    /// <param name="MaxConcurrentLogins">The maximum number of concurrent sessions allowed for this user. Must be greater than zero.</param>
    /// <param name="IdleTimeout">The maximum duration the user session can remain idle before being disconnected. Specified as a <see
    /// cref="TimeSpan"/>.</param>
    /// <param name="MaxUploadKbps">The maximum upload speed allowed for the user, in kilobits per second. Must be zero or positive; zero means
    /// unlimited.</param>
    /// <param name="MaxDownloadKbps">The maximum download speed allowed for the user, in kilobits per second. Must be zero or positive; zero means
    /// unlimited.</param>
    /// <param name="PrimaryGroup">The primary group name assigned to the user. Used for group-based permissions and access control. Cannot be null
    /// or empty.</param>
    /// <param name="SecondaryGroups">A collection of additional group names the user belongs to, in addition to the primary group. May be empty if
    /// the user has no secondary groups.</param>
    /// <param name="CreditsKb">The number of credits available to the user, in kilobytes. Used to limit or track data transfer quotas.</param>
    /// <param name="AllowedIpMask">An optional IP address mask specifying which client IPs are allowed to connect as this user. If null, no IP
    /// restriction is enforced.</param>
    /// <param name="RequireIdentMatch">Indicates whether IDENT protocol enforcement is required for this user. If <see langword="true"/>, the user's
    /// IDENT must match <paramref name="RequiredIdent"/>.</param>
    /// <param name="RequiredIdent">The IDENT string that must match for the user to log in, if <paramref name="RequireIdentMatch"/> is <see
    /// langword="true"/>. If null, no IDENT enforcement is applied.</param>
    /// <param name="FlagsRaw">A serialized string containing user-specific flags. Each character represents a distinct flag that controls user
    /// features or restrictions.</param>
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

        // NEW — Primary group (replaces original GroupName)
        string? PrimaryGroup,

        // NEW — Secondary groups
        ImmutableArray<string> SecondaryGroups,

        // Credits (existing)
        long CreditsKb,

        // IP mask (existing)
        string? AllowedIpMask,

        // IDENT enforcement (existing)
        bool RequireIdentMatch,
        string? RequiredIdent,

        // NEW — user flags (serialized string)
        string FlagsRaw
    )
    {
        /// <summary>
        /// Gets the set of flag characters associated with this instance.
        /// </summary>
        /// <remarks>The returned set is immutable and will be empty if no flags are defined. This
        /// property is initialized during object construction and cannot be modified after initialization.</remarks>
        public ImmutableHashSet<char> Flags { get; init; } =
            FlagsRaw is null
                ? ImmutableHashSet<char>.Empty
                : FlagsRaw.ToCharArray().ToImmutableHashSet();
        /// <summary>
        /// Determines whether the specified flag character is present in the set of flags.
        /// </summary>
        /// <param name="flag">The flag character to check for presence. The comparison is case-insensitive; both uppercase and lowercase
        /// characters are treated equivalently.</param>
        /// <returns>true if the specified flag is present; otherwise, false.</returns>
        public bool HasFlag(char flag)
            => Flags.Contains(char.ToUpperInvariant(flag));
        /// <summary>
        /// Gets a value indicating whether the user has VIP status.
        /// </summary>
        public bool IsVip => HasFlag('V');
        /// <summary>
        /// Gets a value indicating whether the user has site operator privileges.
        /// </summary>
        public bool IsSiteOp => HasFlag('S');
        /// <summary>
        /// Gets a value indicating whether the current instance represents a master entity.
        /// </summary>
        public bool IsMaster => HasFlag('M');
        /// <summary>
        /// Gets a value indicating whether the no-ratio flag is set for this instance.
        /// </summary>
        public bool IsNoRatio => HasFlag('1');
        /// <summary>
        /// Gets a value indicating whether the item is marked as hidden.
        /// </summary>
        public bool IsHidden => HasFlag('H');
        /// <summary>
        /// Gets a value indicating whether the user is immune to being kicked from the server.
        /// </summary>
        public bool IsKickImmune => HasFlag('Z');
        /// <summary>
        /// Gets a value indicating whether the user is required to use TLS for authentication.
        /// </summary>
        public bool IsRequireTlsUser => HasFlag('R');

        /// <summary>
        /// Returns all groups user belongs to.
        /// Primary + secondary combined.
        /// </summary>
        public IEnumerable<string> AllGroups
            => SecondaryGroups.Insert(0, PrimaryGroup ?? string.Empty);

        /// <summary>
        /// Gets the primary group name assigned to the user.
        /// </summary>
        public string? GroupName => PrimaryGroup;

        /// <summary>
        /// Returns true if the user belongs to the specified group (primary or secondary).
        /// </summary>
        public bool IsInGroup(string group) => !string.IsNullOrWhiteSpace(group) &&
                                               AllGroups.Any(g =>
                                                   string.Equals(g, group, StringComparison.OrdinalIgnoreCase));
    }
}
