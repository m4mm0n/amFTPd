using amFTPd.Core.Events;

namespace amFTPd.Core.Site.Commands
{
    public sealed class SiteSfvCommand : SiteCommandBase
    {
        public override string Name => "SFV";

        public override bool RequiresAdmin => false;

        public override string HelpText => "SFV <path>  - show zipscript status for release";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            var engine = context.Runtime.Zipscript;
            if (engine is null)
            {
                await context.Session.WriteAsync("550 Zipscript is not enabled.\r\n", cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(argument))
            {
                await context.Session.WriteAsync("501 Usage: SITE SFV <path>\r\n", cancellationToken);
                return;
            }

            var path = argument.Trim();

            var status = engine.GetStatus(path);
            if (status is null)
            {
                await context.Session.WriteAsync("211 No zipscript status for this path.\r\n", cancellationToken);
                return;
            }

            // Header
            await context.Session.WriteAsync(
                $"211- Zipscript status for {status.ReleasePath}\r\n" +
                $"211- Section:   {status.SectionName}\r\n" +
                $"211- Has SFV:   {(status.HasSfv ? "YES" : "NO")}\r\n" +
                $"211- Complete:  {(status.IsComplete ? "YES" : "NO")}\r\n" +
                "211- Files:\r\n",
                cancellationToken);

            // Per-file listing
            foreach (var f in status.Files)
            {
                var state = f.State.ToString().ToUpperInvariant();
                var expected = f.ExpectedCrc is { } ec
                    ? ec.ToString("X8")
                    : "--------";
                var actual = f.ActualCrc is { } ac
                    ? ac.ToString("X8")
                    : "--------";

                await context.Session.WriteAsync(
                    $"211-  {state,-7} {f.FileName,-32} exp={expected} got={actual}\r\n",
                    cancellationToken);
            }

            await context.Session.WriteAsync("211 End of SFV status.\r\n", cancellationToken);

            // EventBus: announce zipscript status (for IRC / other listeners)
            var releaseName = Path.GetFileName(status.ReleasePath.TrimEnd('/', '\\'));
            string reason;

            if (!status.HasSfv)
                reason = "NO_SFV";
            else if (status.IsComplete)
                reason = "COMPLETE";
            else
                reason = "INCOMPLETE";

            context.Runtime.EventBus?.Publish(new FtpEvent
            {
                Type = FtpEventType.ZipscriptStatus,
                Timestamp = DateTimeOffset.UtcNow,
                User = context.Session.Account?.UserName,
                Group = context.Session.Account?.GroupName,
                Section = status.SectionName,
                VirtualPath = status.ReleasePath,
                ReleaseName = string.IsNullOrEmpty(releaseName) ? status.ReleasePath : releaseName,
                Reason = reason
            });
        }
    }
}
