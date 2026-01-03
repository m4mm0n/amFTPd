/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteDupeCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-02 03:56:36
 *  Last Modified:  2025-12-14 21:29:44
 *  CRC32:          0xF81D4BF8
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

using amFTPd.Core.Dupe;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteDupeCommand : SiteCommandBase
{
    public override string Name => "DUPE";
    public override bool RequiresAdmin => false;

    public override string HelpText =>
        "DUPE <pattern> [-exact] [-nuked|-ok] [-section=s] [-group=g] " +
        "[-newer=d] [-older=d] [-size>n]";

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

        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync("501 Usage: SITE DUPE <pattern> [filters]\r\n", ct);
            return;
        }

        var tokens = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var pattern = tokens[0];

        var exact = false;
        bool? nuked = null;
        string? group = null;
        string? section = null;
        int? newer = null;
        int? older = null;
        long? minSize = null;
        //int? minFiles = null;
        var limit = 50;

        foreach (var t in tokens.Skip(1))
        {
            if (t.Equals("-exact", StringComparison.OrdinalIgnoreCase))
                exact = true;
            else if (t.Equals("-nuked", StringComparison.OrdinalIgnoreCase))
                nuked = true;
            else if (t.Equals("-ok", StringComparison.OrdinalIgnoreCase))
                nuked = false;
            else if (t.StartsWith("-group=", StringComparison.OrdinalIgnoreCase))
                group = t[7..];
            else if (t.StartsWith("-section=", StringComparison.OrdinalIgnoreCase))
                section = t[9..];
            else if (t.StartsWith("-newer=", StringComparison.OrdinalIgnoreCase))
                newer = int.Parse(t[7..]);
            else if (t.StartsWith("-older=", StringComparison.OrdinalIgnoreCase))
                older = int.Parse(t[7..]);
            else if (t.StartsWith("-size>", StringComparison.OrdinalIgnoreCase))
                minSize = ParseSize(t[6..]);
            //else if (t.StartsWith("-files>", StringComparison.OrdinalIgnoreCase))
            //    minFiles = int.Parse(t[7..]);
        }

        List<DupeEntry> results = new();
        var wildcard = pattern.Contains('*') || pattern.Contains('?');

        if (exact || !wildcard)
        {
            if (section != null)
            {
                var hit = store.Find(section, pattern);
                if (hit != null) results.Add(hit);
            }
            else
            {
                results.AddRange(store.Search(pattern, null, 1)
                    .Where(x => x.ReleaseName.Equals(pattern, StringComparison.OrdinalIgnoreCase)));
            }
        }
        else
        {
            results.AddRange(store.Search(pattern, section, limit));
        }

        var now = DateTimeOffset.UtcNow;

        var filtered = results.Where(d =>
        {
            if (nuked.HasValue && d.IsNuked != nuked.Value)
                return false;

            if (group != null &&
                !string.Equals(d.UploaderGroup, group, StringComparison.OrdinalIgnoreCase))
                return false;

            if (section != null &&
                !string.Equals(d.SectionName, section, StringComparison.OrdinalIgnoreCase))
                return false;

            if (newer.HasValue &&
                (now - d.FirstSeen).TotalDays > newer.Value)
                return false;

            if (older.HasValue &&
                (now - d.FirstSeen).TotalDays < older.Value)
                return false;

            if (minSize.HasValue && d.TotalBytes < minSize.Value)
                return false;

            //if (minFiles.HasValue && d.FileCount < minFiles.Value)
            //    return false;

            return true;
        })
        .OrderBy(x => x.IsNuked)
        .ThenBy(x => x.SectionName)
        .ThenBy(x => x.ReleaseName)
        .Take(limit)
        .ToList();

        if (filtered.Count == 0)
        {
            await context.Session.WriteAsync("211 No matches found.\r\n", ct);
            return;
        }

        await context.Session.WriteAsync("211- Dupe matches:\r\n", ct);

        foreach (var d in filtered)
        {
            var mb = d.TotalBytes / (1024 * 1024.0);
            var groupName = string.IsNullOrEmpty(d.UploaderGroup) ? "UNKNOWN" : d.UploaderGroup;
            var nukedTag = d.IsNuked ? " [NUKED]" : "";

            await context.Session.WriteAsync(
                $" {d.SectionName,-8} {d.ReleaseName,-40} {mb,8:0.0} MB  {groupName}{nukedTag}\r\n",
                ct);
        }

        await context.Session.WriteAsync("211 End of DUPE listing.\r\n", ct);
    }

    static long ParseSize(string text)
    {
        text = text.Trim().ToUpperInvariant();

        var mult = 1L;
        if (text.EndsWith("KB")) { mult = 1024; text = text[..^2]; }
        else if (text.EndsWith("MB")) { mult = 1024 * 1024; text = text[..^2]; }
        else if (text.EndsWith("GB")) { mult = 1024L * 1024 * 1024; text = text[..^2]; }

        return long.Parse(text) * mult;
    }
}