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

using amFTPd.Config.Ftpd.RatioRules;

namespace amFTPd.Core.Ratio
{
    /// <summary>
    /// Provides functionality to resolve the most specific section rule for a given virtual path based on prefix
    /// matching.
    /// </summary>
    /// <remarks>SectionResolver selects the section rule whose VirtualRoot is the longest prefix of the
    /// specified path, enabling hierarchical configuration or routing scenarios. This class is sealed and intended for
    /// use where section rules are organized by virtual root prefixes.</remarks>
    public sealed class SectionResolver
    {
        private readonly Dictionary<string, SectionRule> _sections;
        /// <summary>
        /// Initializes a new instance of the SectionResolver class using the specified section rules.
        /// </summary>
        /// <param name="sections">A dictionary containing section names mapped to their corresponding rules. If null, an empty dictionary is
        /// used.</param>
        public SectionResolver(Dictionary<string, SectionRule> sections) => _sections = sections ?? new();

        /// <summary>
        /// Returns the section rule whose VirtualRoot is a prefix of the path.
        /// Longest prefix wins.
        /// </summary>
        public SectionRule? Resolve(string virtualPath)
        {
            SectionRule? best = null;
            var bestLen = -1;

            foreach (var sec in _sections.Values
                         .Where(sec => virtualPath.StartsWith(sec.VirtualRoot, StringComparison.OrdinalIgnoreCase))
                         .Where(sec => sec.VirtualRoot.Length > bestLen))
            {
                best = sec;
                bestLen = sec.VirtualRoot.Length;
            }

            return best;
        }
    }
}
