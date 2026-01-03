using amFTPd.Config.Ftpd;
using amFTPd.Core.ReleaseSystem;

namespace amFTPd.Core.Vfs.Providers;

/// <summary>
/// Provides a virtual file system (VFS) provider that exposes release directories organized by section and release
/// name, using a release registry as the data source.
/// </summary>
/// <remarks>This provider enables navigation and resolution of virtual paths in the format "/SECTION" and
/// "/SECTION/RELEASE". It is typically used in FTP or similar virtualized environments to present release data as a
/// hierarchical directory structure. Only releases marked as visible in the registry are exposed through this
/// provider.</remarks>
public sealed class ReleaseVfsProvider : IVfsProvider
{
    private readonly ReleaseRegistry _registry;

    public ReleaseVfsProvider(ReleaseRegistry registry)
        => _registry = registry;

    public bool CanHandle(string virtualPath)
    {
        if (virtualPath == "/") return false;

        return virtualPath.Equals("/TODAY", StringComparison.OrdinalIgnoreCase)
            || virtualPath.Equals("/0DAY", StringComparison.OrdinalIgnoreCase)
            || virtualPath.StartsWith("/TODAY-", StringComparison.OrdinalIgnoreCase)
            || virtualPath.Equals("/NUKED", StringComparison.OrdinalIgnoreCase)
            || virtualPath.Equals("/INCOMPLETE", StringComparison.OrdinalIgnoreCase)
            || virtualPath.Equals("/ARCHIVE", StringComparison.OrdinalIgnoreCase)
            || IsSectionRoot(virtualPath)
            || IsSectionRelease(virtualPath);
    }

    public VfsResolveResult Resolve(string virtualPath, FtpUser? user)
    {
        virtualPath = Normalize(virtualPath);

        if (IsSectionRelease(virtualPath))
        {
            var (section, release) = Split(virtualPath);

            if (_registry.TryGet(section, release, out var ctx) &&
                ctx.IsVisible)
            {
                return VfsResolveResult.Ok(
                    new VfsNode(
                        VfsNodeType.VirtualDirectory,
                        virtualPath,
                        null,
                        null,
                        null));
            }
        }

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

        var src = Enumerable.Empty<ReleaseContext>();

        if (virtualPath.Equals("/TODAY", StringComparison.OrdinalIgnoreCase) ||
            virtualPath.Equals("/0DAY", StringComparison.OrdinalIgnoreCase))
        {
            src = _registry.All.Where(IsCompletedToday);
        }
        else if (virtualPath.StartsWith("/TODAY-", StringComparison.OrdinalIgnoreCase))
        {
            var section = virtualPath[7..];
            src = _registry.EnumerateBySection(section).Where(IsCompletedToday);
        }
        else if (virtualPath.Equals("/NUKED", StringComparison.OrdinalIgnoreCase))
        {
            src = _registry.All.Where(r => r.State == ReleaseState.Nuked);
        }
        else if (virtualPath.Equals("/INCOMPLETE", StringComparison.OrdinalIgnoreCase))
        {
            src = _registry.All.Where(r => r.State == ReleaseState.Incomplete);
        }
        else if (virtualPath.Equals("/ARCHIVE", StringComparison.OrdinalIgnoreCase))
        {
            var today = DateTimeOffset.UtcNow.Date;
            src = _registry.All.Where(r =>
                r.State == ReleaseState.Complete &&
                r.LastUpdated.Date < today);
        }
        else if (IsSectionRoot(virtualPath))
        {
            var section = virtualPath.TrimStart('/');
            src = _registry.EnumerateBySection(section)
                .Where(r => r.State == ReleaseState.Complete);
        }

        return src
            .OrderByDescending(r => r.LastUpdated)
            .Select(r =>
                new VfsNode(
                    VfsNodeType.VirtualDirectory,
                    $"{virtualPath}/{r.ReleaseName}",
                    null,
                    null,
                    null));
    }

    // helpers
    private static bool IsCompletedToday(ReleaseContext r)
        => r.State == ReleaseState.Complete &&
           r.LastUpdated.Date == DateTimeOffset.UtcNow.Date;

    private static bool IsSectionRoot(string path)
        => path.Count(c => c == '/') == 1;

    private static bool IsSectionRelease(string path)
        => path.Count(c => c == '/') == 2;

    private static (string section, string release) Split(string path)
    {
        var p = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return (p[0], p[1]);
    }

    private static string Normalize(string p)
    {
        p = p.Replace('\\', '/');
        if (!p.StartsWith("/")) p = "/" + p;
        return p.TrimEnd('/');
    }
}