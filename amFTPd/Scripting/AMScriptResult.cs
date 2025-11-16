namespace amFTPd.Scripting
{
    /// <summary>
    /// Represents the result of executing an AM script, including the action to take, associated costs, rewards, and an
    /// optional message.
    /// </summary>
    /// <remarks>This record encapsulates the outcome of a script execution, including the action to be
    /// performed, the cost incurred for downloading, the upload reward earned, and an optional message providing
    /// additional context or details.</remarks>
    /// <param name="Action">The action to be taken as a result of the script execution. Possible values are defined in the <see
    /// cref="AMRuleAction"/> enumeration.</param>
    /// <param name="CostDownload">The cost incurred for downloading, represented as a long integer.</param>
    /// <param name="EarnedUpload">The reward earned for uploading, represented as a long integer.</param>
    /// <param name="Message">An optional message providing additional context or details about the script result. This value can be <see
    /// langword="null"/>.</param>
    public sealed record AMScriptResult(
        AMRuleAction Action,
        long CostDownload,
        long EarnedUpload,
        string? Message = null
    )
    {
        public static AMScriptResult NoChange(AMScriptContext ctx)
            => new(AMRuleAction.None, ctx.CostDownload, ctx.EarnedUpload);

        public static AMScriptResult Allow(AMScriptContext ctx)
            => new(AMRuleAction.Allow, ctx.CostDownload, ctx.EarnedUpload);

        public static AMScriptResult Deny(AMScriptContext ctx)
            => new(AMRuleAction.Deny, ctx.CostDownload, ctx.EarnedUpload);
    }
}