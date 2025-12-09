namespace amFTPd.Core.Site.Commands
{
    public sealed class SiteGroupaddCommand : SiteCommandBase
    {
        public override string Name => "GROUPADD";
        public override bool RequiresAdmin => true;
        public override string HelpText => "GROUPADD <group> - create a group (if backend supports it)";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            await context.Session.WriteAsync("502 GROUPADD not implemented for this backend.\r\n", cancellationToken);
        }
    }
}
