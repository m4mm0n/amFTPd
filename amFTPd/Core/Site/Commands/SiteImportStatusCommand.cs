using amFTPd.Core.Import;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteImportStatusCommand : SiteCommandBase
{
    public override string Name => "IMPORTSTATUS";
    public override bool RequiresAdmin => true;

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken ct)
    {
        var p = ImportProgressRegistry.Current;

        if (p == null)
        {
            await context.Session.WriteAsync(
                "200 No import/export running.\r\n", ct);
            return;
        }

        await context.Session.WriteAsync(
            $"200- {p.Name}\r\n" +
            $" Started : {p.Started:u}\r\n" +
            $" Progress: {p.Processed}/{p.Total}\r\n" +
            $" Cancel  : {p.CancelRequested}\r\n" +
            $"200 End.\r\n",
            ct);
    }
}