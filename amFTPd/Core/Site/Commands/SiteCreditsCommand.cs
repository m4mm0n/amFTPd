using amFTPd.Config.Ftpd;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteCreditsCommand : SiteCommandBase
{
    public override string Name => "CREDITS";
    public override string HelpText => "CREDITS [user] - show credits for you or another user";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        FtpUser? target;

        if (string.IsNullOrWhiteSpace(argument))
        {
            // No argument -> current user
            target = context.Session.Account;
            if (target is null)
            {
                await context.Session.WriteAsync(
                    "530 Not logged in.\r\n",
                    cancellationToken);
                return;
            }
        }
        else
        {
            // Admin can query arbitrary users
            var acc = context.Session.Account;
            if (acc is null || !acc.IsAdmin)
            {
                await context.Session.WriteAsync(
                    "550 Only admins can query other users' credits.\r\n",
                    cancellationToken);
                return;
            }

            var userName = argument.Trim();
            target = context.Users.FindUser(userName);
            if (target is null)
            {
                await context.Session.WriteAsync(
                    "550 No such user.\r\n",
                    cancellationToken);
                return;
            }
        }

        var line =
            $"200-User {target.UserName} credits:\r\n" +
            $"200 CREDITSKB={target.CreditsKb}\r\n";

        await context.Session.WriteAsync(line, cancellationToken);
    }
}