/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           RatioEngine.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x4A016FA1
 *  
 *  Description:
 *      Provides functionality to compute and resolve effective ratio rules based on section, directory, and ratio configurat...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using amFTPd.Config.Ftpd;
using amFTPd.Config.Ftpd.RatioRules;
using amFTPd.Scripting;
using CoreRatioContext = amFTPd.Core.RatioLoginContext;

namespace amFTPd.Core.Ratio
{
    /// <summary>
    /// Provides functionality to compute and resolve effective ratio rules based on section, directory, and ratio
    /// configurations.
    /// </summary>
    /// <remarks>The <see cref="RatioEngine"/> class is designed to evaluate and determine the appropriate
    /// <see cref="RatioRule"/> for a given virtual path and user group. It relies on predefined rules and
    /// configurations, including section rules, directory rules, and ratio rules, to perform the resolution.</remarks>
    public sealed class RatioEngine
    {
        private readonly Dictionary<string, SectionRule> _sections;
        private readonly Dictionary<string, DirectoryRule> _dirRules;
        private readonly Dictionary<string, RatioRule> _ratioRules;
        private readonly RatioResolutionPipeline _ratioPipeline;

        /// <summary>
        /// Initializes a new instance of the <see cref="RatioEngine"/> class with the specified configuration rules and
        /// group settings.
        /// </summary>
        /// <param name="sections">A dictionary containing section rules, where the key is the section name and the value is the corresponding
        /// <see cref="SectionRule"/>. This parameter cannot be <see langword="null"/>.</param>
        /// <param name="dirRules">A dictionary containing directory rules, where the key is the directory name and the value is the
        /// corresponding <see cref="DirectoryRule"/>. This parameter cannot be <see langword="null"/>.</param>
        /// <param name="ratioRules">A dictionary containing ratio rules, where the key is the ratio name and the value is the corresponding <see
        /// cref="RatioRule"/>. This parameter cannot be <see langword="null"/>.</param>
        /// <param name="groups">A dictionary containing group configurations, where the key is the group name and the value is the
        /// corresponding <see cref="GroupConfig"/>. This parameter cannot be <see langword="null"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown if any of the parameters <paramref name="sections"/>, <paramref name="dirRules"/>, <paramref
        /// name="ratioRules"/>, or <paramref name="groups"/> is <see langword="null"/>.</exception>
        public RatioEngine(
            Dictionary<string, SectionRule> sections,
            Dictionary<string, DirectoryRule> dirRules,
            Dictionary<string, RatioRule> ratioRules,
            Dictionary<string, GroupConfig> groups)
        {
            _sections = sections ?? throw new ArgumentNullException(nameof(sections));
            _dirRules = dirRules ?? throw new ArgumentNullException(nameof(dirRules));
            _ratioRules = ratioRules ?? throw new ArgumentNullException(nameof(ratioRules));
            _ = groups ?? throw new ArgumentNullException(nameof(groups));
            _ratioPipeline = new RatioResolutionPipeline(
                new DirectoryRuleEngine(_dirRules),
                new Config.Ftpd.RatioRules.SectionResolver(_sections),
                _ratioRules);
        }

        /// <summary>
        /// Computes the effective ratio rule for a user + virtual path.
        /// Purely SectionRule, DirectoryRule, RatioRule based.
        /// </summary>
        public RatioRule Resolve(string virtualPath, string? primaryGroup, RatioResolutionPipeline pipeline) => pipeline.Resolve(virtualPath, primaryGroup);

        /// <summary>
        /// Evaluates login-specific policy for the provided context.
        /// This is invoked from the PASS handler to decide whether to allow the login,
        /// optionally adjust speed limits or credits, etc.
        /// </summary>
        /// <param name="ctx">The login context to evaluate.</param>
        /// <returns>
        /// An <see cref="AMScriptResult"/> describing the action (allow/deny) and any side effects,
        /// such as new speed limits or credit deltas.
        /// </returns>
        public AMScriptResult ResolveLoginRule(CoreRatioContext ctx)
            => ResolveLoginRuleInternal(ctx);

        internal AMScriptResult ResolveLoginRuleInternal(CoreRatioContext ctx)
        {
            if (ctx is null) throw new ArgumentNullException(nameof(ctx));

            var nowUtc = ctx.NowUtc == default
                ? DateTime.UtcNow
                : ctx.NowUtc;

            // Anonymous and no-ratio users are exempt from login policy checks.
            if (ctx.IsAnonymous || (ctx.User?.IsNoRatio == true))
            {
                return AMScriptResult.Allow(new AMScriptContext(
                    IsFxp: false,
                    Section: "/",
                    FreeLeech: false,
                    UserName: ctx.UserName,
                    UserGroup: ctx.GroupName ?? string.Empty,
                    Bytes: 0,
                    Kb: 0,
                    CostDownload: 0,
                    EarnedUpload: 0,
                    Event: "LOGIN",
                    IsAdmin: ctx.User?.IsAdmin == true || ctx.User?.IsSiteop == true));
            }

            var group = ctx.GroupName ?? string.Empty;
            var rule = _ratioPipeline.Resolve("/", group);

            if (!IsAllowedLoginWindow(rule, nowUtc))
            {
                return AMScriptResult.DenyWithReason(
                    "530 Login denied by ratio policy: outside allowed login window.");
            }

            // Best-effort minimum-ratio enforcement.
            // There is no live ratio accumulator yet, so when minimum ratio is configured
            // we block obviously inconsistent states.
            if (rule.MinimumRatio is > 0 && ctx.User is not null && ctx.User.CreditsKb <= 0)
            {
                return AMScriptResult.DenyWithReason(
                    $"530 Login denied by ratio policy: minimum ratio {rule.MinimumRatio.Value:0.00} requires account history.");
            }

            return AMScriptResult.Allow(new AMScriptContext(
                IsFxp: false,
                Section: "/",
                FreeLeech: false,
                UserName: ctx.UserName,
                UserGroup: group,
                Bytes: 0,
                Kb: 0,
                CostDownload: 0,
                EarnedUpload: 0,
                Event: "LOGIN",
                IsAdmin: ctx.User?.IsAdmin == true || ctx.User?.IsSiteop == true));
        }

        private static bool IsAllowedLoginWindow(RatioRule rule, DateTime nowUtc)
        {
            var minHour = Math.Clamp(rule.MinHour, 0, 24);
            var maxHour = Math.Clamp(rule.MaxHour, 0, 24);
            var hour = nowUtc.Hour;

            if (minHour == maxHour)
            {
                return true;
            }

            if (minHour < maxHour)
            {
                return hour >= minHour && hour < maxHour;
            }

            return hour >= minHour || hour < maxHour;
        }
    }
}
