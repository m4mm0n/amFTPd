namespace amFTPd.Core.Site.Commands;

public sealed class SiteUnblockCommand : SiteCommandBase
{
    public override string Name => "UNBLOCK";
    public override bool RequiresAdmin => true;
    public override bool RequiresSiteop => true;
    public override string HelpText => "UNBLOCK <ip_key> - remove IP block";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync(
                "501 Usage: SITE UNBLOCK <ip_key>\r\n",
                cancellationToken);
            return;
        }

        context.Server.UnblockIp(argument.Trim());

        await context.Session.WriteAsync(
            $"200 IP bucket {argument} unblocked.\r\n",
            cancellationToken);
    }
}