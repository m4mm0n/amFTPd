using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Parsers;

/// <summary>
/// Parses user records from a directory containing user flag files in the "userfiles" subdirectory of a specified root
/// path.
/// </summary>
/// <remarks>This parser is intended for use with import workflows that require reading user information from
/// legacy or external sources where each user is represented by a file containing flag data. The parser yields one
/// record per user file found. Only files with non-empty names are processed.</remarks>
public sealed class IoUserParser : IImportParser<ImportedUserRecord>
{
    public IEnumerable<ImportedUserRecord> Parse(string rootPath)
    {
        var usersDir = Path.Combine(rootPath, "userfiles");
        if (!Directory.Exists(usersDir))
            yield break;

        foreach (var file in Directory.GetFiles(usersDir))
        {
            var userName = Path.GetFileName(file);
            if (string.IsNullOrWhiteSpace(userName))
                continue;

            var flags = File.ReadAllText(file);

            var isSiteop = flags.Contains("SITEOP", StringComparison.OrdinalIgnoreCase);
            var isAdmin = flags.Contains("ADMIN", StringComparison.OrdinalIgnoreCase);
            var isNoRatio = flags.Contains("NORATIO", StringComparison.OrdinalIgnoreCase);

            yield return new ImportedUserRecord(
                UserName: userName,
                PrimaryGroup: null,
                SecondaryGroups: Array.Empty<string>(),
                IsSiteop: isSiteop,
                IsAdmin: isAdmin,
                IsNoRatio: isNoRatio,
                CreditsKb: 0);
        }
    }
}