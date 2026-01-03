using amFTPd.Core.Pre;
using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SitePrelistCommand : SiteCommandBase
{
    public override string Name => "PRELIST";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;

    public override string HelpText =>
        "PRELIST [count|section] - show recent PREs";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        // Snapshot first (thread-safe)
        var snapshot = context.Runtime.PreRegistry.All.ToArray();

        if (snapshot.Length == 0)
        {
            await context.Session.WriteAsync(
                "200-Recent PREs\r\n" +
                " No PREs recorded.\r\n" +
                "200 End.\r\n",
                cancellationToken);
            return;
        }

        var limit = 20;
        string? sectionFilter = null;

        if (!string.IsNullOrWhiteSpace(argument))
        {
            if (int.TryParse(argument, out var n))
                limit = Math.Clamp(n, 1, 200);
            else
                sectionFilter = argument.Trim();
        }

        IEnumerable<PreEntry> query = snapshot;

        if (!string.IsNullOrEmpty(sectionFilter))
        {
            query = query.Where(p =>
                p.Section.Equals(sectionFilter,
                    StringComparison.OrdinalIgnoreCase));
        }

        query = query
            .OrderByDescending(p => p.Timestamp)
            .Take(limit);

        var sb = new StringBuilder();
        sb.AppendLine("200-Recent PREs");

        foreach (var p in query)
        {
            sb.AppendLine(
                $" {p.Timestamp:HH:mm:ss} " +
                $"{p.Section,-10} " +
                $"{p.ReleaseName} " +
                $"({p.User})");
        }

        sb.AppendLine("200 End.");

        await context.Session.WriteAsync(
            sb.ToString(),
            cancellationToken);
    }
}