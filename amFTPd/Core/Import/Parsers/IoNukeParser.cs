using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Parsers;

/// <summary>
/// Parses nuke log files and extracts imported nuke records from a specified root directory.
/// </summary>
/// <remarks>This parser reads the "nuke.log" file located in the provided root path and yields records for each
/// valid nuke entry found. Only lines that conform to the expected nuke log format are parsed; malformed or incomplete
/// lines are ignored. This class is not thread-safe.</remarks>
public sealed class IoNukeParser : IImportParser<ImportedNukeRecord>
{
    public IEnumerable<ImportedNukeRecord> Parse(string rootPath)
    {
        var nukeFile = Path.Combine(rootPath, "nuke.log");
        if (!File.Exists(nukeFile))
            yield break;

        foreach (var line in File.ReadLines(nukeFile))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var clean = line.Trim();

            // Strip timestamp prefix
            if (clean.StartsWith('['))
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

            // NUKE SECTION RELEASE MULT REASON NUKER [TIMESTAMP]
            if (parts.Length < 6)
                continue;

            var section = parts[1];
            var release = parts[2];

            // Multiplier: allow "x3" or "3"
            var multRaw = parts[3].TrimStart('x', 'X');
            if (!int.TryParse(multRaw, out var mult))
                continue;

            var nuker = parts[^2];
            var reason = string.Join(
                ' ',
                parts.Skip(4).Take(parts.Length - 6));

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