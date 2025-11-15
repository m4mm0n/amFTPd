namespace amFTPd.Config.Ftpd
{
    /// <summary>
    /// Represents a configuration section for an FTP server, including its name, virtual root path, and settings
    /// related to free leech and upload/download ratios.
    /// </summary>
    /// <param name="Name">The name of the FTP section. This is typically used to identify the section uniquely.</param>
    /// <param name="VirtualRoot">The virtual root path of the FTP section, such as <c>"/0day"</c> or <c>"/mp3"</c>.</param>
    /// <param name="FreeLeech">A value indicating whether downloads in this section are free leech. If <see langword="true"/>, downloads do not
    /// cost credits; otherwise, they do.</param>
    /// <param name="RatioUploadUnit">The upload unit used to calculate the upload-to-download ratio. For example, a value of 1 in a 1:3 ratio.</param>
    /// <param name="RatioDownloadUnit">The download unit used to calculate the upload-to-download ratio. For example, a value of 3 in a 1:3 ratio.</param>
    public sealed record FtpSection(
        string Name,
        string VirtualRoot,      // e.g. "/0day", "/mp3"
        bool FreeLeech,          // if true: downloads do not cost credits
        int RatioUploadUnit,     // e.g. 1 in 1:3
        int RatioDownloadUnit    // e.g. 3 in 1:3
    );
}
