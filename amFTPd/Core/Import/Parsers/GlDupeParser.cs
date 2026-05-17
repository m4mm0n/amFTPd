using System;
using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Parsers;

public sealed class GlDupeParser : IImportParser<ImportedDupeRecord>
{
    public IEnumerable<ImportedDupeRecord> Parse(string rootPath)
    {
        var candidates = new[]
        {
            Path.Combine(rootPath, "ftp-data", "misc", "dupefile.txt"),
            Path.Combine(rootPath, "ftp-data", "misc", "dupefile"),
            Path.Combine(rootPath, "ftp-data", "logs", "dupefile"),
            Path.Combine(rootPath, "ftp-data", "logs", "dupefile.txt"),
            Path.Combine(rootPath, "dupefile.txt"),
            Path.Combine(rootPath, "dupefile")
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

        foreach (var line in File.ReadLines(file))
        {
            var raw = line.Trim();
            if (raw.Length == 0 || raw.StartsWith('#'))
                continue;

            var parts = raw.Contains('|')
                ? raw.Split('|')
                : raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 5)
                continue;

            if (!long.TryParse(parts[3].Trim(), out var ts))
                continue;
            if (!long.TryParse(parts[4].Trim(), out var size))
                continue;

            var nukeReason = parts.Length > 5 ? parts[5].Trim() : null;
            var isNuked = !string.IsNullOrWhiteSpace(nukeReason);

            yield return new ImportedDupeRecord
            {
                Section = parts[0].Trim(),
                Release = parts[1].Trim(),
                Group = string.IsNullOrWhiteSpace(parts[2])
                    ? "UNKNOWN"
                    : parts[2].Trim(),
                FirstSeen = DateTimeOffset.FromUnixTimeSeconds(ts),
                TotalBytes = size,
                IsNuked = isNuked,
                NukeReason = nukeReason,
                NukeMultiplier = 1.0
            };
        }
    }
}
