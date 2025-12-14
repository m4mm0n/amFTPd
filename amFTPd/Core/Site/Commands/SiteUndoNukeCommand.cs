/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteUndoNukeCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 12:48:38
 *  Last Modified:  2025-12-14 21:42:14
 *  CRC32:          0x740EB926
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
using amFTPd.Core.Events;

namespace amFTPd.Core.Site.Commands
{
    public sealed class SiteUndoNukeCommand : SiteCommandBase
    {
        public override string Name => "UNNUKE";
        public override bool RequiresAdmin => false;
        public override bool RequiresSiteop => true;
        public override string HelpText => "UNNUKE <path> [reason] - undo a previous NUKE and restore release name.";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            var s = context.Session;

            if (string.IsNullOrWhiteSpace(argument))
            {
                await s.WriteAsync("501 Usage: SITE UNNUKE <nuked-path> [reason]\r\n", cancellationToken);
                return;
            }

            var parts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var pathArg = parts[0];
            var reason = parts.Length > 1 ? parts[1] : "unnuke";

            var virtNuked = FtpPath.Normalize(s.Cwd, pathArg);

            string? physNuked;
            try
            {
                physNuked = context.Router.FileSystem.MapToPhysical(virtNuked);
            }
            catch
            {
                await s.WriteAsync("550 Permission denied.\r\n", cancellationToken);
                return;
            }

            var isDir = Directory.Exists(physNuked);
            var isFile = File.Exists(physNuked);

            if (!isDir && !isFile)
            {
                await s.WriteAsync("550 Path not found.\r\n", cancellationToken);
                return;
            }

            // Derive original (unnuked) name by stripping .NUKED* suffix.
            var nukedName = Path.GetFileName(virtNuked.TrimEnd('/', '\\'));
            var origName = nukedName;

            var idx = nukedName.IndexOf(".NUKED", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
                origName = nukedName[..idx];

            if (string.Equals(origName, nukedName, StringComparison.Ordinal))
            {
                await s.WriteAsync("550 Path does not look nuked.\r\n", cancellationToken);
                return;
            }

            var parentVirt = Path.GetDirectoryName(virtNuked.Replace('\\', '/')) ?? "/";
            parentVirt = parentVirt.Replace('\\', '/');
            if (!parentVirt.StartsWith("/"))
                parentVirt = "/" + parentVirt.Trim('/');

            var virtOrig = parentVirt.TrimEnd('/') + "/" + origName;

            string? parentPhys;
            try
            {
                parentPhys = context.Router.FileSystem.MapToPhysical(parentVirt);
            }
            catch
            {
                await s.WriteAsync("550 Permission denied.\r\n", cancellationToken);
                return;
            }

            if (parentPhys != null)
            {
                var physOrig = Path.Combine(parentPhys, origName);

                if (Directory.Exists(physOrig) || File.Exists(physOrig))
                {
                    await s.WriteAsync("550 UNNUKE target already exists.\r\n", cancellationToken);
                    return;
                }

                try
                {
                    if (isDir)
                    {
                        if (physNuked != null) Directory.Move(physNuked, physOrig);
                    }
                    else if (physNuked != null) File.Move(physNuked, physOrig);
                }
                catch
                {
                    await s.WriteAsync("550 Failed to rename nuked path.\r\n", cancellationToken);
                    return;
                }
            }

            var section = context.Router.GetSectionForVirtual(virtOrig);
            var user = s.Account?.UserName ?? "unknown";

            if (context.Runtime.Zipscript is not null)
            {
                // This uses the original (unnuked) path as release key.
                context.Runtime.Zipscript.MarkReleaseUnnuked(virtOrig, user);
            }

            // EventBus (optional): publish an UNNUKE event
            context.Runtime.EventBus?.Publish(new FtpEvent
            {
                Type = FtpEventType.Unnuke,
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = context.Session.SessionId,
                User = user,
                Group = s.Account?.GroupName,
                Section = section.Name,
                VirtualPath = virtOrig,
                ReleaseName = Path.GetFileName(virtOrig),
                Reason = reason
            });

            await s.WriteAsync($"250 UNNUKE completed for {virtOrig}\r\n", cancellationToken);
        }

    }
}
