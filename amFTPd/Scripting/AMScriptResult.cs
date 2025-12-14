/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AMScriptResult.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-16 06:36:37
 *  Last Modified:  2025-12-14 18:10:56
 *  CRC32:          0x27D83D1B
 *  
 *  Description:
 *      Represents the result of an action performed by an AMScript, including the action type, associated costs, and optiona...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
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
    /// <param name="IsError">Indicates whether the result represents an error condition.</param>
    /// <param name="ErrorCode">An optional error code associated with the result, if applicable.</param>
    /// <param name="ErrorMessage">An optional error message providing details about the error condition, if applicable.</param>
    public sealed partial record AMScriptResult(
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
        long? CreditDelta = null,
        bool IsError = false,
        string? ErrorCode = null,
        string? ErrorMessage = null
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
        /// <summary>
        /// Creates an <see cref="AMScriptResult"/> that represents an error condition with the specified error code and
        /// message.
        /// </summary>
        /// <remarks>Use this method to generate a standardized error result when an operation fails and
        /// you need to communicate the error details to the caller.</remarks>
        /// <param name="ctx">The current <see cref="AMScriptContext"/> associated with the operation. Cannot be <see langword="null"/>.</param>
        /// <param name="errorCode">A string that identifies the type or category of the error. Cannot be <see langword="null"/>.</param>
        /// <param name="errorMessage">A descriptive message providing details about the error. Cannot be <see langword="null"/>.</param>
        /// <returns>An <see cref="AMScriptResult"/> instance indicating an error, with the specified error code and message set.
        /// Other result fields are set to their default or context-derived values.</returns>
        public static AMScriptResult Error(AMScriptContext ctx, string errorCode, string errorMessage)
            => new(
                AMRuleAction.None,
                ctx.CostDownload,
                ctx.EarnedUpload,
                Message: null,
                SiteOutput: null,
                DenyReason: null,
                NewUploadLimit: null,
                NewDownloadLimit: null,
                CreditDelta: null,
                IsError: true,
                ErrorCode: errorCode,
                ErrorMessage: errorMessage
            );

        /// <summary>
        /// Convenience alias for <see cref="NewUploadLimit"/> when interpreted as kilobits per second.
        /// This keeps compatibility with callers using the *Kbps* naming.
        /// </summary>
        public int? NewUploadLimitKbps => NewUploadLimit;

        /// <summary>
        /// Convenience alias for <see cref="NewDownloadLimit"/> when interpreted as kilobits per second.
        /// This keeps compatibility with callers using the *Kbps* naming.
        /// </summary>
        public int? NewDownloadLimitKbps => NewDownloadLimit;
    }
}