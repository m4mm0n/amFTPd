/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-22
 *  Last Modified:  2025-11-22
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

namespace amFTPd.Config.Vfs;

/// <summary>
/// Represents the configuration settings for a virtual file system (VFS).
/// </summary>
/// <remarks>This configuration defines the behavior and constraints of the virtual file system, 
/// including case sensitivity, mount points, user-specific mounts, virtual files,  file size limits, and access
/// restrictions. It also includes caching settings  for metadata to optimize performance.</remarks>
public sealed record VfsConfig
{
    public bool CaseSensitive { get; init; } = false;

    public List<VfsMount> Mounts { get; init; } = new();
    public List<VfsUserMount> UserMounts { get; init; } = new();
    public List<VfsVirtualFile> VirtualFiles { get; init; } = new();

    /// <summary>Maximum allowed file size in bytes (0 = unlimited).</summary>
    public long MaxFileSizeBytes { get; init; } = 0;

    /// <summary>Block extensions (e.g. ".exe", ".bat").</summary>
    public List<string> DenyExtensions { get; init; } = new();

    /// <summary>Hide/deny hidden files.</summary>
    public bool DenyHiddenFiles { get; init; } = false;

    /// <summary>Cache TTL for metadata (seconds).</summary>
    public int CacheTtlSeconds { get; init; } = 30;
}