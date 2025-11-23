/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-22
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
    /// Represents configuration settings for a user group, including descriptive information, ratio and bonus
    /// multipliers, and optional group flags.
    /// </summary>
    /// <param name="Description">The descriptive text for the group. Provides context or details about the group's purpose or characteristics.</param>
    /// <param name="RatioMultiply">The multiplier applied to download ratios for the group. Used to adjust the cost of downloads; must be a
    /// non-negative value.</param>
    /// <param name="UploadBonus">The multiplier applied to upload bonuses for the group. Used to calculate earned credits; must be a non-negative
    /// value.</param>
    /// <param name="Flags">An optional set of group flags represented as characters. Reserved for future use; can be null or empty.</param>
    public sealed record GroupConfig(
        string Description,

        // Ratio multiplier applied to downloads (cost *= N)
        double RatioMultiply,

        // Upload bonus multiplier (earned credits *= N)
        double UploadBonus,

        // Optional group flags (future use)
        ImmutableHashSet<char> Flags
    );
}
