namespace amFTPd.Core.Site.Commands;

public sealed class SiteRscheckCommand : SiteCommandBase
{
    public override string Name => "RSCHECK";
    public override bool RequiresAdmin => true;
    public override string HelpText => "RSCHECK <path> - check rescan status (zipscript integration)";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        await context.Session.WriteAsync("502 RSCHECK not implemented yet.\r\n", cancellationToken);
    }
}