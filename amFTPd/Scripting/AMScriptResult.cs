namespace amFTPd.Scripting
{
    /// <summary>
    /// Represents the result of executing an AM script, including the action to take and associated cost and earnings.
    /// </summary>
    /// <remarks>This record encapsulates the outcome of a script execution, including the specified action
    /// and any associated cost or earnings. Use the static methods <see cref="NoChange(AMScriptContext)"/>, <see
    /// cref="Allow(AMScriptContext)"/>, and <see cref="Deny(AMScriptContext)"/>  to create predefined results based on
    /// the provided script context.</remarks>
    /// <param name="Action">The action to be taken as a result of the script execution. Possible values are defined in <see
    /// cref="AMRuleAction"/>.</param>
    /// <param name="CostDownload">The cost incurred for downloading, measured in arbitrary units.</param>
    /// <param name="EarnedUpload">The earnings gained from uploading, measured in arbitrary units.</param>
    public sealed record AMScriptResult(
        AMRuleAction Action,
        long CostDownload,
        long EarnedUpload
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