namespace amFTPd.Core.Site.Commands;

public sealed class SiteDelPreCommand : SiteCommandBase
{
    public override string Name => "DELPRE";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;

    public override string HelpText =>
        "DELPRE <release> - remove a release from the PRE list";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync(
                "501 Syntax: SITE DELPRE <release>\r\n",
                cancellationToken);
            return;
        }

        var registry = context.Runtime.PreRegistry;

        var removed = registry.TryRemoveByRelease(argument);

        if (!removed)
        {
            await context.Session.WriteAsync(
                $"550 PRE '{argument}' not found.\r\n",
                cancellationToken);
            return;
        }

        await context.Session.WriteAsync(
            $"200 PRE '{argument}' removed from PRE list.\r\n",
            cancellationToken);
    }
}