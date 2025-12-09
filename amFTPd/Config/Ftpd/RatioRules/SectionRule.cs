/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SectionRule.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x06BFC9B6
 *  
 *  Description:
 *      Connects a logical section to a particular <see cref="RatioRule"/>. Example: Section "0DAY" uses RatioRule "STANDARD".
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





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
