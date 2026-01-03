using amFTPd.Core.Dupe;
using amFTPd.Core.Events;
using amFTPd.Core.Race;
using System.Text;
using amFTPd.Config.Ftpd;

namespace amFTPd.Core.Site;

/// <summary>
/// Provides static methods for applying and reverting nuke status to releases, updating related site state and event
/// logs accordingly.
/// </summary>
/// <remarks>The NukePropagation class is intended for use within site command processing to ensure that nuke and
/// unnuke actions are consistently reflected across dupe stores, event buses, scene registries, and log files. All
/// methods are static and require valid context and release information to function correctly.</remarks>
public static class NukePropagation
{
    /// <summary>
    /// Marks a release as nuked in the specified section, records the nuke event, and updates related site and race
    /// state.
    /// </summary>
    /// <remarks>This method updates dupe stores, fires site and race events, publishes a nuke event to the
    /// event bus, updates the scene registry, and appends an entry to the scene log. If <paramref name="race"/> is
    /// provided, a race completion event is also triggered. No action is taken if <paramref name="releaseVirt"/> is
    /// null or whitespace.</remarks>
    /// <param name="context">The command context containing runtime services, session information, and event routing. Cannot be null.</param>
    /// <param name="releaseVirt">The virtual path of the release to be nuked. Leading and trailing slashes or backslashes are trimmed. If null or
    /// whitespace, the operation is not performed.</param>
    /// <param name="section">The FTP section in which the release resides, or null if not applicable.</param>
    /// <param name="nuker">The name of the user or system performing the nuke action.</param>
    /// <param name="reason">The reason for nuking the release. This information is recorded in logs and events.</param>
    /// <param name="nukeMultiplier">The multiplier value associated with the nuke, typically used for scoring or penalty calculations.</param>
    /// <param name="race">The race snapshot associated with the release, or null if the release is not part of a race.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="context"/> is null.</exception>
    public static void ApplyNuke(
        SiteCommandContext context,
        string releaseVirt,
        FtpSection? section,
        string nuker,
        string reason,
        double nukeMultiplier,
        RaceSnapshot? race)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(releaseVirt)) return;

        var virt = releaseVirt.TrimEnd('/', '\\');
        var sectionName = section?.Name ?? string.Empty;
        var releaseName = Path.GetFileName(virt);

        UpdateDupeStore(
            context.Runtime.DupeStore,
            virt,
            sectionName,
            releaseName,
            isNuked: true,
            reason: reason,
            nukeMultiplier: nukeMultiplier);

        context.Router.FireSiteEvent("onNuke", virt, section, nuker);
        if (race is not null)
            context.Router.FireSiteEvent("onRaceComplete", virt, section, nuker);

        context.Runtime.EventBus?.Publish(new FtpEvent
        {
            Type = FtpEventType.Nuke,
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = context.Session.SessionId,
            User = nuker,
            Group = context.Session.Account?.GroupName,
            Section = string.IsNullOrWhiteSpace(sectionName) ? null : sectionName,
            VirtualPath = virt,
            ReleaseName = releaseName,
            Reason = reason,
            Extra = $"mult={nukeMultiplier}"
        });

        context.SceneRegistry.Nuke(sectionName, virt, reason);

        AppendSceneLog(
            action: "NUKE",
            virt: virt,
            user: nuker,
            reason: reason,
            multiplier: nukeMultiplier,
            race: race);
    }
    /// <summary>
    /// Removes the nuke status from a specified release and updates related site state, logs, and events.
    /// </summary>
    /// <remarks>This method updates the dupe store, fires site and event bus notifications, updates the scene
    /// registry, and appends an unnuke entry to the scene log. If releaseVirt is null or whitespace, the method returns
    /// without performing any action.</remarks>
    /// <param name="context">The site command context in which the unnuke operation is performed. Cannot be null.</param>
    /// <param name="releaseVirt">The virtual path of the release to unnuke. Leading and trailing slashes or backslashes are ignored. If null or
    /// whitespace, the method does nothing.</param>
    /// <param name="section">The FTP section associated with the release, or null if not applicable.</param>
    /// <param name="unnuker">The name of the user performing the unnuke operation.</param>
    /// <param name="reason">The reason for the unnuke, which may be included in event logs and notifications.</param>
    /// <exception cref="ArgumentNullException">Thrown if the context parameter is null.</exception>
    public static void ApplyUnnuke(
        SiteCommandContext context,
        string releaseVirt,
        FtpSection? section,
        string unnuker,
        string reason)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(releaseVirt)) return;

        var virt = releaseVirt.TrimEnd('/', '\\');
        var sectionName = section?.Name ?? string.Empty;
        var releaseName = Path.GetFileName(virt);

        UpdateDupeStore(
            context.Runtime.DupeStore,
            virt,
            sectionName,
            releaseName,
            isNuked: false,
            reason: null,
            nukeMultiplier: 0);

        context.Router.FireSiteEvent("onUnnuke", virt, section, unnuker);

        context.Runtime.EventBus?.Publish(new FtpEvent
        {
            Type = FtpEventType.Unnuke,
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = context.Session.SessionId,
            User = unnuker,
            Group = context.Session.Account?.GroupName,
            Section = string.IsNullOrWhiteSpace(sectionName) ? null : sectionName,
            VirtualPath = virt,
            ReleaseName = releaseName,
            Reason = reason
        });

        context.SceneRegistry.Unnuke(sectionName, virt);

        AppendSceneLog(
            action: "UNNUKE",
            virt: virt,
            user: unnuker,
            reason: reason,
            multiplier: 0,
            race: null);
    }

    private static void UpdateDupeStore(
        IDupeStore? dupeStore,
        string releaseVirt,
        string sectionName,
        string releaseName,
        bool isNuked,
        string? reason,
        double nukeMultiplier)
    {
        if (dupeStore is null)
            return;

        if (string.IsNullOrWhiteSpace(releaseName))
            return;

        var now = DateTimeOffset.UtcNow;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<DupeEntry>(64);

        if (!string.IsNullOrWhiteSpace(sectionName))
        {
            var exact = dupeStore.Find(sectionName, releaseName);
            if (exact is not null)
                candidates.Add(exact);

            candidates.AddRange(dupeStore.Search(releaseName, sectionName, limit: 50));
        }

        candidates.AddRange(dupeStore.Search(releaseName, sectionName: null, limit: 50));

        foreach (var entry in candidates)
        {
            if (!seen.Add(entry.Key))
                continue;

            var isSameVirt = !string.IsNullOrWhiteSpace(entry.VirtualPath) &&
                             entry.VirtualPath.TrimEnd('/', '\\')
                                 .Equals(releaseVirt, StringComparison.OrdinalIgnoreCase);

            var isSameName = entry.ReleaseName.Equals(releaseName, StringComparison.OrdinalIgnoreCase);

            if (!isSameVirt && !isSameName)
                continue;

            if (!string.IsNullOrWhiteSpace(sectionName) &&
                !entry.SectionName.Equals(sectionName, StringComparison.OrdinalIgnoreCase) &&
                !isSameVirt)
            {
                continue;
            }

            var updated = entry with
            {
                IsNuked = isNuked,
                NukeReason = isNuked ? reason : null,
                NukeMultiplier = isNuked ? (int)Math.Round(nukeMultiplier) : 0,
                LastUpdated = now
            };

            dupeStore.Upsert(updated);
        }
    }
    private static void AppendSceneLog(
        string action,
        string virt,
        string user,
        string reason,
        double multiplier,
        RaceSnapshot? race)
    {
        try
        {
            Directory.CreateDirectory("logs");

            var now = DateTimeOffset.UtcNow;
            var sb = new StringBuilder();

            sb.Append(now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
              .Append(" | ")
              .Append(action)
              .Append(" | ")
              .Append("path=").Append(virt)
              .Append(" | user=").Append(user);

            if (!string.IsNullOrWhiteSpace(reason))
                sb.Append(" | reason=").Append(reason);

            if (multiplier > 0)
                sb.Append(" | mult=").Append(multiplier);

            if (race is not null)
                sb.Append(" | totalBytes=").Append(race.TotalBytes).Append(" | files=").Append(race.FileCount);

            sb.AppendLine();
            File.AppendAllText("logs/nukes.log", sb.ToString());
        }
        catch
        {
        }
    }
}
