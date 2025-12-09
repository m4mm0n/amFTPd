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
        /// Optional comment/description for the group. Alias for Description.
        /// </summary>
        public string Comment
        {
            get => Description;
            init => Description = value;
        }

        /// <summary>
        /// Whether this group should be treated as a siteop/admin group.
        /// </summary>
        public bool IsSiteOp { get; init; }

        /// <summary>
        /// Optional recommended maximum number of users in this group.
        /// (Not enforced unless you add logic elsewhere.)
        /// </summary>
        public int MaxUsers { get; init; }

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
