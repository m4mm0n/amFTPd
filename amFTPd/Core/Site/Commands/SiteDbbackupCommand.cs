using amFTPd.Db;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteDbbackupCommand : SiteCommandBase
{
    public override string Name => "DBBACKUP";
    public override bool RequiresAdmin => true;
    public override string HelpText => "DBBACKUP";
    public override async Task ExecuteAsync(SiteCommandContext context, string argument, CancellationToken cancellationToken)
    {
        if (context.Router.Runtime.Database is null)
        {
            await context.Session.WriteAsync("550 Database backend not enabled.\r\n", cancellationToken);
            return;
        }
        var maint = new DatabaseMaintenance(context.Router.Runtime.Database, context.Log);
        maint.CreateBackup();
        await context.Session.WriteAsync("200 DBBACKUP completed.\r\n", cancellationToken);
    }
}