using amFTPd.Core.Events;
using amFTPd.Core.Race;
using amFTPd.Logging;
using System.Text;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteNukeCommand : SiteCommandBase
{
    public override string Name => "NUKE";
    public override bool RequiresAdmin => true;
    public override string HelpText => "NUKE";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {

        if (context.Session.Account is not { IsAdmin: true })
        {
            await context.Session.WriteAsync("550 SITE NUKE requires admin privileges.\r\n", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync(
                "501 Usage: SITE NUKE <path> <reason...>\r\n",
                cancellationToken);
            return;
        }

        var parts = argument.Split(' ', 2, StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await context.Session.WriteAsync(
                "501 Usage: SITE NUKE <path> <reason...>\r\n",
                cancellationToken);
            return;
        }

        var pathArg = parts[0];
        var reason = parts[1];

        var virt = FtpPath.Normalize(context.Session.Cwd, pathArg);
        string phys;
        try
        {
            phys = context.Router.FileSystem.MapToPhysical(virt);
        }
        catch
        {
            await context.Session.WriteAsync("550 Permission denied.\r\n", cancellationToken);
            return;
        }

        var isDir = Directory.Exists(phys);
        var isFile = File.Exists(phys);

        if (!isDir && !isFile)
        {
            await context.Session.WriteAsync("550 File or directory not found.\r\n", cancellationToken);
            return;
        }

        // Determine release path key for race/nuke credit logic
        string releaseVirt;
        if (isDir)
        {
            releaseVirt = virt;
        }
        else
        {
            var dirVirtRaw = Path.GetDirectoryName(virt);
            releaseVirt = string.IsNullOrEmpty(dirVirtRaw) || dirVirtRaw == "\\"
                ? "/"
                : dirVirtRaw.Replace('\\', '/');
        }

        // Try to resolve section for the release (used for logging / events)
        Config.Ftpd.FtpSection? section = null;
        try
        {
            section = context.Router.GetSectionForVirtual(releaseVirt);
        }
        catch
        {
            // best-effort; section can be null
        }

        var nuker = context.Session.Account?.UserName ?? "unknown";
        var nukeMultiplier = 3.0; // classic 3x nuke penalty

        if (section?.NukeMultiplier is { } sectionMult && sectionMult > 0)
        {
            nukeMultiplier = sectionMult;
        }
        else if (context.Router.Config.DefaultNukeMultiplier > 0)
        {
            nukeMultiplier = context.Router.Config.DefaultNukeMultiplier;
        }

        // We keep penalties list and race reference, but *no* RatioEngine calls anymore.
        var penalties = new List<(string User, long Bytes, long PenaltyKb, long NewCredits)>();
        RaceSnapshot? race = null;

        if (context.RaceEngine.TryGetRace(releaseVirt, out var r))
        {
            // For now we just remember the race for logging / events.
            // Credit-back logic via RatioEngine was removed because the API does not exist.
            race = r;
        }

        // Perform the actual physical NUKE (rename)
        try
        {
            var parent = Path.GetDirectoryName(phys) ?? phys;
            var name = Path.GetFileName(phys);
            var baseNukedName = name + ".NUKED";
            var target = Path.Combine(parent, baseNukedName);

            if (Directory.Exists(target) || File.Exists(target))
            {
                var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                target = Path.Combine(parent, $"{name}.NUKED-{stamp}");
            }

            if (isDir)
            {
                Directory.Move(phys, target);
            }
            else
            {
                File.Move(phys, target);
            }

            // Log to main log
            context.Log.Log(
                FtpLogLevel.Warn,
                $"SITE NUKE by {nuker}: {virt} => {target} (Reason: {reason}, Penalties: {penalties.Count})");

            // Append to nukes.log (scene-style nuke log stub)
            try
            {
                Directory.CreateDirectory("logs");
                var sb = new StringBuilder();
                var now = DateTimeOffset.UtcNow;

                sb.Append(now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
                    .Append(" | NUKE | ")
                    .Append($"path={virt} | nuker={nuker} | reason={reason} | mult={nukeMultiplier}");

                if (race is not null)
                {
                    sb.Append($" | totalBytes={race.TotalBytes} | files={race.FileCount}");
                }

                if (penalties.Count > 0)
                {
                    sb.Append(" | penalties=");
                    sb.Append(string.Join(";", penalties.Select(p =>
                        $"{p.User}:{p.Bytes}B:-{p.PenaltyKb}KB=>{p.NewCredits}KB")));
                }

                sb.AppendLine();

                File.AppendAllText("logs/nukes.log", sb.ToString());
            }
            catch
            {
                // logging failure shouldn't break NUKE
            }

            // DUPE DB integration: mark releases as nuked
            if (context.Runtime.DupeStore is { } dupeStore)
            {
                var trimmed = releaseVirt.TrimEnd('/', '\\');
                var releaseName = Path.GetFileName(trimmed);

                var matches = dupeStore.Search(releaseName);

                foreach (var entry in matches)
                {
                    var updated = entry with
                    {
                        IsNuked = true,
                        NukeReason = reason,
                        NukeMultiplier = (int)Math.Round(nukeMultiplier),
                        LastUpdated = DateTimeOffset.UtcNow
                    };

                    dupeStore.Upsert(updated);
                }
            }

            // AMScript notification hooks:
            context.Router.FireSiteEvent("onNuke", releaseVirt, section, nuker);

            if (race is not null)
                context.Router.FireSiteEvent("onRaceComplete", releaseVirt, section, nuker);

            // EventBus: announce NUKE
            var releaseNameForEvent = Path.GetFileName(releaseVirt.TrimEnd('/', '\\'));

            context.Runtime.EventBus?.Publish(new FtpEvent
            {
                Type = FtpEventType.Nuke,
                Timestamp = DateTimeOffset.UtcNow,
                User = nuker,
                Group = context.Session.Account?.GroupName,
                Section = section?.Name,
                VirtualPath = releaseVirt,
                ReleaseName = releaseNameForEvent,
                Reason = reason,
                Extra = $"mult={nukeMultiplier}"
            });

            await context.Session.WriteAsync(
                $"250 NUKE completed for {virt}. Reason: {reason}\r\n",
                cancellationToken);
        }
        catch (Exception ex)
        {
            context.Log.Log(
                FtpLogLevel.Error,
                $"SITE NUKE failed for {virt}: {ex.Message}");

            await context.Session.WriteAsync("550 NUKE failed.\r\n", cancellationToken);
        }
    }
}