using amFTPd.Core.Import.Records;
using amFTPd.Db;

namespace amFTPd.Core.Import.Mappers;

/// <summary>
/// Provides functionality to import group records into a group store, adding new groups that do not already exist.
/// </summary>
public sealed class GroupImportMapper
{
    public void Apply(
        IEnumerable<ImportedGroupRecord> records,
        IGroupStore groups)
    {
        foreach (var g in records)
        {
            if (groups.FindGroup(g.GroupName) is not null)
                continue;

            var group = new FtpGroup(
                GroupName: g.GroupName,
                Description: "Imported",
                Users: new List<string>(),
                SectionCredits: new Dictionary<string, long>());

            groups.TryAddGroup(group, out _);
        }
    }
}