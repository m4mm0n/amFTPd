using amFTPd.Db;

namespace amFTPd.Core.Site.Commands
{
    public sealed class SiteDbfsckCommand : SiteCommandBase
    {
        public override string Name => "DBFSCK";
        public override bool RequiresAdmin => true;
        public override string HelpText => "DBFSCK";
        public override async Task ExecuteAsync(SiteCommandContext context, string argument, CancellationToken cancellationToken)
        {
            if (context.Router.Runtime.Database is null)
            {
                await context.Session.WriteAsync("550 Database backend not enabled.\r\n", cancellationToken);
                return;
            }
            var maint = new DatabaseMaintenance(context.Router.Runtime.Database, context.Log);
            maint.RunFsck();
            await context.Session.WriteAsync("200 DBFSCK completed.\r\n", cancellationToken);
        }
    }
}
