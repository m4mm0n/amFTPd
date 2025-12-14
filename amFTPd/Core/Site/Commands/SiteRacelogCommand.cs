/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteRacelogCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:35
 *  Last Modified:  2025-12-14 21:34:44
 *  CRC32:          0xD3435356
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
 * ====================================================================================================
 */


namespace amFTPd.Core.Site.Commands;

public sealed class SiteRacelogCommand : SiteCommandBase
{
    public override string Name => "RACELOG";
    public override bool RequiresAdmin => false;
    public override string HelpText => "RACELOG";

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

        await context.Session.WriteAsync("211-RACELOG (most recent first)\r\n", cancellationToken);

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
                $"211- Path:    {race.ReleasePath}{dupeTag}\r\n" +
                $"211- Section: {race.SectionName}\r\n" +
                $"211- Started: {race.StartedAt:yyyy-MM-dd HH:mm:ss zzz}\r\n" +
                $"211- Last:    {race.LastUpdatedAt:yyyy-MM-dd HH:mm:ss zzz}\r\n" +
                $"211- Files:   {race.FileCount}\r\n" +
                $"211- MB:      {mb:0.00}\r\n" +
                "211-\r\n",
                cancellationToken);
        }

        await context.Session.WriteAsync("211 End\r\n", cancellationToken);

    }
}