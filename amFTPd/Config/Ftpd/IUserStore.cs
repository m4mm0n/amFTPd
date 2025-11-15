namespace amFTPd.Config.Ftpd;

public interface IUserStore
{
    bool TryAuthenticate(string user, string password, out FtpUser? account);
    void OnLogout(FtpUser user);

    FtpUser? FindUser(string userName);
    IEnumerable<FtpUser> GetAllUsers();

    bool TryAddUser(FtpUser user, out string? error);
    bool TryUpdateUser(FtpUser user, out string? error);
}