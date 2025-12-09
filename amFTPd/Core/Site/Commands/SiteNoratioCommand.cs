namespace amFTPd.Core.Site.Commands;

public sealed class SiteNoratioCommand : SiteCommandBase
{
    public override string Name => "NORATIO";
    public override bool RequiresAdmin => true;
    public override string HelpText => "NORATIO <user> [ON|OFF] - show or toggle no-ratio flag for user";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var acc = context.Session.Account;
        if (acc is not { IsAdmin: true })
        {
            await context.Session.WriteAsync(
                "550 SITE NORATIO requires admin privileges.\r\n",
                cancellationToken);
            return;
        }

        var parts = argument?
                        .Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    ?? Array.Empty<string>();

        if (parts.Length == 0)
        {
            await context.Session.WriteAsync(
                "501 Syntax: SITE NORATIO <user> [ON|OFF]\r\n",
                cancellationToken);
            return;
        }

        var userName = parts[0];
        var user = context.Users.FindUser(userName);

        if (user is null)
        {
            await context.Session.WriteAsync(
                "550 No such user.\r\n",
                cancellationToken);
            return;
        }

        // Just show
        if (parts.Length == 1)
        {
            var state = user.IsNoRatio ? "ON" : "OFF";
            await context.Session.WriteAsync(
                $"200 User {user.UserName} NORATIO is {state}\r\n",
                cancellationToken);
            return;
        }

        // Set
        var mode = parts[1].ToUpperInvariant();
        bool newValue;
        if (mode is "ON" or "1" or "TRUE" or "YES" or "Y")
            newValue = true;
        else if (mode is "OFF" or "0" or "FALSE" or "NO" or "N")
            newValue = false;
        else
        {
            await context.Session.WriteAsync(
                "501 Syntax: SITE NORATIO <user> ON|OFF\r\n",
                cancellationToken);
            return;
        }

        var updated = user with { IsNoRatio = newValue };
        if (!context.Users.TryUpdateUser(updated, out var error))
        {
            await context.Session.WriteAsync(
                $"550 Failed to update user: {error ?? "unknown error"}\r\n",
                cancellationToken);
            return;
        }

        var state2 = updated.IsNoRatio ? "ON" : "OFF";
        await context.Session.WriteAsync(
            $"200 NORATIO for {updated.UserName} set to {state2}\r\n",
            cancellationToken);
    }
}