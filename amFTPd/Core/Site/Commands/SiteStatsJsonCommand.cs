using System.Text.Json;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteStatsJsonCommand : SiteCommandBase
{
    public override string Name => "STATSJSON";
    public override bool RequiresAdmin => true;
    public override bool RequiresSiteop => true;

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var status = context.StatusEndpoint;
        if (status is null)
        {
            await context.Session.WriteAsync(
                "550 Status endpoint is disabled.\r\n",
                cancellationToken);
            return;
        }

        var mode = argument?.Trim() ?? string.Empty;

        var includeIps =
            mode.Equals("FULL", StringComparison.OrdinalIgnoreCase);

        var payload = status.BuildStatusPayload(includeIps);

        var json = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions { WriteIndented = true });

        await context.Session.WriteAsync(
            "200-JSON statistics\r\n" +
            json.Replace("\n", "\r\n") +
            "\r\n200 End.\r\n",
            cancellationToken);
    }
}