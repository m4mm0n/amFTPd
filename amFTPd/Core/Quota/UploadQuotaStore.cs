/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           UploadQuotaStore.cs
 *  Created:        2026-04-24
 *  Description:    Per-user upload quota tracking (daily / weekly / monthly).
 *
 *  Design:
 *    - Subscribes to EventBus Upload events; increments per-user counters.
 *    - On each increment, stale windows (past day/week/month) are lazily reset.
 *    - Limits are read from GroupConfig at check time (no caching).
 *    - Admins / siteops are never quota-blocked.
 *    - Thread-safe via per-user ConcurrentDictionary + Interlocked.
 *
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using System.Collections.Concurrent;
using amFTPd.Config.Ftpd;
using amFTPd.Core.Events;

namespace amFTPd.Core.Quota;

/// <summary>Snapshot of a user's current quota window usage.</summary>
public sealed record UserQuotaUsage(
    string UserName,
    long DailyBytesUsed,
    long WeeklyBytesUsed,
    long MonthlyBytesUsed,
    DateOnly DailyWindowDate,
    DateOnly WeeklyWindowStart,
    DateOnly MonthlyWindowStart)
{
    public long DailyMbUsed => DailyBytesUsed / 1_048_576;
    public long WeeklyMbUsed => WeeklyBytesUsed / 1_048_576;
    public long MonthlyMbUsed => MonthlyBytesUsed / 1_048_576;
}

/// <summary>Result of a quota check.</summary>
public sealed record QuotaCheckResult(bool Allowed, string? DeniedReason = null)
{
    public static readonly QuotaCheckResult Ok = new(true);
}

/// <summary>
/// Thread-safe in-memory store for per-user upload quota counters.
/// Subscribe to EventBus to feed it; call <see cref="Check"/> before STOR.
/// </summary>
public sealed class UploadQuotaStore
{
    private sealed class UserState
    {
        // Daily window
        public DateOnly DailyDate;
        public long DailyBytes;

        // Weekly window (Mon-based)
        public DateOnly WeekStart;
        public long WeeklyBytes;

        // Monthly window
        public DateOnly MonthStart;
        public long MonthlyBytes;

        public readonly Lock Lock = new();
    }

    private readonly ConcurrentDictionary<string, UserState> _users =
        new(StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------
    // EventBus hook
    // ------------------------------------------------------------------

    /// <summary>
    /// Subscribes to EventBus Upload events. Call as: <c>eventBus.Subscribe(quotaStore.OnEvent)</c>.
    /// </summary>
    public void OnEvent(FtpEvent ev)
    {
        if (ev.Type != FtpEventType.Upload) return;
        if (string.IsNullOrWhiteSpace(ev.User)) return;

        var bytes = ev.Bytes ?? 0L;
        if (bytes <= 0) return;

        var state = _users.GetOrAdd(ev.User, _ => new UserState());
        var today = DateOnly.FromDateTime(DateTime.Now);

        lock (state.Lock)
        {
            // Lazily reset windows that have rolled over
            RollWindows(state, today);

            state.DailyBytes += bytes;
            state.WeeklyBytes += bytes;
            state.MonthlyBytes += bytes;
        }
    }

    // ------------------------------------------------------------------
    // Check
    // ------------------------------------------------------------------

    /// <summary>
    /// Checks whether <paramref name="user"/> is allowed to upload more data,
    /// given the quota limits defined for their primary group.
    /// Returns <see cref="QuotaCheckResult.Ok"/> for admins and when no limits apply.
    /// </summary>
    public QuotaCheckResult Check(
        FtpUser user,
        IReadOnlyDictionary<string, GroupConfig> groups)
    {
        // Admins/siteops are never limited
        if (user.IsAdmin || user.IsSiteop) return QuotaCheckResult.Ok;

        // Determine effective group config
        GroupConfig? cfg = null;
        if (!string.IsNullOrEmpty(user.PrimaryGroup))
            groups.TryGetValue(user.PrimaryGroup, out cfg);

        if (cfg is null) return QuotaCheckResult.Ok;

        // Siteop group members skip quota
        if (cfg.IsSiteOp) return QuotaCheckResult.Ok;

        // Short-circuit if no limits configured for this group
        if (cfg.DailyUploadLimitMb == 0 &&
            cfg.WeeklyUploadLimitMb == 0 &&
            cfg.MonthlyUploadLimitMb == 0)
            return QuotaCheckResult.Ok;

        var state = _users.GetOrAdd(user.UserName, _ => new UserState());
        var today = DateOnly.FromDateTime(DateTime.Now);

        long daily, weekly, monthly;
        lock (state.Lock)
        {
            RollWindows(state, today);
            daily = state.DailyBytes;
            weekly = state.WeeklyBytes;
            monthly = state.MonthlyBytes;
        }

        // Convert limits to bytes for comparison
        if (cfg.DailyUploadLimitMb > 0)
        {
            var limit = cfg.DailyUploadLimitMb * 1_048_576L;
            if (daily >= limit)
                return new QuotaCheckResult(false,
                    $"Daily upload quota exceeded ({daily / 1_048_576}MB / {cfg.DailyUploadLimitMb}MB)");
        }

        if (cfg.WeeklyUploadLimitMb > 0)
        {
            var limit = cfg.WeeklyUploadLimitMb * 1_048_576L;
            if (weekly >= limit)
                return new QuotaCheckResult(false,
                    $"Weekly upload quota exceeded ({weekly / 1_048_576}MB / {cfg.WeeklyUploadLimitMb}MB)");
        }

        if (cfg.MonthlyUploadLimitMb > 0)
        {
            var limit = cfg.MonthlyUploadLimitMb * 1_048_576L;
            if (monthly >= limit)
                return new QuotaCheckResult(false,
                    $"Monthly upload quota exceeded ({monthly / 1_048_576}MB / {cfg.MonthlyUploadLimitMb}MB)");
        }

        return QuotaCheckResult.Ok;
    }

    // ------------------------------------------------------------------
    // Read usage (for SITE QUOTA)
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the current quota window usage for a user.
    /// Returns zeroed usage if the user has never uploaded.
    /// </summary>
    public UserQuotaUsage GetUsage(string userName)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        if (!_users.TryGetValue(userName, out var state))
        {
            return new UserQuotaUsage(
                userName, 0, 0, 0,
                today,
                StartOfWeek(today),
                new DateOnly(today.Year, today.Month, 1));
        }

        lock (state.Lock)
        {
            RollWindows(state, today);
            return new UserQuotaUsage(
                userName,
                state.DailyBytes,
                state.WeeklyBytes,
                state.MonthlyBytes,
                state.DailyDate,
                state.WeekStart,
                state.MonthStart);
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static void RollWindows(UserState s, DateOnly today)
    {
        // Daily
        if (s.DailyDate == default || s.DailyDate < today)
        {
            s.DailyDate = today;
            s.DailyBytes = 0;
        }

        // Weekly (Monday-based)
        var weekStart = StartOfWeek(today);
        if (s.WeekStart == default || s.WeekStart < weekStart)
        {
            s.WeekStart = weekStart;
            s.WeeklyBytes = 0;
        }

        // Monthly
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        if (s.MonthStart == default || s.MonthStart < monthStart)
        {
            s.MonthStart = monthStart;
            s.MonthlyBytes = 0;
        }
    }

    private static DateOnly StartOfWeek(DateOnly d)
    {
        var diff = (int)d.DayOfWeek - (int)DayOfWeek.Monday;
        if (diff < 0) diff += 7;
        return d.AddDays(-diff);
    }
}
