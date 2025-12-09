/*
* ====================================================================================================
*  Project:        amFTPd - a managed FTP daemon
*  Author:         Geir Gustavsen, ZeroLinez Softworx
*  Created:        2025-11-25
*  Last Modified:  2025-12-01
*  
*  License:
*      MIT License
*      https://opensource.org/licenses/MIT
*
*  Notes:
*      Simple in-memory implementation of ISectionStore. This is used when the
*      binary DB backend is not active, or as a lightweight wrapper over the
*      configuration-based SectionManager.
* ====================================================================================================
*/

using System.Collections.Immutable;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteChgrpCommand : SiteCommandBase
{
    public override string Name => "CHGRP";
    public override bool RequiresAdmin => true;
    public override string HelpText => "CHGRP <user> <primary-group> [secondary1,secondary2,...]";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var s = context.Session;

        if (string.IsNullOrWhiteSpace(argument))
        {
            await s.WriteAsync("501 Syntax: SITE CHGRP <user> <primary-group> [secondary1,secondary2,...]\r\n", cancellationToken);
            return;
        }

        var parts = argument.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await s.WriteAsync("501 Syntax: SITE CHGRP <user> <primary-group> [secondary1,secondary2,...]\r\n", cancellationToken);
            return;
        }

        var userName = parts[0];
        var primary = parts[1];
        var secondaryRaw = parts.Length == 3 ? parts[2] : string.Empty;

        var user = context.Users.FindUser(userName);
        if (user is null)
        {
            await s.WriteAsync("550 User not found.\r\n", cancellationToken);
            return;
        }

        IReadOnlyList<string> secondaryGroups;
        if (string.IsNullOrWhiteSpace(secondaryRaw))
        {
            secondaryGroups = Array.Empty<string>();
        }
        else
        {
            secondaryGroups = secondaryRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        user = user with
        {
            PrimaryGroup = primary,
            SecondaryGroups = secondaryGroups
        };

        if (!context.Users.TryUpdateUser(user, out var error))
        {
            await s.WriteAsync($"550 Failed to update user: {error ?? "unknown error"}\r\n", cancellationToken);
            return;
        }

        await s.WriteAsync("200 Group membership updated.\r\n", cancellationToken);
    }
}