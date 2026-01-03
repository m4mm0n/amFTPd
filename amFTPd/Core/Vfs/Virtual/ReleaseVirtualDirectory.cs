using amFTPd.Core.ReleaseSystem;

namespace amFTPd.Core.Vfs.Virtual;

/// <summary>
/// Represents a virtual directory of completed releases, providing methods to enumerate release sections and releases
/// updated today.
/// </summary>
/// <remarks>This class provides a virtualized view over a release registry, exposing only releases that are in
/// the completed state. It is intended for scenarios where a logical directory structure of releases is needed, such as
/// in virtual file system integrations or UI navigation trees. Instances of this class are not thread-safe.</remarks>
public sealed class ReleaseVirtualDirectory
{
    private readonly ReleaseRegistry _registry;

    public ReleaseVirtualDirectory(ReleaseRegistry registry) => _registry = registry;

    public IEnumerable<VfsNode> ListSection(string section)
    {
        foreach (var r in _registry.EnumerateBySection(section))
        {
            if (r.State != ReleaseState.Complete)
                continue;

            yield return new VfsNode(
                VfsNodeType.VirtualDirectory,
                $"/{section}/{r.ReleaseName}",
                null,
                null,
                null);
        }
    }

    public IEnumerable<VfsNode> ListToday(string? section = null)
    {
        var today = DateTimeOffset.UtcNow.Date;

        foreach (var r in _registry.All)
        {
            if (r.State != ReleaseState.Complete)
                continue;

            if (r.LastUpdated.Date != today)
                continue;

            if (section != null &&
                !r.Section.Equals(section, StringComparison.OrdinalIgnoreCase))
                continue;

            yield return new VfsNode(
                VfsNodeType.VirtualDirectory,
                $"/{r.Section}/{r.ReleaseName}",
                null,
                null,
                null);
        }
    }
}
