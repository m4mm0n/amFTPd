/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SectionResolver.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-28 20:20:04
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xC5372304
 *  
 *  Description:
 *      TODO: Describe this file.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using amFTPd.Config.Ftpd;
using static amFTPd.Db.SectionMappings;

namespace amFTPd.Core.Sections
{
    public sealed class SectionResolver
    {
        private readonly IReadOnlyList<FtpSection> _sections;

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
