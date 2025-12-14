/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteSetflagsCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-25 03:06:34
 *  Last Modified:  2025-12-14 21:37:34
 *  CRC32:          0xD414C459
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


namespace amFTPd.Core.Site.Commands;

public sealed class SiteSetflagsCommand : SiteCommandBase
{
    public override string Name => "SETFLAGS";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
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