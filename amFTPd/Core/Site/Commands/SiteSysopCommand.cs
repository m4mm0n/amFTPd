namespace amFTPd.Core.Site.Commands;

public sealed class SiteSysopCommand : SiteCommandBase
{
    public override string Name => "SYSOP";
    public override bool RequiresAdmin => true;
    public override string HelpText => "SYSOP <user> <on|off>";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Syntax: SITE SYSOP <user> <on|off>\r\n", cancellationToken);
            return;
        }

        var parts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            await s.WriteAsync("501 Syntax: SITE SYSOP <user> <on|off>\r\n", cancellationToken);
            return;
        }

        var userName = parts[0];
        var mode = parts[1].ToLowerInvariant();
        var enable = mode is "on" or "1" or "true" or "yes";

        var user = context.Users.FindUser(userName);
        if (user is null)
        {
            await s.WriteAsync("550 User not found.\r\n", cancellationToken);
            return;
        }

        user = user with { IsAdmin = enable };

        if (!context.Users.TryUpdateUser(user, out var error))
        {
            await s.WriteAsync($"550 Failed to update user: {error ?? "unknown error"}\r\n", cancellationToken);
            return;
        }

        await s.WriteAsync("200 Admin flag updated.\r\n", cancellationToken);
    }
}