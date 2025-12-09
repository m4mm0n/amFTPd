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

using amFTPd.Config.Ftpd;
using amFTPd.Config.Ftpd.RatioRules;
using amFTPd.Scripting;

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
        private readonly Dictionary<string, GroupConfig> _groups;

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
            _groups = groups ?? throw new ArgumentNullException(nameof(groups));
        }

        /// <summary>
        /// Computes the effective ratio rule for a user + virtual path.
        /// Purely SectionRule, DirectoryRule, RatioRule based.
        /// </summary>
        public RatioRule Resolve(string virtualPath, string? primaryGroup, RatioResolutionPipeline pipeline)
        {
            return pipeline.Resolve(virtualPath, primaryGroup);
        }

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
        public AMScriptResult ResolveLoginRule(RatioLoginContext ctx)
        {
            if (ctx is null) throw new ArgumentNullException(nameof(ctx));

            // For now: no special login rules – always allow, no cost, no earned upload.
            // This keeps the PASS wiring simple and compile-safe.
            //
            // Later, you can use:
            //  - ctx.UserName / ctx.GroupName for group-based login policy
            //  - ctx.RemoteAddress / ctx.RemoteHost for IP / host-based blocks
            //  - ctx.NowUtc for time-of-day login windows
            // and return Deny / DenyWithReason, or set NewDownloadLimit / NewUploadLimit, etc.

            return new AMScriptResult(
                Action: AMRuleAction.Allow,
                CostDownload: 0L,
                EarnedUpload: 0L
            );
        }
    }
}
