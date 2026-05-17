using System;
using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Parsers;

/// <summary>
/// Parses glFTPd nuke records from known glFTPd nuke logs.
/// </summary>
public sealed class GlNukeParser : IImportParser<ImportedNukeRecord>
{
    public IEnumerable<ImportedNukeRecord> Parse(string rootPath)
    {
        var candidates = new[]
        {
            Path.Combine(rootPath, "glftpd.nuke"),
            Path.Combine(rootPath, "ftp-data", "logs", "nukelog"),
            Path.Combine(rootPath, "ftp-data", "logs", "nukelog.txt")
        };

        string? nukeFile = null;
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                nukeFile = candidate;
                break;
            }
        }

        if (nukeFile is null)
            yield break;

        foreach (var line in File.ReadLines(nukeFile))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var clean = line.Trim();
            if (clean.StartsWith('#'))
                continue;

            var parts = clean.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 5)
                continue;

            var start = 0;
            if (parts.Length >= 2 && parts[0].Equals("NUKE", StringComparison.OrdinalIgnoreCase))
            {
                start = 1;
            }

            if (parts.Length < start + 5)
                continue;

            var section = parts[start];
            var release = parts[start + 1];

            if (!int.TryParse(parts[start + 2], out var mult))
                continue;

            var nuker = parts[^2];
            var reason = string.Join(
                ' ',
                parts.Skip(start + 3).Take(parts.Length - (start + 5)));

            var ts = DateTimeOffset.UtcNow;
            if (long.TryParse(parts[^1], out var unix))
                ts = DateTimeOffset.FromUnixTimeSeconds(unix);

            yield return new ImportedNukeRecord(
                Section: section,
                Path: $"/{section}/{release}",
                Multiplier: mult,
                Reason: reason,
                Nuker: nuker,
                Timestamp: ts);
        }
    }
}
