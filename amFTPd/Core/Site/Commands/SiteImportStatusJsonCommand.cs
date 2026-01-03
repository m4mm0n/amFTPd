using System.Text.Json;
using amFTPd.Core.Import;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteImportStatusJsonCommand : SiteCommandBase
{
    public override string Name => "IMPORTSTATUSJSON";
    public override bool RequiresAdmin => true;

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(
            ImportProgressRegistry.Current,
            new JsonSerializerOptions { WriteIndented = true });

        await context.Session.WriteAsync(
            "200-JSON\r\n" +
            json.Replace("\n", "\r\n") +
            "\r\n200 End.\r\n",
            ct);
    }
}