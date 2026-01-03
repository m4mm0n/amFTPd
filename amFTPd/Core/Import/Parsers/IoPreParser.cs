using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Parsers;

/// <summary>
/// Parses pre-release records from a pre.log file located in the specified root directory.
/// </summary>
/// <remarks>This parser reads lines from a file named "pre.log" in the given directory and extracts pre-release
/// information for import. Only lines that match the expected format are parsed; others are ignored. The parser yields
/// one ImportedPreRecord per valid entry. This class is not thread-safe.</remarks>
public sealed class IoPreParser : IImportParser<ImportedPreRecord>
{
    public IEnumerable<ImportedPreRecord> Parse(string rootPath)
    {
        var preFile = Path.Combine(rootPath, "pre.log");
        if (!File.Exists(preFile))
            yield break;

        foreach (var line in File.ReadLines(preFile))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var clean = line.Trim();

            // Strip bracketed timestamps
            if (clean.StartsWith('['))
            {
                var idx = clean.IndexOf(']');
                if (idx > 0)
                    clean = clean[(idx + 1)..].Trim();
            }

            // Expect "PRE ..."
            if (!clean.StartsWith("PRE", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = clean.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            // PRE SECTION RELEASE GROUP [TIMESTAMP]
            if (parts.Length < 4)
                continue;

            var section = parts[1];
            var release = parts[2];
            var group = parts[3];

            var ts = DateTimeOffset.UtcNow;

            if (parts.Length >= 5 &&
                long.TryParse(parts[^1], out var unix))
            {
                ts = DateTimeOffset.FromUnixTimeSeconds(unix);
            }

            yield return new ImportedPreRecord(
                Section: section,
                Path: $"/{section}/{release}",
                Group: group,
                Timestamp: ts);
        }
    }
}