using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Parsers;

public sealed class GlGroupParser : IImportParser<ImportedGroupRecord>
{
    public IEnumerable<ImportedGroupRecord> Parse(string rootPath)
    {
        var file = Path.Combine(rootPath, "group");
        if (!File.Exists(file))
            yield break;

        foreach (var line in File.ReadLines(file))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            var parts = line.Split(':');
            if (parts.Length < 4)
                continue;

            var groupName = parts[0];

            yield return new ImportedGroupRecord(
                GroupName: groupName,
                DefaultRatio: 1.0,       // overridden by ratio.conf later
                IsPrivileged: false);
        }
    }
}