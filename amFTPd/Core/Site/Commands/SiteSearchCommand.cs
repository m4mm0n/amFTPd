namespace amFTPd.Core.Site.Commands
{
    public sealed class SiteSearchCommand : SiteCommandBase
    {
        public override string Name => "SEARCH";

        public override bool RequiresAdmin => false;

        public override string HelpText => "SEARCH <pattern> [section]  - search releases in DUPE DB";

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
                await context.Session.WriteAsync("501 Usage: SITE SEARCH <pattern> [section]\r\n", cancellationToken);
                return;
            }

            var parts = argument.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var pattern = parts[0];
            string? sectionFilter = parts.Length > 1 ? parts[1] : null;

            var results = dupeStore.Search(pattern, sectionFilter, limit: 100);
            if (results.Count == 0)
            {
                await context.Session.WriteAsync("211 No matches found.\r\n", cancellationToken);
                return;
            }

            await context.Session.WriteAsync("211- Search matches:\r\n", cancellationToken);

            foreach (var d in results.OrderBy(x => x.SectionName).ThenBy(x => x.ReleaseName))
            {
                var nukedMarker = d.IsNuked ? " [NUKED]" : "";
                var sizeMb = d.TotalBytes / (1024 * 1024.0);

                var line =
                    $"211- {d.SectionName,-8} {d.ReleaseName,-40} {sizeMb,8:0.0} MB  {d.VirtualPath}{nukedMarker}\r\n";

                await context.Session.WriteAsync(line, cancellationToken);
            }

            await context.Session.WriteAsync("211 End of SEARCH listing.\r\n", cancellationToken);
        }
    }
}
