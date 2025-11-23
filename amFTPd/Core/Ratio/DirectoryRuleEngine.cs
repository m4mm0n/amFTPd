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
    /// Provides functionality for managing and applying directory-specific rules to virtual paths, including rule
    /// lookup and rule merging operations.
    /// </summary>
    /// <remarks>Use this class to retrieve directory rules for specific virtual paths and to merge directory
    /// rules with base ratio rules. Instances are immutable after construction and thread-safe for concurrent
    /// access.</remarks>
    public sealed class DirectoryRuleEngine
    {
        private readonly Dictionary<string, DirectoryRule> _rules;
        /// <summary>
        /// Initializes a new instance of the DirectoryRuleEngine class with the specified set of directory rules.
        /// </summary>
        /// <param name="rules">A dictionary containing directory rules, keyed by rule name. If null, an empty set of rules is used.</param>
        public DirectoryRuleEngine(Dictionary<string, DirectoryRule> rules) => _rules = rules ?? new();

        /// <summary>
        /// Finds the directory rule for a given virtual path.
        /// Exact match only.
        /// </summary>
        public DirectoryRule? GetRule(string virtualPath) => _rules.TryGetValue(virtualPath, out var rule) ? rule : null;

        /// <summary>
        /// Merges a directory rule with a base RatioRule (section/global).
        /// </summary>
        public RatioRule Merge(RatioRule baseRule, DirectoryRule? overrideRule) =>
            overrideRule is null
                ? baseRule
                : new RatioRule(
                    Ratio: overrideRule.Ratio ?? baseRule.Ratio,
                    IsFree: overrideRule.IsFree ?? baseRule.IsFree,
                    MultiplyCost: overrideRule.MultiplyCost ?? baseRule.MultiplyCost,
                    UploadBonus: overrideRule.UploadBonus ?? baseRule.UploadBonus
                );
    }
}
