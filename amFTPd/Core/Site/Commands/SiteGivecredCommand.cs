using System.Globalization;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteGivecredCommand : SiteCommandBase
{
    public override string Name => "GIVECRED";
    public override bool RequiresAdmin => true;
    public override string HelpText => "GIVECRED <user> <kb> - give credits (KiB) to a user";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var acc = context.Session.Account;
        if (acc is not { IsAdmin: true })
        {
            await context.Session.WriteAsync(
                "550 SITE GIVECRED requires admin privileges.\r\n",
                cancellationToken);
            return;
        }

        var parts = argument?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    ?? Array.Empty<string>();

        if (parts.Length != 2)
        {
            await context.Session.WriteAsync(
                "501 Syntax: SITE GIVECRED <user> <kb>\r\n",
                cancellationToken);
            return;
        }

        var userName = parts[0];
        if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var deltaKb))
        {
            await context.Session.WriteAsync(
                "501 Invalid credit amount.\r\n",
                cancellationToken);
            return;
        }

        var user = context.Users.FindUser(userName);
        if (user is null)
        {
            await context.Session.WriteAsync(
                "550 No such user.\r\n",
                cancellationToken);
            return;
        }

        var updated = user with { CreditsKb = user.CreditsKb + deltaKb };
        if (!context.Users.TryUpdateUser(updated, out var error))
        {
            await context.Session.WriteAsync(
                $"550 Failed to update user: {error ?? "unknown error"}\r\n",
                cancellationToken);
            return;
        }

        await context.Session.WriteAsync(
            $"200 {deltaKb} KiB credited to {updated.UserName}. New balance: {updated.CreditsKb} KiB\r\n",
            cancellationToken);
    }
}