namespace amFTPd.Core.Site.Commands;

public sealed class SiteSetflagsCommand : SiteCommandBase
{
    public override string Name => "SETFLAGS";
    public override bool RequiresAdmin => true;
    public override string HelpText => "SETFLAGS <user> <flags>";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Syntax: SITE SETFLAGS <user> <flags>\r\n", cancellationToken);
            return;
        }

        var parts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await s.WriteAsync("501 Syntax: SITE SETFLAGS <user> <flags>\r\n", cancellationToken);
            return;
        }

        var userName = parts[0];
        var flags = parts[1];

        var user = context.Users.FindUser(userName);
        if (user is null)
        {
            await s.WriteAsync("550 User not found.\r\n", cancellationToken);
            return;
        }

        user = user with { FlagsRaw = flags };

        if (!context.Users.TryUpdateUser(user, out var error))
        {
            await s.WriteAsync($"550 Failed to update flags: {error ?? "unknown error"}\r\n", cancellationToken);
            return;
        }

        await s.WriteAsync("200 Flags updated.\r\n", cancellationToken);
    }
}