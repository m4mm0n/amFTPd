/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           RatioResolutionPipeline.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-28 21:28:55
 *  Last Modified:  2025-12-10 03:58:32
 *  CRC32:          0x4ADABB13
 *  
 *  Description:
 *      Provides a pipeline for resolving the effective ratio rule for a given virtual path, using directory overrides, secti...
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
        private readonly IReadOnlyDictionary<string, RatioRule> _ratioRules;

        public RatioResolutionPipeline(
            DirectoryRuleEngine dirEngine,
            SectionResolver sectionResolver,
            IReadOnlyDictionary<string, RatioRule> ratioRules)
        {
            _dirEngine = dirEngine;
            _sectionResolver = sectionResolver;
            _ratioRules = ratioRules ?? throw new ArgumentNullException(nameof(ratioRules));
        }

        /// <summary>
        /// Resolve ratio rule for a path + group pair.
        /// 
        /// Returns:
        ///     1. DirectoryRule (highest priority)
        ///     2. SectionRule
        ///     3. RatioRule (lowest priority)
        /// </summary>
        public RatioRule Resolve(string virtPath, string? group)
        {
            virtPath = Normalize(virtPath);

            //---------------------------------------------------------------------
            // 1. Directory rules (strongest override)
            //---------------------------------------------------------------------
            var dRule = _dirEngine.Resolve(virtPath);
            if (dRule != null)
                return new RatioRule(
                    Ratio: dRule.Ratio,
                    IsFree: dRule.IsFree,
                    MultiplyCost: dRule.MultiplyCost,
                    UploadBonus: dRule.UploadBonus
                );

            //---------------------------------------------------------------------
            // 2. Section rule
            //---------------------------------------------------------------------
            var section = _sectionResolver.Resolve(virtPath);
            if (section != null)
                return new RatioRule(
                    Ratio: section.Ratio,
                    IsFree: section.IsFree,
                    MultiplyCost: section.MultiplyCost,
                    UploadBonus: section.UploadBonus
                );

            //---------------------------------------------------------------------
            // 3. Group-based ratio rule
            //---------------------------------------------------------------------
            if (group != null && _ratioRules.TryGetValue(group, out var rRule))
            {
                return rRule;
            }

            //---------------------------------------------------------------------
            // 4. Fallback: 1:1 ratio, no free leech, no bonus.
            //---------------------------------------------------------------------
            return new RatioRule(
                Ratio: 1.0,
                IsFree: false,
                MultiplyCost: 1.0,
                UploadBonus: 1.0
            );
        }

        private static string Normalize(string p)
        {
            p = p.Replace('\\', '/');
            if (!p.StartsWith('/'))
                p = "/" + p;
            return p;
        }
    }
}
