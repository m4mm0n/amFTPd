namespace amFTPd.Scripting
{

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
        // RETURN: no change
        public static AMScriptResult NoChange(AMScriptContext ctx)
            => new(
                AMRuleAction.None,
                ctx.CostDownload,
                ctx.EarnedUpload
            );

        // RETURN: allow
        public static AMScriptResult Allow(AMScriptContext ctx)
            => new(
                AMRuleAction.Allow,
                ctx.CostDownload,
                ctx.EarnedUpload
            );

        // RETURN: deny (generic)
        public static AMScriptResult Deny(AMScriptContext ctx)
            => new(
                AMRuleAction.Deny,
                ctx.CostDownload,
                ctx.EarnedUpload
            );

        // RETURN: SITE custom output
        public static AMScriptResult CustomOutput(string msg)
            => new(
                AMRuleAction.Allow,
                0,
                0,
                Message: null,
                SiteOutput: msg
            );

        // RETURN: deny with a custom message
        public static AMScriptResult DenyWithReason(string msg)
            => new(
                AMRuleAction.Deny,
                0,
                0,
                Message: null,
                SiteOutput: null,
                DenyReason: msg
            );

        // RETURN: override site command (pure override, no fallback)
        public static AMScriptResult SiteOverride()
            => new(
                AMRuleAction.Allow,
                0,
                0,
                Message: "SITE_OVERRIDE"
            );
    }
}