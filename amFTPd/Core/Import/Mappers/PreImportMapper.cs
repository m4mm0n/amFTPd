using amFTPd.Core.Import.Records;
using amFTPd.Core.Pre;

namespace amFTPd.Core.Import.Mappers;

/// <summary>
/// Provides functionality to map a collection of imported pre'd records into entries within a pre'd
/// registry.
/// </summary>
/// <remarks>This class is intended for use when importing pre-release data from external sources and registering
/// them in a central registry. Instances of this class are not intended to be inherited.</remarks>
public sealed class PreImportMapper
{
    public void Apply(
        IEnumerable<ImportedPreRecord> records,
        PreRegistry registry)
    {
        foreach (var r in records)
        {
            if (string.IsNullOrWhiteSpace(r.Path))
                continue;

            // derive release name from path
            var releaseName = r.Path
                .TrimEnd('/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();

            if (string.IsNullOrEmpty(releaseName))
                continue;

            var entry = new PreEntry(
                Section: r.Section,
                ReleaseName: releaseName,
                VirtualPath: r.Path,
                User: r.Group,          // group == PRE label for imports
                Timestamp: r.Timestamp);

            registry.TryAdd(entry);
        }
    }
}