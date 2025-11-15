namespace amFTPd.Config.Ftpd
{
    public sealed record FtpSection(
        string Name,
        string VirtualRoot,      // e.g. "/0day", "/mp3"
        bool FreeLeech,          // if true: downloads do not cost credits
        int RatioUploadUnit,     // e.g. 1 in 1:3
        int RatioDownloadUnit    // e.g. 3 in 1:3
    );
}
