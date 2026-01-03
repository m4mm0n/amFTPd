using amFTPd.Db.Abstractions;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteSqlProvidersCommand : SiteCommandBase
{
    public override string Name => "SQLPROVIDERS";
    public override bool RequiresAdmin => true;
    public override bool RequiresSiteop => true;

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken ct)
    {
        var providers = SqlProviderRegistry.Providers;

        if (providers.Count == 0)
        {
            await context.Session.WriteAsync(
                "200 No SQL providers available.\r\n", ct);
            return;
        }

        await context.Session.WriteAsync(
            "200- Available SQL providers:\r\n", ct);

        foreach (var p in providers.OrderBy(x => x))
        {
            await context.Session.WriteAsync(
                $" {p}\r\n", ct);
        }

        await context.Session.WriteAsync("200 End.\r\n", ct);
    }
}