/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           GroupConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x1677032C
 *  
 *  Description:
 *      Represents configuration settings for a user group, including descriptive information, ratio and bonus multipliers, a...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace amFTPd.Config.Ftpd
{
    /// <summary>
    /// Represents configuration settings for a user group, including descriptive information, ratio and bonus
    /// multipliers, and optional group flags.
    /// </summary>
    /// <param name="Description">The descriptive text for the group. Provides context or details about the group's purpose or characteristics.</param>
    /// <param name="RatioMultiply">The multiplier applied to download ratios for the group. Used to adjust the cost of downloads; must be a
    /// non-negative value.</param>
    /// <param name="UploadBonus">The multiplier applied to upload bonuses for the group. Used to calculate earned credits; must be a non-negative
    /// value.</param>
    /// <param name="Flags">An optional set of group flags represented as characters. Reserved for future use; can be null or empty.</param>
    public sealed record GroupConfig
    {
        // Original properties
        public string Description { get; init; } = string.Empty;

        /// <summary>
        /// Compatibility field used by older configs where the group name is repeated on each object.
        /// </summary>
        [JsonPropertyName("GroupName")]
        public string? GroupName { get; init; }

        /// <summary>
        /// Compatibility field listing users directly on each group object.
        /// </summary>
        [JsonPropertyName("Users")]
        public IReadOnlyList<string> Users { get; init; } = [];

        /// <summary>
        /// Ratio multiplier applied to downloads (cost *= N).
        /// </summary>
        public double RatioMultiply { get; init; } = 1.0;

        /// <summary>
        /// Upload bonus multiplier (earned credits *= N).
        /// </summary>
        public double UploadBonus { get; init; } = 1.0;

        /// <summary>
        /// Optional group flags (future use).
        /// </summary>
        public ImmutableHashSet<char> Flags { get; init; } = ImmutableHashSet<char>.Empty;

        // ------------------------------------------------------------------
        // New properties used by SITE GROUPINFO / admin surface
        // ------------------------------------------------------------------

        /// <summary>
        /// Whether this group should be treated as a siteop/admin group.
        /// </summary>
        public bool IsSiteOp { get; init; }

        /// <summary>
        /// Compatibility alias for older configs using <c>IsAdminGroup</c>.
        /// </summary>
        [JsonPropertyName("IsAdminGroup")]
        public bool IsAdminGroup
        {
            get => IsSiteOp;
            init => IsSiteOp = value;
        }

        /// <summary>
        /// Optional recommended maximum number of users in this group.
        /// (Not enforced unless you add logic elsewhere.)
        /// </summary>
        public int MaxUsers { get; init; }

        /// <summary>
        /// Compatibility alias for older configs using <c>Comment</c>.
        /// </summary>
        [JsonPropertyName("Comment")]
        public string Comment
        {
            get => Description;
            init => Description = value;
        }

        // ------------------------------------------------------------------
        // Upload quotas  (0 = unlimited)
        // ------------------------------------------------------------------

        /// <summary>Maximum upload allowed per day per user in this group (MiB). 0 = no limit.</summary>
        public long DailyUploadLimitMb { get; init; }

        /// <summary>Maximum upload allowed per week (Mon–Sun) per user in this group (MiB). 0 = no limit.</summary>
        public long WeeklyUploadLimitMb { get; init; }

        /// <summary>Maximum upload allowed per calendar month per user in this group (MiB). 0 = no limit.</summary>
        public long MonthlyUploadLimitMb { get; init; }

        // ------------------------------------------------------------------
        // Constructors
        // ------------------------------------------------------------------

        public GroupConfig()
        {
        }

        // Backwards-compatible positional constructor
        public GroupConfig(
            string Description,
            double RatioMultiply,
            double UploadBonus,
            ImmutableHashSet<char> Flags)
        {
            this.Description = Description;
            this.RatioMultiply = RatioMultiply;
            this.UploadBonus = UploadBonus;
            this.Flags = Flags;
        }
    }
}
