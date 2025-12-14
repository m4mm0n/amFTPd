/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteRaceCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:35
 *  Last Modified:  2025-12-14 21:34:38
 *  CRC32:          0xC2BB1017
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

public sealed class SiteRaceCommand : SiteCommandBase
{
    public override string Name => "RACE";
    public override bool RequiresAdmin => false;
    public override string HelpText => "RACE";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {

        // RACE is read-only; usually visible to all logged-in users, no admin check.

        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync(
                "501 Usage: SITE RACE <path>\r\n",
                cancellationToken);
            return;
        }

        var releaseVirt = FtpPath.Normalize(context.Session.Cwd, argument);

        if (!context.RaceEngine.TryGetRace(releaseVirt, out var race))
        {
            await context.Session.WriteAsync("550 No race information for this path.\r\n", cancellationToken);
            return;
        }

        // DUPE DB integration: show [NUKED]/[DUPE] like LASTRACES/RACELOG
        var dupeTag = string.Empty;
        if (context.Runtime.DupeStore is { } dupeStore)
        {
            // Race.ReleasePath is a virtual directory path for the release
            var releaseName = Path.GetFileName(race.ReleasePath.TrimEnd('/', '\\'));
            if (!string.IsNullOrEmpty(releaseName))
            {
                var dupe = dupeStore.Find(race.SectionName, releaseName);
                if (dupe is { IsNuked: true })
                    dupeTag = " [NUKED]";
                else if (dupe is not null)
                    dupeTag = " [DUPE]";
            }
        }

        var totalBytes = race.TotalBytes <= 0 ? 1 : race.TotalBytes; // avoid div-by-zero
        var ordered = race.UserBytes
            .OrderByDescending(kv => kv.Value)
            .ToList();

        await context.Session.WriteAsync(
            $"211-RACE {race.ReleasePath}{dupeTag} (section: {race.SectionName})\r\n",
            cancellationToken);
        await context.Session.WriteAsync("211-User             MB        %\r\n", cancellationToken);
        await context.Session.WriteAsync("211-------------------------------\r\n", cancellationToken);

        foreach (var kv in ordered)
        {
            var user = kv.Key;
            var bytes = kv.Value;
            var mb = bytes / (1024.0 * 1024.0);
            var pct = (double)bytes * 100.0 / totalBytes;

            await context.Session.WriteAsync(
                $"211- {user,-12} {mb,8:0.00} {pct,6:0.0}\r\n",
                cancellationToken);
        }

        await context.Session.WriteAsync("211 End\r\n", cancellationToken);

    }
}