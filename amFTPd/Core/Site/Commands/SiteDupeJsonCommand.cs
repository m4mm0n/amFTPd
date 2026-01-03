using System.Text.Json;
using amFTPd.Core.Dupe;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteDupeJsonCommand : SiteCommandBase
{
    public override string Name => "DUPEJSON";
    public override bool RequiresAdmin => true;

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken ct)
    {
        var store = context.Runtime.DupeStore;
        if (store is null)
        {
            await context.Session.WriteAsync("550 DUPE database not enabled.\r\n", ct);
            return;
        }

        var filters = SiteDupeFilters.Parse(argument);

        var entries = store.ToSceneDupeDb()
            .Where(filters.Match)
            .OrderBy(x => x.Section)
            .ThenBy(x => x.ReleaseName)
            .ToList();

        var json = JsonSerializer.Serialize(
            entries,
            new JsonSerializerOptions { WriteIndented = true });

        await context.Session.WriteAsync(
            "200-JSON DUPE\r\n" +
            json.Replace("\n", "\r\n") +
            "\r\n200 End.\r\n",
            ct);
    }
}