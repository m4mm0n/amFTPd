/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-24
 *  Last Modified:  2025-11-28
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

namespace amFTPd.Db
{
    public static class SectionMappings
    {
        public static Config.Ftpd.FtpSection Normalize(this Config.Ftpd.FtpSection section)
        {
            if (section == null)
                throw new ArgumentNullException(nameof(section));

            var root = section.VirtualRoot?.Replace('\\', '/').Trim() ?? "/";
            if (!root.StartsWith('/'))
                root = "/" + root;

            return section with
            {
                VirtualRoot = root
            };
        }
    }
}
