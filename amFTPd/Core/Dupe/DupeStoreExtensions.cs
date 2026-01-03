namespace amFTPd.Core.Dupe;

/// <summary>
/// Provides extension methods for converting an IDupeStore to a portable scene dupe database.
/// </summary>
public static class DupeStoreExtensions
{
    /// <summary>
    /// Convert the entire runtime DupeStore into a portable scene dupe database.
    /// </summary>
    public static IReadOnlyList<SceneDupeEntry> ToSceneDupeDb(
        this IDupeStore store)
    {
        if (store is null)
            throw new ArgumentNullException(nameof(store));

        // We rely on Search("*") to enumerate everything
        var all = store.Search("*", null, int.MaxValue);

        var list = new List<SceneDupeEntry>(all.Count);

        foreach (var e in all)
        {
            list.Add(new SceneDupeEntry
            {
                Section = e.SectionName,
                ReleaseName = e.ReleaseName,
                Group = e.UploaderGroup ?? "UNKNOWN",
                ReleaseDate = e.FirstSeen,

                TotalBytes = e.TotalBytes,

                // These can be refined later when DupeRelease replaces DupeEntry
                FileCount = 0,
                ArchiveCount = 0,

                HasSfv = false,
                HasNfo = false,
                HasDiz = false,

                Crc32 = new Dictionary<string, uint>(),

                IsNuked = e.IsNuked,
                NukeReason = e.NukeReason,
                NukeMultiplier = e.NukeMultiplier
            });
        }

        return list;
    }
}