namespace amFTPd.Core.Site.Commands;

public sealed class SiteGroupdelCommand : SiteCommandBase
{
    public override string Name => "GROUPDEL";
    public override bool RequiresAdmin => true;
    public override string HelpText => "GROUPDEL <group> - delete a group (if backend supports it)";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        await context.Session.WriteAsync("502 GROUPDEL not implemented for this backend.\r\n", cancellationToken);
    }
}