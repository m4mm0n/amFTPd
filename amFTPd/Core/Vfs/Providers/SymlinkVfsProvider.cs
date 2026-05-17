/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SymlinkVfsProvider.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-24 00:00:00
 *  Last Modified:  2026-04-24 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      VFS provider that intercepts registered symlink paths and delegates
 *      resolution/enumeration to the target path via the parent VfsManager.
 *
 *      Resolution strategy:
 *        - CanHandle: true if the virtual path (or any prefix segment) is a known link.
 *        - Resolve:   rewrite link prefix → target prefix, then ask VfsManager.
 *        - Enumerate: same rewrite, then ask VfsManager.
 *
 *      Dangling symlinks (target does not resolve) return a descriptive 550 error.
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

using amFTPd.Config.Ftpd;

namespace amFTPd.Core.Vfs.Providers;

/// <summary>
/// VFS provider that resolves virtual symlinks registered in a <see cref="VfsSymlinkStore"/>.
/// </summary>
/// <remarks>
/// This provider sits near the top of the provider chain (just after Pre/Release/Group providers,
/// but BEFORE the Physical provider) so that symlinks override the underlying filesystem view.
/// It holds a back-reference to its owning <see cref="VfsManager"/> so it can delegate target
/// resolution without duplicating the full provider chain logic.
/// </remarks>
public sealed class SymlinkVfsProvider : IVfsProvider
{
    private readonly VfsSymlinkStore _store;

    // Set after construction once VfsManager itself is available.
    // This avoids a circular constructor dependency.
    internal VfsManager? Owner { get; set; }

    public SymlinkVfsProvider(VfsSymlinkStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    // ------------------------------------------------------------------
    // IVfsProvider
    // ------------------------------------------------------------------

    public bool CanHandle(string virtualPath)
    {
        // We handle:
        //   /LINK_PATH          — the link itself
        //   /LINK_PATH/sub/dir  — anything below the link
        // Try to find a symlink entry whose normalised path is a prefix of virtualPath.
        return FindMatchingLink(virtualPath) is not null;
    }

    public VfsResolveResult Resolve(string virtualPath, FtpUser? user)
    {
        var (link, remainder) = FindMatchingLink(virtualPath) ?? default;
        if (link is null)
            return VfsResolveResult.NotFound();

        var targetPath = RewritePath(link, remainder!);

        if (Owner is null)
            return VfsResolveResult.NotFound("Symlink provider not wired to VfsManager.");

        var result = Owner.ResolveSkipSymlinks(targetPath, user);
        if (!result.Success)
            return VfsResolveResult.NotFound($"Dangling symlink: {link.LinkPath} → {link.TargetPath} (target not found)");

        // Return a node with the original virtual path so the client sees the link path.
        var node = result.Node!;
        return VfsResolveResult.Ok(node with { VirtualPath = virtualPath });
    }

    public IEnumerable<VfsNode> Enumerate(string virtualPath, FtpUser? user)
    {
        var (link, remainder) = FindMatchingLink(virtualPath) ?? default;
        if (link is null || Owner is null)
            return Enumerable.Empty<VfsNode>();

        var targetPath = RewritePath(link, remainder!);
        return Owner.EnumerateSkipSymlinks(targetPath, user);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Finds the longest symlink entry that is a prefix of (or equals) <paramref name="virtualPath"/>.
    /// Returns (symlink, remainder) where remainder is the path below the link root, e.g. "/sub/dir".
    /// </summary>
    private (VfsSymlink link, string remainder)? FindMatchingLink(string virtualPath)
    {
        VfsSymlink? best = null;
        string? bestRemainder = null;

        foreach (var link in _store.GetAll())
        {
            var lp = link.LinkPath; // already normalised, starts with /

            if (virtualPath.Equals(lp, StringComparison.OrdinalIgnoreCase))
            {
                // exact match — use it directly
                return (link, "");
            }

            // prefix match: virtualPath starts with lp + "/"
            if (virtualPath.StartsWith(lp + "/", StringComparison.OrdinalIgnoreCase))
            {
                var rem = virtualPath[lp.Length..]; // includes leading /
                // pick longest matching link (most specific)
                if (best is null || lp.Length > best.LinkPath.Length)
                {
                    best = link;
                    bestRemainder = rem;
                }
            }
        }

        return best is null ? null : (best, bestRemainder!);
    }

    private static string RewritePath(VfsSymlink link, string remainder)
    {
        // remainder is "" or "/sub/path"
        var target = link.TargetPath; // normalised, no trailing slash
        return string.IsNullOrEmpty(remainder) ? target : target + remainder;
    }
}
