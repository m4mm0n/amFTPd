using amFTPd.Config.Ftpd;
using amFTPd.Core.ReleaseSystem;

namespace amFTPd.Core.Vfs.Providers;

/// <summary>
/// Provides a virtual file system (VFS) provider that exposes group-based directories under the "/GROUPS" virtual path.
/// Enables navigation and enumeration of groups and their associated releases within the VFS.
/// </summary>
/// <remarks>The GroupVfsProvider allows clients to browse available groups and their releases by mapping the
/// "/GROUPS" path to a virtual directory structure. Each group appears as a subdirectory under "/GROUPS", and each
/// release for a group appears as a subdirectory under its respective group directory. This provider is typically used
/// in scenarios where releases are organized by group and need to be accessed or listed in a hierarchical manner.
/// Thread safety is determined by the underlying ReleaseRegistry implementation.</remarks>
public sealed class GroupVfsProvider : IVfsProvider
{
    private readonly ReleaseRegistry _registry;

    public GroupVfsProvider(ReleaseRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public bool CanHandle(string virtualPath)
    {
        virtualPath = Normalize(virtualPath);
        return virtualPath.Equals("/GROUPS", StringComparison.OrdinalIgnoreCase)
            || virtualPath.StartsWith("/GROUPS/", StringComparison.OrdinalIgnoreCase);
    }

    public VfsResolveResult Resolve(string virtualPath, FtpUser? user)
    {
        virtualPath = Normalize(virtualPath);

        if (CanHandle(virtualPath))
        {
            return VfsResolveResult.Ok(
                new VfsNode(
                    VfsNodeType.VirtualDirectory,
                    virtualPath,
                    null,
                    null,
                    null));
        }

        return VfsResolveResult.NotFound();
    }

    public IEnumerable<VfsNode> Enumerate(string virtualPath, FtpUser? user)
    {
        virtualPath = Normalize(virtualPath);

        // /GROUPS
        if (virtualPath.Equals("/GROUPS", StringComparison.OrdinalIgnoreCase))
        {
            return _registry.All
                .Select(r => ExtractGroup(r.ReleaseName))
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g)
                .Select(g => new VfsNode(
                    VfsNodeType.VirtualDirectory,
                    $"/GROUPS/{g}",
                    null,
                    null,
                    null));
        }

        // /GROUPS/GROUP
        var parts = virtualPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            var group = parts[1];

            return _registry.All
                .Where(r =>
                {
                    var g = ExtractGroup(r.ReleaseName);
                    return g != null &&
                           g.Equals(group, StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(r => r.LastUpdated)
                .Select(r => new VfsNode(
                    VfsNodeType.VirtualDirectory,
                    $"/GROUPS/{group}/{r.ReleaseName}",
                    null,
                    null,
                    null));
        }

        if (parts.Length == 3)
        {
            var group = parts[1];
            var mode = parts[2].ToUpperInvariant();

            var q = _registry.All
                .Where(r => ExtractGroup(r.ReleaseName)?.Equals(group, StringComparison.OrdinalIgnoreCase) == true);

            if (mode == "TODAY")
            {
                var today = DateTimeOffset.UtcNow.Date;
                q = q.Where(r => r.LastUpdated.Date == today);
            }
            else if (mode == "NUKED")
            {
                q = q.Where(r => r.State == ReleaseState.Nuked);
            }
            else
            {
                return Enumerable.Empty<VfsNode>();
            }

            return q
                .OrderByDescending(r => r.LastUpdated)
                .Select(r => new VfsNode(
                    VfsNodeType.VirtualDirectory,
                    $"/GROUPS/{group}/{r.ReleaseName}",
                    null,
                    null,
                    null));
        }

        return Enumerable.Empty<VfsNode>();
    }

    private static string? ExtractGroup(string release)
    {
        var idx = release.LastIndexOf('-');
        return idx > 0 && idx < release.Length - 1
            ? release[(idx + 1)..]
            : null;
    }

    private static string Normalize(string path)
    {
        path = path.Replace('\\', '/').Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;
        if (path.Length > 1 && path.EndsWith('/'))
            path = path.TrimEnd('/');
        return path;
    }
}