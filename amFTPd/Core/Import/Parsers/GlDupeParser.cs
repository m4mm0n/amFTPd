using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Parsers;

public sealed class GlDupeParser : IImportParser<ImportedDupeRecord>
{
    public IEnumerable<ImportedDupeRecord> Parse(string rootPath)
    {
        var file = Path.Combine(rootPath, "ftp-data", "misc", "dupefile.txt");
        if (!File.Exists(file))
            yield break;

        foreach (var line in File.ReadLines(file))
        {
            // MP3|Artist-Album-GRP|GRP|1700000000|123456789|NUKED:bad
            var parts = line.Split('|');
            if (parts.Length < 5)
                continue;

            yield return new ImportedDupeRecord
            {
                Section = parts[0],
                Release = parts[1],
                Group = parts[2],
                FirstSeen =
                    DateTimeOffset.FromUnixTimeSeconds(long.Parse(parts[3])),
                TotalBytes = long.Parse(parts[4]),
                IsNuked = parts.Length > 5,
                NukeReason = parts.Length > 5 ? parts[5] : null,
                NukeMultiplier = 1.0
            };
        }
    }
}