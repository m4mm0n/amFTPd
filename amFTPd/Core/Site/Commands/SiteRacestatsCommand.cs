/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteRacestatsCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:35
 *  Last Modified:  2025-12-14 21:34:52
 *  CRC32:          0x98A63D24
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

public sealed class SiteRacestatsCommand : SiteCommandBase
{
    public override string Name => "RACESTATS";
    public override bool RequiresAdmin => false;
    public override string HelpText => "RACESTATS";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {

        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync(
                "501 Usage: SITE RACESTATS <path>\r\n",
                cancellationToken);
            return;
        }

        var releaseVirt = FtpPath.Normalize(context.Session.Cwd, argument);

        if (!context.RaceEngine.TryGetRace(releaseVirt, out var race))
        {
            await context.Session.WriteAsync("550 No race information for this path.\r\n", cancellationToken);
            return;
        }

        var totalBytes = race.TotalBytes;
        var totalMb = totalBytes / (1024.0 * 1024.0);
        var ordered = race.UserBytes
            .OrderByDescending(kv => kv.Value)
            .ToList();

        await context.Session.WriteAsync(
            $"211-RACESTATS {race.ReleasePath}\r\n",
            cancellationToken);
        await context.Session.WriteAsync($"211- Section:       {race.SectionName}\r\n", cancellationToken);
        await context.Session.WriteAsync($"211- Started:       {race.StartedAt:yyyy-MM-dd HH:mm:ss zzz}\r\n", cancellationToken);
        await context.Session.WriteAsync($"211- Last Update:   {race.LastUpdatedAt:yyyy-MM-dd HH:mm:ss zzz}\r\n", cancellationToken);
        await context.Session.WriteAsync($"211- Files:         {race.FileCount}\r\n", cancellationToken);
        await context.Session.WriteAsync($"211- Total Bytes:   {totalBytes}\r\n", cancellationToken);
        await context.Session.WriteAsync($"211- Total MB:      {totalMb:0.00}\r\n", cancellationToken);
        await context.Session.WriteAsync("211-\r\n", cancellationToken);
        await context.Session.WriteAsync("211-User             MB        %\r\n", cancellationToken);
        await context.Session.WriteAsync("211-------------------------------\r\n", cancellationToken);

        var denom = totalBytes <= 0 ? 1 : totalBytes;

        foreach (var kv in ordered)
        {
            var user = kv.Key;
            var bytes = kv.Value;
            var mb = bytes / (1024.0 * 1024.0);
            var pct = bytes * 100.0 / denom;

            await context.Session.WriteAsync(
                $"211- {user,-12} {mb,8:0.00} {pct,6:0.0}\r\n",
                cancellationToken);
        }

        await context.Session.WriteAsync("211 End\r\n", cancellationToken);

    }
}