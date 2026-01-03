using amFTPd.Core.Dupe;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteDupeFullCommand : SiteCommandBase
{
    public override string Name => "DUPEFULL";
    public override bool RequiresSiteop => true;

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

        if (entries.Count == 0)
        {
            await context.Session.WriteAsync("211 No matches found.\r\n", ct);
            return;
        }

        await context.Session.WriteAsync("211- Detailed DUPE listing:\r\n", ct);

        foreach (var d in entries)
        {
            await context.Session.WriteAsync(
                $"Section : {d.Section}\r\n" +
                $"Release : {d.ReleaseName}\r\n" +
                $"Group   : {d.Group}\r\n" +
                $"Date    : {d.ReleaseDate:u}\r\n" +
                $"Size    : {d.TotalBytes / (1024 * 1024.0):0.0} MB\r\n" +
                $"Files   : {d.FileCount}\r\n" +
                $"Archives: {d.ArchiveCount}\r\n" +
                $"SFV/NFO/DIZ: {d.HasSfv}/{d.HasNfo}/{d.HasDiz}\r\n" +
                $"Nuked   : {(d.IsNuked ? "YES" : "NO")}\r\n" +
                $"Multiplier: {d.NukeMultiplier}\r\n\r\n",
                ct);
        }

        await context.Session.WriteAsync("211 End of DUPEFULL.\r\n", ct);
    }
}