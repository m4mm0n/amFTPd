namespace amFTPd.Config.Ftpd;

public sealed record FtpUserConfig(
    List<FtpUserConfigUser> Users
)
{
    public static FtpUserConfig Empty => new(new List<FtpUserConfigUser>());
}