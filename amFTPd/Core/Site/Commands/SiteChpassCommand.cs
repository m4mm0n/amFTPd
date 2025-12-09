using amFTPd.Security;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteChpassCommand : SiteCommandBase
{
    public override string Name => "CHPASS";
    public override bool RequiresAdmin => true;
    public override string HelpText => "CHPASS <user> <newpassword>";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync("501 Syntax: SITE CHPASS <user> <newpassword>\r\n", cancellationToken);
            return;
        }

        var parts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await context.Session.WriteAsync("501 Syntax: SITE CHPASS <user> <newpassword>\r\n", cancellationToken);
            return;
        }

        var userName = parts[0];
        var newPassword = parts[1];

        var user = context.Session.Users.FindUser(userName);
        if (user is null)
        {
            await context.Session.WriteAsync("550 User not found.\r\n", cancellationToken);
            return;
        }

        var newHash = PasswordHasher.HashPassword(newPassword);
        var updated = user with { PasswordHash = newHash };

        if (context.Session.Users.TryUpdateUser(updated, out var error))
        {
            await context.Session.WriteAsync($"200 Password changed for user '{userName}'.\r\n", cancellationToken);
        }
        else
        {
            await context.Session.WriteAsync($"550 Failed to update user: {error}\r\n", cancellationToken);
        }
    }
}