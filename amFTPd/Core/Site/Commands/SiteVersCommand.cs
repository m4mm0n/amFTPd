using System.Reflection;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteVersCommand : SiteCommandBase
{
    public override string Name => "VERS";
    public override string HelpText => "VERS - show amFTPd version";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;
        var asm = Assembly.GetExecutingAssembly().GetName();
        var ver = asm.Version?.ToString() ?? "unknown";

        await s.WriteAsync($"200 amFTPd {ver}\r\n", cancellationToken);
    }
}