/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           VfsMount.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-23 20:41:52
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xB4EC9965
 *  
 *  Description:
 *      Describes a mapping from a virtual path to a physical directory on disk, optionally with attached virtual files.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Config.Vfs
{
    /// <summary>
    /// Describes a mapping from a virtual path to a physical directory on disk,
    /// optionally with attached virtual files.
    /// </summary>
    public sealed record VfsMount
    {
        public string Name { get; init; } = string.Empty;

        /// <summary>Virtual root (e.g. "/pub").</summary>
        public string VirtualRoot { get; init; } = "/";

        /// <summary>Actual physical path on disk.</summary>
        public string PhysicalPath { get; init; } = "/";

        /// <summary>Compatibility alias used by some code.</summary>
        public string VirtualPath
        {
            get => VirtualRoot;
            init => VirtualRoot = value;
        }

        /// <summary>Physical path on disk.</summary>
        public string PhysicalRoot { get; init; } = string.Empty;

        public bool IsReadOnly { get; init; }

        public IReadOnlyList<VfsVirtualFile> VirtualFiles { get; init; } =
            Array.Empty<VfsVirtualFile>();

        public VfsMount(string virtualPath, string physicalPath, bool isReadOnly = false)
        {
            VirtualPath = virtualPath;
            PhysicalPath = physicalPath;
            IsReadOnly = isReadOnly;
        }
    }
}
