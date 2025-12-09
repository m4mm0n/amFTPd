namespace amFTPd.Core.Site.Commands
{
    public sealed class SiteDeluserCommand : SiteCommandBase
    {
        public override string Name => "DELUSER";
        public override bool RequiresAdmin => true;
        public override string HelpText => "DELUSER <user>  - disables the account";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            var s = context.Session;

            if (string.IsNullOrWhiteSpace(argument))
            {
                await s.WriteAsync("501 Syntax: SITE DELUSER <user>\r\n", cancellationToken);
                return;
            }

            var userName = argument.Trim();
            var user = context.Users.FindUser(userName);
            if (user is null)
            {
                await s.WriteAsync("550 User not found.\r\n", cancellationToken);
                return;
            }

            if (user.Disabled)
            {
                await s.WriteAsync("200 User is already disabled.\r\n", cancellationToken);
                return;
            }

            user = user with { Disabled = true };

            if (!context.Users.TryUpdateUser(user, out var error))
            {
                await s.WriteAsync($"550 Failed to disable user: {error ?? "unknown error"}\r\n", cancellationToken);
                return;
            }

            await s.WriteAsync("200 User disabled.\r\n", cancellationToken);
        }
    }
}
