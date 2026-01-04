/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           DirectoryRuleEngine.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-28 21:43:55
 *  Last Modified:  2025-12-13 04:32:32
 *  CRC32:          0xDAAA263F
 *  
 *  Description:
 *      Provides functionality to resolve a directory rule based on a given path.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */

using amFTPd.Config.Ftpd.RatioRules;

namespace amFTPd.Core.Ratio
{
    /// <summary>
    /// Provides functionality to resolve a directory rule based on a given path.
    /// </summary>
    /// <remarks>The <see cref="DirectoryRuleEngine"/> matches a given path against a collection of predefined
    /// directory rules. Rules are evaluated in descending order of their virtual path length, ensuring the most
    /// specific match is returned.</remarks>
    public sealed class DirectoryRuleEngine
    {
        private readonly IReadOnlyDictionary<string, DirectoryRule> _rules;

        public DirectoryRuleEngine(IReadOnlyDictionary<string, DirectoryRule> rules) => _rules = rules ?? throw new ArgumentNullException(nameof(rules));

        public DirectoryRule? Resolve(string? path)
        {
            path = Normalize(path);

            return _rules.Values.OrderByDescending(r => r.VirtualPath.Length).FirstOrDefault(r =>
                path != null && path.StartsWith(r.VirtualPath, StringComparison.OrdinalIgnoreCase));
        }

        private static string? Normalize(string? p)
        {
            p = p?.Replace('\\', '/');
            if (p != null && !p.StartsWith('/'))
                p = "/" + p;
            return p;
        }
    }
}
