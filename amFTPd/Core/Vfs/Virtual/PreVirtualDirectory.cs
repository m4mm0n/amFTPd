using amFTPd.Config.Ftpd;
using amFTPd.Core.Pre;

namespace amFTPd.Core.Vfs.Virtual;

/// <summary>
/// Virtual /PRE directory.
/// Enforces group-based access at VFS level.
/// </summary>
public sealed class PreVirtualDirectory
{
    private readonly PreRegistry _pres;

    public PreVirtualDirectory(PreRegistry pres) => _pres = pres;

    public VfsResolveResult Resolve(string virtualPath, FtpUser? user)
    {
        virtualPath = virtualPath.TrimEnd('/');

        // /PRE (siteops only)
        if (virtualPath.Equals("/PRE", StringComparison.OrdinalIgnoreCase))
        {
            if (user is null || !user.IsSiteop)
                return VfsResolveResult.Denied();

            return VfsResolveResult.Ok(CreateVirtualDir("/PRE"));
        }

        var parts = virtualPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        switch (parts.Length)
        {
            // /PRE/<group>
            case 2:
                {
                    var group = parts[1];

                    if (!CanAccessGroup(user, group))
                        return VfsResolveResult.Denied();

                    if (!_pres.All.Any(p =>
                            p.Section.Equals(group, StringComparison.OrdinalIgnoreCase)))
                        return VfsResolveResult.NotFound();

                    return VfsResolveResult.Ok(CreateVirtualDir(virtualPath));
                }

            // /PRE/<group>/<release>
            case 3:
                {
                    var group = parts[1];
                    var release = parts[2];

                    if (!CanAccessGroup(user, group))
                        return VfsResolveResult.Denied();

                    var exists = _pres.All.Any(p =>
                        p.Section.Equals(group, StringComparison.OrdinalIgnoreCase) &&
                        p.ReleaseName.Equals(release, StringComparison.OrdinalIgnoreCase));

                    return exists
                        ? VfsResolveResult.Ok(CreateVirtualDir(virtualPath))
                        : VfsResolveResult.NotFound();
                }
        }

        return VfsResolveResult.NotFound();
    }

    public IEnumerable<VfsNode> Enumerate(string virtualPath)
    {
        virtualPath = Normalize(virtualPath);

        if (virtualPath.Equals("/PRE", StringComparison.OrdinalIgnoreCase))
        {
            return _pres.All
                .OrderByDescending(p => p.Timestamp)
                .Select(p => new VfsNode(
                    VfsNodeType.VirtualDirectory,
                    $"/PRE/{p.ReleaseName}",
                    null,
                    null,
                    null));
        }

        if (virtualPath.Equals("/PRE/TODAY", StringComparison.OrdinalIgnoreCase))
        {
            var today = DateTimeOffset.UtcNow.Date;

            return _pres.All
                .Where(p => p.Timestamp.Date == today)
                .OrderByDescending(p => p.Timestamp)
                .Select(p => new VfsNode(
                    VfsNodeType.VirtualDirectory,
                    $"/PRE/{p.ReleaseName}",
                    null,
                    null,
                    null));
        }

        if (IsSection(virtualPath))
        {
            var section = virtualPath[5..]; // "/PRE/"
            return _pres.GetByGroup(section)
                .OrderByDescending(p => p.Timestamp)
                .Select(p => new VfsNode(
                    VfsNodeType.VirtualDirectory,
                    $"/PRE/{p.ReleaseName}",
                    null,
                    null,
                    null));
        }

        return Enumerable.Empty<VfsNode>();
    }

    private static bool IsSection(string path)
        => path.StartsWith("/PRE/", StringComparison.OrdinalIgnoreCase)
           && path.Count(c => c == '/') == 1;

    private static string Normalize(string path)
    {
        path = path.Replace('\\', '/').Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;
        if (path.Length > 1 && path.EndsWith('/'))
            path = path.TrimEnd('/');
        return path;
    }


    private static VfsNode CreateVirtualDir(string path) =>
        new(
            VfsNodeType.VirtualDirectory,
            path,
            null,
            null,
            null
        );

    private static bool CanAccessGroup(FtpUser? user, string group)
    {
        if (user is null)
            return false;

        if (user.IsSiteop)
            return true;

        return user.GroupName != null &&
               user.GroupName.Equals(group, StringComparison.OrdinalIgnoreCase);
    }
}
