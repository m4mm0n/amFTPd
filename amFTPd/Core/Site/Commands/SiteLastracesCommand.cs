/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteLastracesCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:35
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xC398E2D0
 *  
 *  Description:
 *      TODO: Describe this file.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







namespace amFTPd.Core.Site.Commands;

public sealed class SiteLastracesCommand : SiteCommandBase
{
    public override string Name => "LASTRACES";
    public override bool RequiresAdmin => false;
    public override string HelpText => "LASTRACES";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {

        var max = 10;
        if (!string.IsNullOrWhiteSpace(argument) &&
            int.TryParse(argument.Trim(), out var parsed) &&
            parsed > 0 && parsed <= 50)
        {
            max = parsed;
        }

        var races = context.RaceEngine.GetRecentRaces(max);
        if (races.Count == 0)
        {
            await context.Session.WriteAsync("211 No races recorded yet.\r\n", cancellationToken);
            return;
        }

        await context.Session.WriteAsync("211-Last races:\r\n", cancellationToken);
        await context.Session.WriteAsync("211-#  Section  Files  MB      LastUpdate           Path\r\n", cancellationToken);
        await context.Session.WriteAsync("211--------------------------------------------------------------\r\n", cancellationToken);

        var idx = 1;
        var dupeStore = context.Runtime.DupeStore;

        foreach (var race in races)
        {
            var dupeTag = "";
            if (dupeStore is not null)
            {
                var releaseName = Path.GetFileName(race.ReleasePath.TrimEnd('/'));
                var dupe = dupeStore.Find(race.SectionName, releaseName);
                if (dupe is { IsNuked: true })
                    dupeTag = " [NUKED]";
                else if (dupe is not null)
                    dupeTag = " [DUPE]";
            }

            var mb = race.TotalBytes / (1024.0 * 1024.0);
            await context.Session.WriteAsync(
                $"211- {idx,2} {race.SectionName,-8} {race.FileCount,3} files {mb,8:0.0} MB {race.LastUpdatedAt:yyyy-MM-dd HH:mm} {race.ReleasePath}{dupeTag}\r\n",
                cancellationToken);

            idx++;
        }

        await context.Session.WriteAsync("211 End\r\n", cancellationToken);

    }
}