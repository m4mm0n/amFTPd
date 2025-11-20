/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-20
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
    /// <summary>
    /// Represents a configuration section for FTP operations, including settings for upload and download multipliers,
    /// default credits, and path information.
    /// </summary>
    /// <param name="SectionName">The name of the FTP section. This value cannot be null or empty.</param>
    /// <param name="RelativePath">The relative path associated with the FTP section. This value cannot be null or empty.</param>
    /// <param name="UploadMultiplier">The multiplier applied to upload operations, used to adjust upload bandwidth or quotas.</param>
    /// <param name="DownloadMultiplier">The multiplier applied to download operations, used to adjust download bandwidth or quotas.</param>
    /// <param name="DefaultCreditsKb">The default amount of credits, in kilobytes, allocated for operations in this section.</param>
    public sealed record FtpSection(
        string SectionName,
        string RelativePath,
        long UploadMultiplier,
        long DownloadMultiplier,
        long DefaultCreditsKb
    );
}
