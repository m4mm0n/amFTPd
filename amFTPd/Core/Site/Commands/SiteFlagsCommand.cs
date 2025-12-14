/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteFlagsCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 08:46:09
 *  Last Modified:  2025-12-14 21:29:58
 *  CRC32:          0xB37FC15F
 *  
 *  Description:
 *      TODO: Describe this file.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


namespace amFTPd.Core.Site.Commands
{
    public sealed class SiteFlagsCommand : SiteCommandBase
    {
        public override string Name => "FLAGS";
        public override bool RequiresAdmin => false;
        public override bool RequiresSiteop => true;
        public override string HelpText => "FLAGS <user> [flags] - show or set user flag string";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            var acc = context.Session.Account;
            if (acc is not { IsAdmin: true })
            {
                await context.Session.WriteAsync(
                    "550 SITE FLAGS requires admin privileges.\r\n",
                    cancellationToken);
                return;
            }

            var parts = argument?
                .Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? [];

            if (parts.Length == 0)
            {
                await context.Session.WriteAsync(
                    "501 Syntax: SITE FLAGS <user> [flags]\r\n",
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
                var flags = string.IsNullOrWhiteSpace(user.FlagsRaw) ? "-" : user.FlagsRaw;
                await context.Session.WriteAsync(
                    $"200 User {user.UserName} flags: {flags}\r\n",
                    cancellationToken);
                return;
            }

            // Set
            var newFlags = parts[1];
            var updated = user with { FlagsRaw = newFlags };

            if (!context.Users.TryUpdateUser(updated, out var error))
            {
                await context.Session.WriteAsync(
                    $"550 Failed to update user: {error ?? "unknown error"}\r\n",
                    cancellationToken);
                return;
            }

            await context.Session.WriteAsync(
                $"200 Flags for {updated.UserName} set to '{updated.FlagsRaw}'.\r\n",
                cancellationToken);
        }
    }
}
