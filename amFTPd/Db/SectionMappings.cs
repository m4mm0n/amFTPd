/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SectionMappings.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-24 05:47:12
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xA523D711
 *  
 *  Description:
 *      Normalizes stored section records into runtime section configuration.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





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
                VirtualRoot = root,
                RatioUploadUnit = section.RatioUploadUnit > 0 ? section.RatioUploadUnit : 1,
                RatioDownloadUnit = section.RatioDownloadUnit > 0 ? section.RatioDownloadUnit : 1
            };
        }
    }
}
