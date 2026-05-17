/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteGroupaddCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 08:47:39
 *  Last Modified:  2025-12-14 21:30:31
 *  CRC32:          0xC503C812
 *  
 *  Description:
 *      SITE GROUPADD creates a runtime group for scene administration.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


using amFTPd.Config.Ftpd;
using amFTPd.Db;

namespace amFTPd.Core.Site.Commands
{
    /// <summary>
    /// Implements <c>SITE GROUPADD</c>, creating a group in the live runtime configuration and, when available, the
    /// configured group store.
    /// </summary>
    public sealed class SiteGroupaddCommand : SiteCommandBase
    {
        public override string Name => "GROUPADD";
        public override bool RequiresAdmin => false;
        public override bool RequiresSiteop => true;
        public override string HelpText => "GROUPADD <group> [description] - create a scene group";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            if (context.Session.Account is not { IsAdmin: true })
            {
                await context.Session.WriteAsync("550 SITE GROUPADD requires admin privileges.\r\n", cancellationToken);
                return;
            }

            var parts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                await context.Session.WriteAsync("501 Syntax: SITE GROUPADD <group> [description]\r\n", cancellationToken);
                return;
            }

            var groupName = parts[0].Trim();
            var description = parts.Length > 1 ? parts[1].Trim() : $"Scene group {groupName}";

            if (string.IsNullOrWhiteSpace(groupName) ||
                groupName.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
            {
                await context.Session.WriteAsync("501 Invalid group name.\r\n", cancellationToken);
                return;
            }

            if (context.Runtime.ContainsGroup(groupName))
            {
                await context.Session.WriteAsync("550 Group already exists.\r\n", cancellationToken);
                return;
            }

            var cfg = new GroupConfig
            {
                Description = description,
                RatioMultiply = 1.0,
                UploadBonus = 1.0
            };

            if (context.Runtime.GroupStore is not null)
            {
                var group = new FtpGroup(groupName, description, [], []);
                if (!context.Runtime.GroupStore.TryAddGroup(group, out var error))
                {
                    await context.Session.WriteAsync($"550 Failed to create group: {error ?? "unknown error"}\r\n", cancellationToken);
                    return;
                }
            }

            context.Runtime.SetGroup(groupName, cfg);
            context.Runtime.AuditLog?.Log(
                actor: context.Session.Account.UserName,
                action: "GROUPADD",
                target: groupName,
                detail: description,
                ip: context.Session.RemoteEndPoint?.Address.ToString());

            await context.Session.WriteAsync($"200 Group created: {groupName}\r\n", cancellationToken);
        }
    }
}
