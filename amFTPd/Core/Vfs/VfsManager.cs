/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           VfsManager.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-13 04:40:48
 *  CRC32:          0x4E47346E
 *  
 *  Description:
 *      Manages a virtual file system (VFS) by resolving virtual paths to physical file system paths, handling user-specific...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */

using amFTPd.Config.Ftpd;
using amFTPd.Config.Vfs;
using amFTPd.Core.Pre;
using amFTPd.Core.ReleaseSystem;
using amFTPd.Core.Sections;
using amFTPd.Core.Vfs.Providers;

namespace amFTPd.Core.Vfs;

/// <summary>
/// Manages a virtual file system (VFS) by resolving virtual paths to physical file system paths, handling
/// user-specific mounts, global mounts, and virtual files.
/// </summary>
/// <remarks>The <see cref="VfsManager"/> class provides functionality to map virtual paths to physical
/// paths based on a configuration that includes global mounts, user-specific mounts, and virtual files. It also
/// enforces policies such as file size limits, hidden file restrictions, and denied file extensions. This class is
/// designed to work with an FTP file system and supports caching for improved performance.</remarks>
public sealed class VfsManager
{
    private readonly VfsCache _cache;
    private readonly List<IVfsProvider> _providers;

    public VfsManager(
        IEnumerable<VfsMount> mounts,
        IEnumerable<VfsUserMount> userMounts,
        ReleaseRegistry releaseRegistry,
        SectionResolver sectionResolver,
        PreRegistry preRegistry)
    {
        ArgumentNullException.ThrowIfNull(sectionResolver);
        ArgumentNullException.ThrowIfNull(preRegistry);
        ArgumentNullException.ThrowIfNull(releaseRegistry);

        _cache = new VfsCache(TimeSpan.FromSeconds(5));

        // IMPORTANT: provider order = priority
        _providers = new List<IVfsProvider>
        {
            // IMPORTANT: provider order = priority
            // 1) Pure virtual namespaces
            new PreVfsProvider(preRegistry),
            new ReleaseVfsProvider(releaseRegistry),
            new GroupVfsProvider(releaseRegistry),

            // 2) Physical filesystem (wins when a real dir/file exists)
            new PhysicalVfsProvider(
                mounts?.ToList() ?? [],
                userMounts?.ToList() ?? [],
                sectionResolver),

            // 3) Section shortcuts (used when the physical target does not exist)
            new ShortcutVfsProvider(sectionResolver)
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------------------------
    public VfsResolveResult Resolve(string virtualPath, FtpUser? user)
    {
        if (string.IsNullOrWhiteSpace(virtualPath))
            return VfsResolveResult.NotFound();

        virtualPath = NormalizeVirtualPath(virtualPath);

        if (_cache.TryGet(virtualPath, out var cached))
            return cached;

        foreach (var provider in _providers)
        {
            if (!provider.CanHandle(virtualPath))
                continue;

            var result = provider.Resolve(virtualPath, user);
            if (result.Success)
            {
                _cache.Set(virtualPath, result);
                return result;
            }
        }

        return VfsResolveResult.NotFound();
    }

    /// <summary>
    /// Enumerates child nodes for a given virtual directory path.
    /// </summary>
    public IEnumerable<VfsNode> Enumerate(string virtualPath, FtpUser? user)
    {
        if (string.IsNullOrWhiteSpace(virtualPath))
            return Enumerable.Empty<VfsNode>();

        virtualPath = NormalizeVirtualPath(virtualPath);

        foreach (var provider in _providers)
        {
            if (!provider.CanHandle(virtualPath))
                continue;

            var result = provider.Resolve(virtualPath, user);
            if (!result.Success)
                continue;

            try
            {
                return provider.Enumerate(virtualPath, user) ?? Enumerable.Empty<VfsNode>();
            }
            catch
            {
                // Enumeration is best-effort; callers handle empty results.
                return Enumerable.Empty<VfsNode>();
            }
        }

        return Enumerable.Empty<VfsNode>();
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------
    private static string NormalizeVirtualPath(string path)
    {
        path = path.Replace('\\', '/').Trim();

        if (!path.StartsWith('/'))
            path = "/" + path;

        // no trailing slash except root
        if (path.Length > 1 && path.EndsWith('/'))
            path = path.TrimEnd('/');

        return path;
    }
}