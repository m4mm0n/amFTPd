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
    /// Provides a pipeline for resolving the effective ratio rule for a given virtual path, using directory overrides,
    /// section rules, path-based rules, and a global default.
    /// </summary>
    /// <remarks>Resolution is performed in priority order: directory-specific overrides are checked first,
    /// followed by section rules, then path-based rules from the provided dictionary, and finally the global default
    /// rule if no match is found. This class is typically used to centralize ratio rule logic for virtual paths in
    /// systems where multiple sources of configuration may apply.</remarks>
    public sealed class RatioResolutionPipeline
    {
        private readonly DirectoryRuleEngine _dirEngine;
        private readonly SectionResolver _sectionResolver;
        private readonly Dictionary<string, RatioRule> _ratioRules;
        /// <summary>
        /// Initializes a new instance of the RatioResolutionPipeline class with the specified directory rule engine,
        /// section resolver, and ratio rules.
        /// </summary>
        /// <param name="dirEngine">The directory rule engine used to evaluate directory-based rules during ratio resolution. Cannot be null.</param>
        /// <param name="sectionResolver">The section resolver responsible for determining applicable sections for ratio calculations. Cannot be null.</param>
        /// <param name="ratioRules">A dictionary containing ratio rules, keyed by rule name, that define how ratios are resolved. Cannot be
        /// null.</param>
        public RatioResolutionPipeline(
            DirectoryRuleEngine dirEngine,
            SectionResolver sectionResolver,
            Dictionary<string, RatioRule> ratioRules)
        {
            _dirEngine = dirEngine;
            _sectionResolver = sectionResolver;
            _ratioRules = ratioRules;
        }

        /// <summary>
        /// Resolves the effective ratio rule using:
        ///  1. Directory override
        ///  2. Section rule
        ///  3. RatioRules dictionary
        ///  4. Global default
        /// </summary>
        public RatioRule Resolve(string virtualPath)
        {
            // level 1: directory override
            var dir = _dirEngine.GetRule(virtualPath);
            if (dir is not null)
            {
                return new RatioRule(
                    Ratio: dir.Ratio ?? 1.0,
                    IsFree: dir.IsFree ?? false,
                    MultiplyCost: dir.MultiplyCost ?? 1.0,
                    UploadBonus: dir.UploadBonus ?? 1.0
                );
            }

            // level 2: matching section
            var sec = _sectionResolver.Resolve(virtualPath);
            if (sec is not null)
            {
                return new RatioRule(
                    Ratio: sec.Ratio,
                    IsFree: sec.IsFree,
                    MultiplyCost: sec.MultiplyCost,
                    UploadBonus: sec.UploadBonus
                );
            }

            // level 3: path-based ratio rule
            foreach (var kv in from kv in _ratioRules let key = kv.Key where virtualPath.StartsWith(key, StringComparison.OrdinalIgnoreCase) select kv)
                return kv.Value;

            // level 4: global
            return RatioRule.Default;
        }
    }
}
