namespace amFTPd.Config.Ftpd;

public sealed record FtpSectionConfig(
    List<FtpSection> Sections
)
{
    public static FtpSectionConfig Empty => new(new List<FtpSection>());
}