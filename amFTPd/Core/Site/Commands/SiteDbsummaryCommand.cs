namespace amFTPd.Core.Site.Commands;

public sealed class SiteDbsummaryCommand : SiteCommandBase
{
    public override string Name => "DBSUMMARY";
    public override bool RequiresAdmin => true;
    public override string HelpText => "DBSUMMARY";
    public override async Task ExecuteAsync(SiteCommandContext context, string argument, CancellationToken cancellationToken)
    {
        if (context.Router.Runtime.Database is null)
        {
            await context.Session.WriteAsync("550 Database backend not enabled.\r\n", cancellationToken);
            return;
        }
        context.Router.Runtime.Database.PrintSummary();
        await context.Session.WriteAsync("200 DBSUMMARY completed.\r\n", cancellationToken);
    }
}