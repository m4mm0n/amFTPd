namespace amFTPd.Core.Site.Commands;

public sealed class SiteBlockCommand : SiteCommandBase
{
    public override string Name => "BLOCK";
    public override bool RequiresAdmin => true;
    public override bool RequiresSiteop => true;
    public override string HelpText => "BLOCK <ip_key> [reason]";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var parts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            await context.Session.WriteAsync(
                "501 Usage: SITE BLOCK <ip_key> [reason]\r\n",
                cancellationToken);
            return;
        }

        var key = parts[0];
        var reason = parts.Length > 1 ? parts[1] : "manual block";

        context.Server.BlockIpByKey(key, reason);

        await context.Session.WriteAsync(
            $"200 IP bucket {key} blocked.\r\n",
            cancellationToken);
    }
}