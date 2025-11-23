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

namespace amFTPd.Config.Ftpd
{
    /// <summary>
    /// Represents the configuration details for an FTP section, including its name, virtual root path, leeching policy,
    /// ratio requirements, and optional nuke multiplier.
    /// </summary>
    /// <remarks>This record is typically used to define access and credit policies for different content
    /// areas on an FTP server. Ratio values determine upload/download requirements, and the nuke multiplier affects how
    /// penalties are calculated for problematic releases.</remarks>
    /// <param name="Name">The unique name of the FTP section. Used to identify the section within the system.</param>
    /// <param name="VirtualRoot">The virtual root directory path for the section, such as "/0day" or "/mp3". Specifies where the section is
    /// located within the FTP hierarchy.</param>
    /// <param name="FreeLeech">Indicates whether downloads in this section are free leech. If <see langword="true"/>, downloads do not consume
    /// user credits.</param>
    /// <param name="RatioUploadUnit">The unit value representing the required amount of data to upload for ratio enforcement. For example, in a 1:3
    /// ratio, this would be 1.</param>
    /// <param name="RatioDownloadUnit">The unit value representing the allowed amount of data to download for ratio enforcement. For example, in a 1:3
    /// ratio, this would be 3.</param>
    /// <param name="NukeMultiplier">An optional multiplier applied to penalties or credit adjustments when a release in this section is nuked. If
    /// <see langword="null"/>, no nuke multiplier is applied.</param>
    public sealed record FtpSection(
        string Name,
        string VirtualRoot,      // e.g. "/0day", "/mp3"
        bool FreeLeech,          // if true: downloads do not cost credits
        int RatioUploadUnit,     // e.g. 1 in 1:3
        int RatioDownloadUnit,    // e.g. 3 in 1:3
        double? NukeMultiplier = null
    );
}
