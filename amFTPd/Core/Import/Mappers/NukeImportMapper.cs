using amFTPd.Core.Import.Records;
using amFTPd.Core.Zipscript;
using amFTPd.Logging;

namespace amFTPd.Core.Import.Mappers;

/// <summary>
/// Provides functionality to apply imported nuke records to a zipscript engine and log the corresponding actions.
/// </summary>
/// <remarks>This class is intended for use in scenarios where nuke records, typically imported from external
/// sources, need to be processed and reflected in the system's zipscript state. The class is not thread-safe.</remarks>
public sealed class NukeImportMapper
{
    private readonly IFtpLogger _log;

    public NukeImportMapper(IFtpLogger log) => _log = log;

    public void Apply(
        IEnumerable<ImportedNukeRecord> records,
        ZipscriptEngine zipscript)
    {
        foreach (var r in records)
        {
            zipscript.MarkReleaseNuked(
                virtualReleasePath: r.Path,
                sectionName: r.Section,
                nuker: r.Nuker,
                reason: r.Reason,
                nukeMultiplier: (double)r.Multiplier);

            _log.Log(FtpLogLevel.Info,
                $"[IMPORT][NUKE] {r.Path} " +
                $"x{r.Multiplier} by {r.Nuker} " +
                $"({r.Timestamp:yyyy-MM-dd HH:mm:ss})");
        }
    }
}