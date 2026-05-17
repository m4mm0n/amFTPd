using System;
using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Parsers;

/// <summary>
/// Parses nuke log files and extracts imported nuke records from a specified root directory.
/// </summary>
/// <remarks>
/// Io-style logs are commonly named <c>nuke.log</c>, but some deployments also keep
/// <c>logs\\nuke.log</c> or emit variants with partial tokens.
/// </remarks>
public sealed class IoNukeParser : IImportParser<ImportedNukeRecord>
{
    public IEnumerable<ImportedNukeRecord> Parse(string rootPath)
    {
        var candidates = new[]
        {
            Path.Combine(rootPath, "nuke.log"),
            Path.Combine(rootPath, "logs", "nuke.log")
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
            if (clean.Length == 0 || clean.StartsWith('#'))
                continue;

            // Strip optional timestamp prefix.
            if (clean.StartsWith("[", StringComparison.Ordinal))
            {
                var idx = clean.IndexOf(']');
                if (idx > 0)
                    clean = clean[(idx + 1)..].Trim();
            }

            if (!clean.StartsWith("NUKE", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = clean.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            // Expected minimum: NUKE SECTION RELEASE MULT REASON NUKER [TIMESTAMP]
            if (parts.Length < 6)
                continue;

            var section = parts[1];
            var release = parts[2];

            var multRaw = parts[3].TrimStart('x', 'X');
            if (!int.TryParse(multRaw, out var mult))
                continue;

            var nuker = parts[^2];

            var reason = string.Join(
                " ",
                parts.Skip(4).Take(parts.Length - 5));

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
