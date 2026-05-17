using System.Text;
using amFTPd.Config.Daemon;
using amFTPd.Config.Ftpd;
using amFTPd.Core.Dupe;
using amFTPd.Core.Events;
using amFTPd.Core.Race;
using amFTPd.Core.Scene;
using amFTPd.Core.Stats;
using amFTPd.Logging;

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

        // Deduct upload credits from everyone who contributed to this release.
        if (race is not null && race.UserBytes.Count > 0)
        {
            var penaltyMap = DeductNukeCredits(context.Users, race.UserBytes, nukeMultiplier);
            if (penaltyMap.Count > 0)
            {
                PersistNukePenalties(context.Runtime.DupeStore, sectionName, releaseName, penaltyMap);
                AppendCreditLog("NUKE-DEDUCT", virt, penaltyMap);
            }
        }

        PerfCounters.NukeExecuted();
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

        // Restore credits before we clear the penalty record from the dupe entry.
        var restored = RestoreNukeCredits(context.Users, context.Runtime.DupeStore, sectionName, releaseName);
        if (restored.Count > 0)
            AppendCreditLog("UNNUKE-RESTORE", virt, restored);

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

        PerfCounters.UnnukeExecuted();
    }

    // ==========================================================
    // SYSTEM (AUTO) NUKE — no session/router required
    // ==========================================================

    /// <summary>
    /// Apply a nuke triggered by an automated rule (e.g. auto-nuke engine).
    /// Does not require an active FTP session or router; all state changes are
    /// driven through <paramref name="runtime"/> directly.
    /// </summary>
    public static void ApplySystemNuke(
        AmFtpdRuntimeConfig runtime,
        IFtpLogger log,
        string releaseVirt,
        string sectionName,
        string reason,
        double nukeMultiplier,
        SceneStateRegistry? sceneRegistry = null)
    {
        if (runtime is null) throw new ArgumentNullException(nameof(runtime));
        if (string.IsNullOrWhiteSpace(releaseVirt)) return;

        var virt = releaseVirt.TrimEnd('/', '\\');
        var releaseName = Path.GetFileName(virt);
        const string nuker = "AUTO-NUKE";

        // Update dupe store
        UpdateDupeStore(
            runtime.DupeStore,
            virt,
            sectionName,
            releaseName,
            isNuked: true,
            reason: reason,
            nukeMultiplier: nukeMultiplier);

        // Publish event
        runtime.EventBus?.Publish(new FtpEvent
        {
            Type = FtpEventType.AutoNuke,
            Timestamp = DateTimeOffset.UtcNow,
            User = nuker,
            Section = string.IsNullOrWhiteSpace(sectionName) ? null : sectionName,
            VirtualPath = virt,
            ReleaseName = releaseName,
            Reason = reason,
            Extra = $"mult={nukeMultiplier}"
        });

        // Update scene registry (if wired)
        sceneRegistry?.Nuke(sectionName, virt, reason);

        // Append scene log
        AppendSceneLog(
            action: "AUTO-NUKE",
            virt: virt,
            user: nuker,
            reason: reason,
            multiplier: nukeMultiplier,
            race: null);

        log.Log(FtpLogLevel.Info,
            $"[AUTO-NUKE] {releaseName} in {sectionName}: {reason} (×{nukeMultiplier})");

        PerfCounters.NukeExecuted();
    }

    // ==========================================================
    // CREDIT DEDUCTION / RESTORATION
    // ==========================================================

    /// <summary>
    /// Proportionally deduct credits from every uploader in the race.
    /// Penalty = ceil(bytesUploadedKb × nukeMultiplier), floored at zero credits.
    /// Returns a map of username → penaltyKb for the uploaders that were found and updated.
    /// </summary>
    private static Dictionary<string, long> DeductNukeCredits(
        IUserStore users,
        IReadOnlyDictionary<string, long> userBytes,
        double nukeMultiplier)
    {
        var penalties = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var (userName, bytes) in userBytes)
        {
            var user = users.FindUser(userName);
            if (user is null) continue;

            var bytesKb = bytes / 1024L;
            if (bytesKb <= 0) continue;

            var penaltyKb = (long)Math.Ceiling(bytesKb * nukeMultiplier);
            var newCredits = Math.Max(0L, user.CreditsKb - penaltyKb);

            if (users.TryUpdateUser(user with { CreditsKb = newCredits }, out _))
                penalties[userName] = penaltyKb;
        }

        return penalties;
    }

    /// <summary>
    /// Write the penalty map into the matching <see cref="DupeEntry"/> so it survives
    /// across restarts and can be replayed on UNNUKE.
    /// </summary>
    private static void PersistNukePenalties(
        IDupeStore? dupeStore,
        string sectionName,
        string releaseName,
        Dictionary<string, long> penalties)
    {
        if (dupeStore is null || penalties.Count == 0) return;

        var entry = FindMatchingDupeEntry(dupeStore, sectionName, releaseName);
        if (entry is null) return;

        var updated = entry with
        {
            NukePenalties = new Dictionary<string, long>(penalties, StringComparer.OrdinalIgnoreCase),
            LastUpdated = DateTimeOffset.UtcNow
        };
        dupeStore.Upsert(updated);
    }

    /// <summary>
    /// Read the stored penalty map from the dupe entry and credit each uploader back.
    /// Returns the map of username → restoredKb (empty if nothing was stored).
    /// Called before <see cref="UpdateDupeStore"/> clears <c>NukePenalties</c>.
    /// </summary>
    private static Dictionary<string, long> RestoreNukeCredits(
        IUserStore users,
        IDupeStore? dupeStore,
        string sectionName,
        string releaseName)
    {
        var restorations = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (dupeStore is null) return restorations;

        var entry = FindMatchingDupeEntry(dupeStore, sectionName, releaseName);
        if (entry is null || entry.NukePenalties.Count == 0) return restorations;

        foreach (var (userName, penaltyKb) in entry.NukePenalties)
        {
            var user = users.FindUser(userName);
            if (user is null) continue;

            var newCredits = user.CreditsKb + penaltyKb;
            if (newCredits < 0) newCredits = long.MaxValue; // overflow guard

            if (users.TryUpdateUser(user with { CreditsKb = newCredits }, out _))
                restorations[userName] = penaltyKb;
        }

        return restorations;
    }

    /// <summary>Append a credit-mutation block to the nukes log.</summary>
    private static void AppendCreditLog(string action, string virt, Dictionary<string, long> entries)
    {
        if (entries.Count == 0) return;
        try
        {
            Directory.CreateDirectory("logs");
            var sb = new StringBuilder();
            sb.Append(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss zzz"))
              .Append(" | ").Append(action)
              .Append(" | path=").AppendLine(virt);
            foreach (var (user, kb) in entries)
                sb.Append("  ").Append(user).Append(' ')
                  .Append(action.StartsWith("NUKE") ? '-' : '+').Append(kb).AppendLine("kb");
            File.AppendAllText("logs/nukes.log", sb.ToString());
        }
        catch { }
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
        candidates.AddRange(FindMatchingDupeEntries(dupeStore, sectionName, releaseName));

        var updatedAny = false;

        foreach (var entry in candidates)
        {
            if (!seen.Add(entry.Key))
                continue;

            var isSameVirt = !string.IsNullOrWhiteSpace(entry.VirtualPath) &&
                             entry.VirtualPath.TrimEnd('/', '\\')
                                 .Equals(releaseVirt, StringComparison.OrdinalIgnoreCase);

            var isSameName = IsMatchingReleaseName(entry.ReleaseName, releaseName);

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
                // Clear stored penalties when unnuking so stale data can't be replayed.
                NukePenalties = isNuked
                    ? entry.NukePenalties
                    : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
                LastUpdated = now
            };

            dupeStore.Upsert(updated);
            updatedAny = true;
        }

        if (!updatedAny && isNuked)
        {
            dupeStore.Upsert(new DupeEntry
            {
                ReleaseName = releaseName,
                SectionName = sectionName,
                VirtualPath = releaseVirt,
                TotalBytes = 0,
                FirstSeen = now,
                LastUpdated = now,
                IsNuked = true,
                NukeReason = reason,
                NukeMultiplier = (int)Math.Round(nukeMultiplier)
            });
        }
    }

    /// <summary>
    /// Returns all dupe entries that match <paramref name="releaseName"/>, including legacy
    /// <c>.NUKED</c> variants and stripped names.
    /// </summary>
    private static IReadOnlyList<DupeEntry> FindMatchingDupeEntries(
        IDupeStore dupeStore,
        string sectionName,
        string releaseName)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
            return Array.Empty<DupeEntry>();

        var matches = new List<DupeEntry>(8);
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var lookup in GetReleaseNameLookupKeys(releaseName))
        {
            var patternLookup = lookup.Contains('*') || lookup.Contains('?');
            if (patternLookup)
            {
                if (!string.IsNullOrWhiteSpace(sectionName))
                {
                    foreach (var e in dupeStore.Search(lookup, sectionName, limit: 100))
                    {
                        if (IsMatchingReleaseName(e.ReleaseName, releaseName) && dedupe.Add(e.Key))
                            matches.Add(e);
                    }
                }

                foreach (var e in dupeStore.Search(lookup, sectionName: null, limit: 100))
                {
                    if (IsMatchingReleaseName(e.ReleaseName, releaseName) && dedupe.Add(e.Key))
                        matches.Add(e);
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(sectionName))
                {
                    var exact = dupeStore.Find(sectionName, lookup);
                    if (exact is not null && dedupe.Add(exact.Key) && IsMatchingReleaseName(exact.ReleaseName, releaseName))
                        matches.Add(exact);
                }

                if (!string.IsNullOrWhiteSpace(sectionName))
                {
                    foreach (var e in dupeStore.Search(lookup, sectionName, limit: 100))
                    {
                        if (IsMatchingReleaseName(e.ReleaseName, releaseName) && dedupe.Add(e.Key))
                            matches.Add(e);
                    }
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// Returns the first matching dupe entry for persistence/restore actions.
    /// </summary>
    private static DupeEntry? FindMatchingDupeEntry(
        IDupeStore dupeStore,
        string sectionName,
        string releaseName)
    {
        var matches = FindMatchingDupeEntries(dupeStore, sectionName, releaseName);
        return matches.Count == 0 ? null : matches[0];
    }

    /// <summary>
    /// Returns lookup keys to resolve a release with or without <c>.NUKED</c> suffixes.
    /// </summary>
    private static IReadOnlyList<string> GetReleaseNameLookupKeys(string releaseName)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
            return Array.Empty<string>();

        var normalized = releaseName.Trim();
        var baseName = GetBaseReleaseName(normalized);
        var keys = new List<string>(3) { normalized };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedKeys = new List<string>(3);

        foreach (var key in keys)
        {
            if (seen.Add(key))
                orderedKeys.Add(key);
        }

        if (!string.IsNullOrWhiteSpace(baseName))
        {
            var nukedLookup = $"{baseName}.NUKED*";
            if (seen.Add(nukedLookup))
                orderedKeys.Add(nukedLookup);

            if (!string.Equals(baseName, normalized, StringComparison.OrdinalIgnoreCase))
            {
                if (seen.Add(baseName))
                    orderedKeys.Add(baseName);
            }
        }

        return orderedKeys;
    }

    /// <summary>
    /// Normalizes release names used in matching logic by stripping known .NUKED suffixes.
    /// Supports <c>.NUKED</c> and <c>.NUKED-YYYY...</c>.
    /// </summary>
    private static string GetBaseReleaseName(string releaseName)
    {
        var idx = releaseName.IndexOf(".NUKED", StringComparison.OrdinalIgnoreCase);
        if (idx <= 0)
            return releaseName;

        return releaseName[..idx];
    }

    /// <summary>
    /// Returns true if <paramref name="candidateRelease"/> identifies the same logical release as
    /// <paramref name="targetRelease"/> or one of its <c>.NUKED</c> variants.
    /// </summary>
    private static bool IsMatchingReleaseName(string candidateRelease, string targetRelease)
    {
        if (string.IsNullOrWhiteSpace(candidateRelease) || string.IsNullOrWhiteSpace(targetRelease))
            return false;

        if (candidateRelease.Equals(targetRelease, StringComparison.OrdinalIgnoreCase))
            return true;

        var baseName = GetBaseReleaseName(targetRelease);
        if (candidateRelease.Equals(baseName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!candidateRelease.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
            return false;

        return candidateRelease[baseName.Length..].StartsWith(
            ".NUKED",
            StringComparison.OrdinalIgnoreCase);
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
