using amFTPd.Core.Import;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteImportCancelCommand : SiteCommandBase
{
    public override string Name => "IMPORTCANCEL";
    public override bool RequiresAdmin => true;

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken ct)
    {
        ImportProgressRegistry.Cancel();
        await context.Session.WriteAsync(
            "200 Import/export cancellation requested.\r\n", ct);
    }
}