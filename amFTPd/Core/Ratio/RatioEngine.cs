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

using amFTPd.Config.Ftpd;
using amFTPd.Config.Ftpd.RatioRules;

namespace amFTPd.Core.Ratio
{
    /// <summary>
    /// Provides methods for resolving ratio rules and calculating credit earnings and costs for FTP users based on
    /// upload and download activity.
    /// </summary>
    /// <remarks>The RatioEngine encapsulates logic for determining effective ratio rules for virtual paths
    /// and users, as well as computing credit-related operations such as upload earnings and download costs. It
    /// supports directory-level, section-level, and per-path rule resolution, and applies user-specific modifiers such
    /// as VIP status and free-leech conditions. This class is thread-safe for concurrent read operations if the
    /// underlying rule dictionaries are not modified after construction.</remarks>
    public sealed class RatioEngine
    {
        private readonly Dictionary<string, SectionRule> _sections;
        private readonly Dictionary<string, DirectoryRule> _directoryRules;
        private readonly Dictionary<string, RatioRule> _ratioRules;
        private readonly Dictionary<string, GroupConfig> _groups;
        /// <summary>
        /// Initializes a new instance of the RatioEngine class with the specified section, directory, ratio, and group
        /// configurations.
        /// </summary>
        /// <param name="sections">A dictionary containing section rules, keyed by section name. If null, an empty dictionary is used.</param>
        /// <param name="directoryRules">A dictionary containing directory rules, keyed by directory name. If null, an empty dictionary is used.</param>
        /// <param name="ratioRules">A dictionary containing ratio rules, keyed by rule name. If null, an empty dictionary is used.</param>
        /// <param name="groups">A dictionary containing group configurations, keyed by group name. If null, an empty dictionary is used.</param>
        public RatioEngine(
            Dictionary<string, SectionRule> sections,
            Dictionary<string, DirectoryRule> directoryRules,
            Dictionary<string, RatioRule> ratioRules,
            Dictionary<string, GroupConfig> groups)
        {
            _sections = sections ?? new();
            _directoryRules = directoryRules ?? new();
            _ratioRules = ratioRules ?? new();
            _groups = groups ?? new();
        }

        /// <summary>
        /// Computes the effective ratio rule for a given virtual path and user.
        /// </summary>
        public RatioRule ResolveRule(string virtualPath, FtpUser user)
        {
            // 1) Directory-level override (exact match)
            if (_directoryRules.TryGetValue(virtualPath, out var dir))
            {
                var rr = BuildFromDirectoryRule(dir);
                if (rr is not null)
                    return rr;
            }

            // 2) Section-level match (prefix match)
            foreach (var sec in _sections.Values.Where(sec => virtualPath.StartsWith(sec.VirtualRoot, StringComparison.OrdinalIgnoreCase)))
                return new RatioRule(
                    Ratio: sec.Ratio,
                    IsFree: sec.IsFree,
                    MultiplyCost: sec.MultiplyCost,
                    UploadBonus: sec.UploadBonus
                );

            // 3) RatioRules dictionary (per-path rule)
            foreach (var kv in from kv in _ratioRules let key = kv.Key where virtualPath.StartsWith(key, StringComparison.OrdinalIgnoreCase) select kv)
                return kv.Value;

            // 4) Global default
            return RatioRule.Default;
        }

        private RatioRule? BuildFromDirectoryRule(DirectoryRule dr)
        {
            if (dr.IsFree == null
                && dr.Ratio == null
                && dr.MultiplyCost == null
                && dr.UploadBonus == null)
                return null;

            return new RatioRule(
                Ratio: dr.Ratio ?? 1.0,
                IsFree: dr.IsFree ?? false,
                MultiplyCost: dr.MultiplyCost ?? 1.0,
                UploadBonus: dr.UploadBonus ?? 1.0
            );
        }

        /// <summary>
        /// Computes how many KB of credits the user earns from an upload.
        /// </summary>
        public long ComputeUploadEarnedKb(long bytes, RatioRule rule, FtpUser user)
        {
            if (bytes <= 0)
                return 0;

            var kb = bytes / 1024.0;

            var bonus = rule.UploadBonus;

            // VIP multiplier
            if (user.IsVip)
                bonus *= 1.5;

            return (long)Math.Round(kb * bonus, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Computes how many KB credits a download costs.
        /// Returns 0 if directory is free-leech.
        /// </summary>
        public long ComputeDownloadCostKb(long bytes, RatioRule rule, FtpUser user)
        {
            if (user.IsNoRatio)
                return 0;

            if (rule.IsFree)
                return 0;

            if (bytes <= 0)
                return 0;

            var kb = bytes / 1024.0;
            var cost = kb / rule.Ratio;

            cost *= rule.MultiplyCost;

            return (long)Math.Round(cost, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Checks if user has enough credits to download the given number of bytes.
        /// </summary>
        public bool HasEnoughCredits(FtpUser user, long bytes, RatioRule rule)
        {
            if (user.IsNoRatio)
                return true;

            if (rule.IsFree)
                return true;

            var cost = ComputeDownloadCostKb(bytes, rule, user);
            return user.CreditsKb >= cost;
        }
    }
}
