using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Parsers;

/// <summary>
/// Parses group information from a directory structure and returns imported group records.
/// </summary>
/// <remarks>This parser expects a subdirectory named "groups" within the specified root path. Each subdirectory
/// within "groups" is interpreted as a separate group, and a corresponding ImportedGroupRecord is created for each. The
/// parser yields no records if the "groups" directory does not exist. This class is not thread-safe.</remarks>
public sealed class IoGroupParser : IImportParser<ImportedGroupRecord>
{
    public IEnumerable<ImportedGroupRecord> Parse(string rootPath)
    {
        var groupsDir = Path.Combine(rootPath, "groups");
        if (!Directory.Exists(groupsDir))
            yield break;

        foreach (var dir in Directory.GetDirectories(groupsDir))
        {
            var groupName = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(groupName))
                continue;

            yield return new ImportedGroupRecord(
                GroupName: groupName,
                DefaultRatio: 1.0,
                IsPrivileged: false);
        }
    }
}