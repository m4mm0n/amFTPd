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

namespace amFTPd.Config.Ftpd.RatioRules
{
    /// <summary>
    /// Represents a rule that defines properties and multipliers for a specific section within a virtual hierarchy.
    /// </summary>
    /// <param name="SectionName">The name of the section to which this rule applies.</param>
    /// <param name="VirtualRoot">The virtual root path associated with the section. This typically identifies the section's location within a
    /// hierarchy.</param>
    /// <param name="Ratio">The default ratio value for the section, used to determine standard calculations or limits.</param>
    /// <param name="IsFree">Indicates whether the section is designated as free-leech. Set to <see langword="true"/> if downloads in this
    /// section do not count against user quotas; otherwise, <see langword="false"/>.</param>
    /// <param name="MultiplyCost">The multiplier applied to costs within this section. Used to adjust cost calculations based on section-specific
    /// rules.</param>
    /// <param name="UploadBonus">The bonus multiplier applied to uploads in this section. Used to increase upload credit or rewards according to
    /// section policy.</param>
    public sealed record SectionRule(
        string SectionName,
        string VirtualRoot,

        // Default ratio for this section
        double Ratio,

        // Free-leech section?
        bool IsFree,

        // Multipliers
        double MultiplyCost,
        double UploadBonus
    );
}
