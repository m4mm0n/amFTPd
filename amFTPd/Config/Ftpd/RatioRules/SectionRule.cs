/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-22
 *  Last Modified:  2025-11-28
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
    /// Connects a logical section to a particular <see cref="RatioRule"/>.
    /// Example: Section "0DAY" uses RatioRule "STANDARD".
    /// </summary>
    public sealed record SectionRule
    {
        /// <summary>
        /// Section name, e.g. "0DAY".
        /// </summary>
        public string SectionName { get; init; } = string.Empty;

        /// <summary>
        /// Associated ratio rule name, e.g. "STANDARD" or "VIP".
        /// </summary>
        public string RatioRuleName { get; init; } = string.Empty;

        /// <summary>
        /// Whether this mapping is enabled.
        /// </summary>
        public bool Enabled { get; init; } = true;
    }
}
