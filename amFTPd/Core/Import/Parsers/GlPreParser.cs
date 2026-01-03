using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Parsers;

/// <summary>
/// Parses glFTPD pre-release records from a "glftpd.pre" file located in the specified root directory.
/// </summary>
/// <remarks>This parser reads the "glftpd.pre" file, if present, and yields an <see cref="ImportedPreRecord"/>
/// for each valid line. Lines that are empty or do not contain at least three space-separated fields are ignored. The
/// parser does not load the entire file into memory, making it suitable for large files. This class is not
/// thread-safe.</remarks>
public sealed class GlPreParser : IImportParser<ImportedPreRecord>
{
    public IEnumerable<ImportedPreRecord> Parse(string rootPath)
    {
        var preFile = Path.Combine(rootPath, "glftpd.pre");
        if (!File.Exists(preFile))
            yield break;

        foreach (var line in File.ReadLines(preFile))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 3)
                continue;

            var section = parts[0];
            var release = parts[1];
            var group = parts[2];

            var ts = DateTimeOffset.UtcNow;

            if (parts.Length >= 4 &&
                long.TryParse(parts[3], out var unix))
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