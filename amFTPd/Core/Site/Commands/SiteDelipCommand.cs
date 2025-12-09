namespace amFTPd.Core.Site.Commands;

public sealed class SiteDelipCommand : SiteCommandBase
{
    public override string Name => "DELIP";
    public override bool RequiresAdmin => true;
    public override string HelpText => "DELIP <user> - clear allowed IP mask for user";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync("501 Syntax: SITE DELIP <user>\r\n", cancellationToken);
            return;
        }

        var userName = argument.Trim();
        var user = context.Session.Users.FindUser(userName);
        if (user is null)
        {
            await context.Session.WriteAsync("550 User not found.\r\n", cancellationToken);
            return;
        }

        var updated = user with { AllowedIpMask = string.Empty };

        if (context.Session.Users.TryUpdateUser(updated, out var error))
        {
            await context.Session.WriteAsync($"200 IP mask cleared for user '{userName}'.\r\n", cancellationToken);
        }
        else
        {
            await context.Session.WriteAsync($"550 Failed to update user: {error}\r\n", cancellationToken);
        }
    }
}