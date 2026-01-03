using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Parsers;

/// <summary>
/// Parses glFTPD nuke records from a "glftpd.nuke" file located in the specified root directory.
/// </summary>
/// <remarks>This parser reads the "glftpd.nuke" file, if present, and yields each valid nuke record as an <see
/// cref="ImportedNukeRecord"/> instance. Lines that are empty, malformed, or do not meet the expected format are
/// ignored. The parser does not throw if the file is missing; it simply yields no results.</remarks>
public sealed class GlNukeParser : IImportParser<ImportedNukeRecord>
{
    public IEnumerable<ImportedNukeRecord> Parse(string rootPath)
    {
        var nukeFile = Path.Combine(rootPath, "glftpd.nuke");
        if (!File.Exists(nukeFile))
            yield break;

        foreach (var line in File.ReadLines(nukeFile))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 5)
                continue;

            var section = parts[0];
            var release = parts[1];

            if (!int.TryParse(parts[2], out var mult))
                continue;

            var nuker = parts[^2];
            var reason = string.Join(
                ' ',
                parts.Skip(3).Take(parts.Length - 5));

            var ts = DateTimeOffset.UtcNow;

            if (long.TryParse(parts[^1], out var unix))
            {
                ts = DateTimeOffset.FromUnixTimeSeconds(unix);
            }

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