/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SectionResolver.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-28 21:15:25
 *  Last Modified:  2025-12-13 04:32:32
 *  CRC32:          0xC30AD8EE
 *  
 *  Description:
 *      Resolves which ratio rule applies for a given path.
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
    /// Resolves which ratio rule applies for a given path.
    /// </summary>
    public sealed class SectionResolver
    {
        public IReadOnlyList<DirectoryRule> DirectoryRules { get; }
        public IReadOnlyList<SectionRule> SectionRules { get; }
        public IReadOnlyList<RatioRule> RatioRules { get; }

        public SectionResolver(
            IReadOnlyList<DirectoryRule> directoryRules,
            IReadOnlyList<SectionRule> sectionRules,
            IReadOnlyList<RatioRule> ratioRules)
        {
            DirectoryRules = directoryRules ?? [];
            SectionRules = sectionRules ?? [];
            RatioRules = ratioRules ?? [];
        }

        /// <summary>
        /// Backwards-compatible ctor for (dirRules, ratioRules).
        /// </summary>
        public SectionResolver(
            IReadOnlyList<DirectoryRule> directoryRules,
            IReadOnlyList<RatioRule> ratioRules)
            : this(directoryRules, [], ratioRules)
        {
        }

        /// <summary>
        /// Backwards-compatible ctor for dictionary of section rules.
        /// </summary>
        public SectionResolver(IDictionary<string, SectionRule> sectionRules)
            : this([], sectionRules.Values.ToList(), [])
        {
        }

        /// <summary>
        /// Resolve the effective ratio rule for a given path.
        /// </summary>
        /// <returns>The rule, or null if none.</returns>
        public RatioRule? Resolve(string? virtualPath)
        {
            string? section = null;

            var match = DirectoryRules
                .Where(r => r.Enabled && r.IsMatch(virtualPath))
                .OrderByDescending(r => r.PathPrefix.Length)
                .FirstOrDefault();

            if (match is not null)
                section = match.SectionName;

            if (string.IsNullOrWhiteSpace(section))
                return null;

            var sectionRule = SectionRules
                .Where(sr => sr.Enabled)
                .FirstOrDefault(sr =>
                    string.Equals(sr.SectionName, section, StringComparison.OrdinalIgnoreCase));

            if (sectionRule is null)
                return null;

            var ratioRule = RatioRules.FirstOrDefault(rr =>
                string.Equals(rr.Name, sectionRule.RatioRuleName, StringComparison.OrdinalIgnoreCase));

            return ratioRule;
        }
    }
}
