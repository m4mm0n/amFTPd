using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Parsers;

public sealed class GlRatioParser : IImportParser<ImportedRatioRecord>
{
    public IEnumerable<ImportedRatioRecord> Parse(string rootPath)
    {
        var file = Path.Combine(rootPath, "ratio.conf");
        if (!File.Exists(file))
            yield break;

        foreach (var line in File.ReadLines(file))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                continue;

            var ratioParts = parts[1].Split(':');
            if (ratioParts.Length != 2)
                continue;

            if (!double.TryParse(ratioParts[0], out var r))
                continue;

            yield return new ImportedRatioRecord(
                Target: parts[0],
                IsUser: parts[0].Equals("USER", StringComparison.OrdinalIgnoreCase),
                Ratio: r);
        }
    }
}