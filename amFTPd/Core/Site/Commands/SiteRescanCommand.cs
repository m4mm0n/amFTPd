namespace amFTPd.Core.Site.Commands;

public sealed class SiteRescanCommand : SiteCommandBase
{
    public override string Name => "RESCAN";
    public override bool RequiresAdmin => true;
    public override string HelpText => "RESCAN <path> - rescan a release (zipscript integration)";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        await context.Session.WriteAsync("502 RESCAN not implemented yet.\r\n", cancellationToken);
    }
}