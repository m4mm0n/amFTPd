using amFTPd.Core.Dupe;
using amFTPd.Core.Events;

namespace amFTPd.Core.Site.Commands
{
    public sealed class SitePreCommand : SiteCommandBase
    {
        public override string Name => "PRE";

        // Often restricted to admins / prebot – tweak if you want
        public override bool RequiresAdmin => true;

        public override string HelpText => "PRE <section> <release>  - register a pre in DUPE DB";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            var dupeStore = context.Runtime.DupeStore;
            if (dupeStore is null)
            {
                await context.Session.WriteAsync("550 DUPE database not enabled.\r\n", cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(argument))
            {
                await context.Session.WriteAsync("501 Usage: SITE PRE <section> <release>\r\n", cancellationToken);
                return;
            }

            var parts = argument.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await context.Session.WriteAsync("501 Usage: SITE PRE <section> <release>\r\n", cancellationToken);
                return;
            }

            var sectionName = parts[0];
            var releaseName = parts[1];

            // Build a virtual path hint: "/SECTION/release"
            // You may later resolve this via your VFS if you want.
            var virtPath = $"/{sectionName}/{releaseName}".Replace('\\', '/');

            var now = DateTimeOffset.UtcNow;

            var existing = dupeStore.Find(sectionName, releaseName);
            DupeEntry entry;
            if (existing is null)
            {
                entry = new DupeEntry
                {
                    ReleaseName = releaseName,
                    SectionName = sectionName,
                    VirtualPath = virtPath,
                    TotalBytes = 0,
                    FirstSeen = now,
                    LastUpdated = now,
                    UploaderUser = context.Session.Account?.UserName,
                    UploaderGroup = context.Session.Account?.GroupName,
                    IsNuked = false,
                    NukeReason = null,
                    NukeMultiplier = 0
                };
            }
            else
            {
                entry = existing with
                {
                    VirtualPath = virtPath,
                    LastUpdated = now
                };
            }

            dupeStore.Upsert(entry);

            // EventBus: announce PRE
            context.Runtime.EventBus?.Publish(new FtpEvent
            {
                Type = FtpEventType.Pre,
                Timestamp = DateTimeOffset.UtcNow,
                User = context.Session.Account?.UserName,
                Group = context.Session.Account?.GroupName,
                Section = sectionName,
                VirtualPath = virtPath,
                ReleaseName = releaseName,
                Extra = null
            });

            await context.Session.WriteAsync(
                $"200 PRE registered: {sectionName} {releaseName}\r\n",
                cancellationToken);
        }
    }
}
