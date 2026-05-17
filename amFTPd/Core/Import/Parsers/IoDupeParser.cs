using System;
using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Parsers;

public sealed class IoDupeParser : IImportParser<ImportedDupeRecord>
{
    public IEnumerable<ImportedDupeRecord> Parse(string rootPath)
    {
        var candidates = new[]
        {
            Path.Combine(rootPath, "ioDUPE.db"),
            Path.Combine(rootPath, "dupefile.txt"),
            Path.Combine(rootPath, "dupelog"),
            Path.Combine(rootPath, "dupelog.txt"),
            Path.Combine(rootPath, "ftp-data", "misc", "dupefile.txt"),
            Path.Combine(rootPath, "ftp-data", "misc", "dupefile")
        };

        string? file = null;

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                file = c;
                break;
            }
        }

        if (file is null)
            yield break;

        foreach (var rawLine in File.ReadLines(file))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            var line = rawLine.Trim();
            if (line.StartsWith('#'))
                continue;

            var parts = line.Contains('|')
                ? line.Split('|')
                : line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 5)
                continue;

            var section = parts[0].Trim();
            var release = parts[1].Trim();
            var group = parts[2].Trim();

            if (!long.TryParse(parts[3].Trim(), out var ts))
                continue;

            if (!long.TryParse(parts[4].Trim(), out var size))
                continue;

            var isNuked = false;
            string? nukeReason = null;

            if (parts.Length > 5 &&
                parts[5].StartsWith("NUKED", StringComparison.OrdinalIgnoreCase))
            {
                isNuked = true;

                var idx = parts[5].IndexOf(':');
                if (idx > 0 && idx + 1 < parts[5].Length)
                {
                    nukeReason = parts[5][(idx + 1)..].Trim();
                }
                else if (parts.Length > 6)
                {
                    nukeReason = string.Join(' ', parts.Skip(5)).Replace("NUKED", "", StringComparison.OrdinalIgnoreCase).Trim();
                }
            }

            yield return new ImportedDupeRecord
            {
                Section = section,
                Release = release,
                Group = string.IsNullOrEmpty(group) ? "UNKNOWN" : group,
                FirstSeen = DateTimeOffset.FromUnixTimeSeconds(ts),
                TotalBytes = size,
                IsNuked = isNuked,
                NukeReason = nukeReason,
                NukeMultiplier = 1.0
            };
        }
    }
}
