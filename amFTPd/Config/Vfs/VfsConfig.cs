/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-22
 *  Last Modified:  2025-11-23
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

using System;

namespace amFTPd.Config.Vfs;

/// <summary>
/// Configuration for the virtual file system.
/// </summary>
public sealed record VfsConfig
{
    /// <summary>
    /// Old style: all mounts in one list.
    /// </summary>
    public IReadOnlyList<VfsMount> Mounts { get; init; } =
        Array.Empty<VfsMount>();

    /// <summary>
    /// User-specific mounts. These are considered before global mounts.
    /// </summary>
    public IReadOnlyList<VfsUserMount> UserMounts { get; init; } =
        Array.Empty<VfsUserMount>();

    /// <summary>
    /// Helper view of global mounts (same as <see cref="Mounts"/>).
    /// </summary>
    public IReadOnlyList<VfsMount> GlobalMounts => Mounts;

    public VfsMount? ResolveMount(
        string? username,
        string virtualPath,
        out string relativePath)
    {
        if (string.IsNullOrWhiteSpace(virtualPath))
            virtualPath = "/";

        if (!virtualPath.StartsWith("/", StringComparison.Ordinal))
            virtualPath = "/" + virtualPath;

        var userNameNorm = username?.Trim() ?? string.Empty;

        // 1. user-specific mounts
        var userMount = UserMounts
            .Where(um => string.Equals(um.Username, userNameNorm, StringComparison.OrdinalIgnoreCase))
            .Select(um => um.Mount)
            .OrderByDescending(m => m.VirtualRoot.Length)
            .FirstOrDefault(m => IsPrefix(m.VirtualRoot, virtualPath));

        if (userMount is not null)
        {
            relativePath = GetRelativePath(userMount.VirtualRoot, virtualPath);
            return userMount;
        }

        // 2. global mounts
        var globalMount = Mounts
            .OrderByDescending(m => m.VirtualRoot.Length)
            .FirstOrDefault(m => IsPrefix(m.VirtualRoot, virtualPath));

        if (globalMount is not null)
        {
            relativePath = GetRelativePath(globalMount.VirtualRoot, virtualPath);
            return globalMount;
        }

        relativePath = "/";
        return null;
    }

    private static bool IsPrefix(string root, string path)
    {
        if (!root.StartsWith("/"))
            root = "/" + root;

        if (!root.EndsWith("/"))
            root += "/";

        if (!path.StartsWith("/"))
            path = "/" + path;

        return path.Equals(root.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRelativePath(string root, string path)
    {
        if (!root.StartsWith("/"))
            root = "/" + root;

        if (!root.EndsWith("/"))
            root += "/";

        if (!path.StartsWith("/"))
            path = "/" + path;

        if (path.Equals(root.TrimEnd('/', (char)StringComparison.OrdinalIgnoreCase)))
            return "/";

        var rel = path[root.Length..];
        return string.IsNullOrEmpty(rel) ? "/" : "/" + rel.TrimStart('/');
    }
}