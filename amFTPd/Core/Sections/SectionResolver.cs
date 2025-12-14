/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SectionResolver.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-28 20:20:04
 *  Last Modified:  2025-12-13 16:16:16
 *  CRC32:          0x359B3E85
 *  
 *  Description:
 *      Resolves <see cref="FtpSection"/> instances based on a given virtual path.
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
using static amFTPd.Db.SectionMappings;

namespace amFTPd.Core.Sections
{
    /// <summary>
    /// Resolves <see cref="FtpSection"/> instances based on a given virtual path.
    /// </summary>
    /// <remarks>The <see cref="SectionResolver"/> is initialized with a collection of <see
    /// cref="FtpSection"/> objects, which are normalized and ordered to ensure correct prefix-based resolution. This
    /// class is typically used to map incoming virtual paths to their corresponding FTP configuration sections,
    /// supporting scenarios where multiple sections may have overlapping or nested virtual roots.</remarks>
    public sealed class SectionResolver
    {
        private readonly IReadOnlyList<FtpSection> _sections;

        /// <summary>
        /// Initializes a new instance of the <see cref="SectionResolver"/> class using the specified collection of FTP
        /// sections.
        /// </summary>
        /// <remarks>The provided sections are normalized and ordered by descending virtual root length to
        /// ensure correct prefix matching behavior.</remarks>
        /// <param name="sections">The collection of <see cref="FtpSection"/> objects to be managed by the resolver. Each section defines a
        /// virtual root and associated configuration.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="sections"/> is <see langword="null"/>.</exception>
        public SectionResolver(IEnumerable<FtpSection> sections)
        {
            if (sections == null)
                throw new ArgumentNullException(nameof(sections));

            // Normalize and sort longest-first for proper prefix matching.
            _sections = sections
                .Select(s => s.Normalize())
                .OrderByDescending(s => s.VirtualRoot.Length)
                .ToList();
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="virtualPath"></param>
        /// <returns></returns>
        public FtpSection? Resolve(string virtualPath)
        {
            if (string.IsNullOrWhiteSpace(virtualPath))
                return null;

            virtualPath = virtualPath.Replace('\\', '/');
            if (!virtualPath.StartsWith('/'))
                virtualPath = "/" + virtualPath;

            return _sections.FirstOrDefault(sec => virtualPath.StartsWith(sec.VirtualRoot, StringComparison.OrdinalIgnoreCase));
        }
    }
}
