/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-20
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

namespace amFTPd.Scripting
{
    /// <summary>
    /// Represents the result of an action performed by an AMScript, including the action type, associated costs, and
    /// optional metadata such as messages or limits.
    /// </summary>
    /// <remarks>This record encapsulates the outcome of executing an AMScript, including details about the
    /// action taken (e.g., allow, deny, or no change), the cost incurred, and any additional information relevant to
    /// the result. Optional fields provide flexibility for custom outputs, user-specific rules, or site-specific
    /// overrides.</remarks>
    /// <param name="Action">The action to be taken as a result of the AMScript execution. Possible values include <see
    /// cref="AMRuleAction.None"/>, <see cref="AMRuleAction.Allow"/>, and <see cref="AMRuleAction.Deny"/>.</param>
    /// <param name="CostDownload">The cost, in bytes, associated with the download operation.</param>
    /// <param name="EarnedUpload">The amount, in bytes, earned for upload as a result of the AMScript execution.</param>
    /// <param name="Message">An optional message providing additional context or metadata about the result. This can be used for specific
    /// scenarios such as section overrides.</param>
    /// <param name="SiteOutput">An optional custom output string for site-specific commands or responses.</param>
    /// <param name="DenyReason">An optional reason for denying the action, typically used in user-based rules.</param>
    /// <param name="NewUploadLimit">An optional new upload limit, in bytes, to be applied as part of the result.</param>
    /// <param name="NewDownloadLimit">An optional new download limit, in bytes, to be applied as part of the result.</param>
    /// <param name="CreditDelta">An optional credit adjustment, in bytes, to be applied to the user's account.</param>
    public sealed record AMScriptResult(
        AMRuleAction Action,
        long CostDownload,
        long EarnedUpload,

        // Optional message used for things like SECTION_OVERRIDE::<NAME>
        string? Message = null,

        // For SITE custom output
        string? SiteOutput = null,

        // SUBSYSTEM: user-based rules
        string? DenyReason = null,
        int? NewUploadLimit = null,
        int? NewDownloadLimit = null,
        long? CreditDelta = null
    )
    {
        /// <summary>
        /// Returns an <see cref="AMScriptResult"/> that indicates no changes to the current rule action,  while
        /// preserving the provided cost and earned values.
        /// </summary>
        /// <param name="ctx">The context containing the cost and earned values to include in the result.</param>
        /// <returns>An <see cref="AMScriptResult"/> with no rule action applied, and the cost and earned values  set to those
        /// provided in the <paramref name="ctx"/>.</returns>
        public static AMScriptResult NoChange(AMScriptContext ctx)
            => new(
                AMRuleAction.None,
                ctx.CostDownload,
                ctx.EarnedUpload
            );
        /// <summary>
        /// Creates an <see cref="AMScriptResult"/> that represents an "Allow" action.
        /// </summary>
        /// <param name="ctx">The context of the script execution, containing the cost of the download and the earned upload values.</param>
        /// <returns>An <see cref="AMScriptResult"/> instance configured with the "Allow" action, along with the specified cost
        /// and earned values from the provided context.</returns>
        public static AMScriptResult Allow(AMScriptContext ctx)
            => new(
                AMRuleAction.Allow,
                ctx.CostDownload,
                ctx.EarnedUpload
            );
        /// <summary>
        /// Creates a result that represents a denial action in the context of the specified script execution.
        /// </summary>
        /// <param name="ctx">The context of the script execution, including cost and earned values.</param>
        /// <returns>An <see cref="AMScriptResult"/> representing the denial action with the associated context values.</returns>
        public static AMScriptResult Deny(AMScriptContext ctx)
            => new(
                AMRuleAction.Deny,
                ctx.CostDownload,
                ctx.EarnedUpload
            );
        /// <summary>
        /// Creates a new <see cref="AMScriptResult"/> instance with the specified site output message.
        /// </summary>
        /// <param name="msg">The message to include in the site output.</param>
        /// <returns>A new <see cref="AMScriptResult"/> instance with the specified site output message and default values for
        /// other properties.</returns>
        public static AMScriptResult CustomOutput(string msg)
            => new(
                AMRuleAction.Allow,
                0,
                0,
                Message: null,
                SiteOutput: msg
            );
        /// <summary>
        /// Creates a result that denies an action with a specified reason.
        /// </summary>
        /// <param name="msg">The reason for denying the action. This value is included in the result as the denial reason.</param>
        /// <returns>An <see cref="AMScriptResult"/> instance representing a denial with the specified reason.</returns>
        public static AMScriptResult DenyWithReason(string msg)
            => new(
                AMRuleAction.Deny,
                0,
                0,
                Message: null,
                SiteOutput: null,
                DenyReason: msg
            );
        /// <summary>
        /// Creates an <see cref="AMScriptResult"/> instance that represents a site override action.
        /// </summary>
        /// <returns>An <see cref="AMScriptResult"/> object with the action set to <see cref="AMRuleAction.Allow"/>,  a priority
        /// of 0, and a message indicating "SITE_OVERRIDE".</returns>
        public static AMScriptResult SiteOverride()
            => new(
                AMRuleAction.Allow,
                0,
                0,
                Message: "SITE_OVERRIDE"
            );
    }
}