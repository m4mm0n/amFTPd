using amFTPd.Config.Ftpd;
using amFTPd.Core.Import.Records;

namespace amFTPd.Core.Import.Mappers;

public sealed class UserImportMapper
{
    public void Apply(
        IEnumerable<ImportedUserRecord> records,
        IUserStore users)
    {
        foreach (var u in records)
        {
            if (users.FindUser(u.UserName) is not null)
                continue;

            var user = new FtpUser
            {
                UserName = u.UserName,
                Disabled = true,                 // SAFETY
                PrimaryGroup = u.PrimaryGroup,
                SecondaryGroups = u.SecondaryGroups,
                IsAdmin = u.IsAdmin,
                IsSiteop = u.IsSiteop,
                IsNoRatio = u.IsNoRatio,
                CreditsKb = u.CreditsKb,
                FlagsRaw = "imported"
            };

            users.TryAddUser(user, out _);
        }
    }
}