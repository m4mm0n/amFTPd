using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Parsers;

public sealed class GlUserParser : IImportParser<ImportedUserRecord>
{
    public IEnumerable<ImportedUserRecord> Parse(string rootPath)
    {
        var file = Path.Combine(rootPath, "passwd");
        if (!File.Exists(file))
            yield break;

        foreach (var line in File.ReadLines(file))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            var parts = line.Split(':');
            if (parts.Length < 5)
                continue;

            var user = parts[0];
            var flags = parts[4];

            yield return new ImportedUserRecord(
                UserName: user,
                PrimaryGroup: "DEFAULT",
                SecondaryGroups: Array.Empty<string>(),
                IsSiteop: flags.Contains('1'),
                IsAdmin: flags.Contains('6'),
                IsNoRatio: flags.Contains('N'),
                CreditsKb: 0);
        }
    }
}