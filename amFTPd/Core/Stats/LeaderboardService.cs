/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           LeaderboardService.cs
 *  Created:        2026-04-24
 *  Description:    Reads the JSONL session log and computes per-user / per-group
 *                  upload/download leaderboards for arbitrary time windows and
 *                  optional section filters.
 *
 *  Used by: SITE TOP / TOPDAY / TOPWK / TOPMTH / TOPGRP
 *
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using System.Text;
using System.Text.Json;
using amFTPd.Core.Events;
using amFTPd.Db;

namespace amFTPd.Core.Stats;

/// <summary>
/// One entry in a leaderboard — either a user or a group aggregate.
/// </summary>
public sealed record LeaderboardEntry(
    string Name,
    string? Group,
    long Files,
    long BytesUploaded,
    long BytesDownloaded)
{
    public long TotalBytes => BytesUploaded + BytesDownloaded;
}

/// <summary>
/// Window enum used by all TOP commands.
/// </summary>
public enum LeaderboardWindow
{
    AllTime,
    Today,
    ThisWeek,
    ThisMonth,
}

/// <summary>
/// Computes upload/download leaderboards from the JSONL session log.
/// </summary>
public static class LeaderboardService
{
    // ------------------------------------------------------------------
    // Main entry points
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the top N uploaders for the given window and optional section.
    /// </summary>
    public static IReadOnlyList<LeaderboardEntry> TopUploaders(
        string logFilePath,
        LeaderboardWindow window,
        int count,
        string? sectionFilter = null)
    {
        var (from, to) = WindowBounds(window);
        var rows = Aggregate(logFilePath, from, to, sectionFilter, byGroup: false);

        return rows
            .OrderByDescending(r => r.BytesUploaded)
            .ThenByDescending(r => r.Files)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Returns the top N groups by upload bytes.
    /// </summary>
    public static IReadOnlyList<LeaderboardEntry> TopGroups(
        string logFilePath,
        LeaderboardWindow window,
        int count,
        string? sectionFilter = null)
    {
        var (from, to) = WindowBounds(window);
        var rows = Aggregate(logFilePath, from, to, sectionFilter, byGroup: true);

        return rows
            .OrderByDescending(r => r.BytesUploaded)
            .ThenByDescending(r => r.Files)
            .Take(count)
            .ToList();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    public static (DateTimeOffset From, DateTimeOffset To) WindowBounds(LeaderboardWindow window)
    {
        var now = DateTimeOffset.Now;
        var today = now.Date;

        return window switch
        {
            LeaderboardWindow.Today => (new DateTimeOffset(today), new DateTimeOffset(today.AddDays(1))),
            LeaderboardWindow.ThisWeek => (new DateTimeOffset(StartOfWeek(today)), now),
            LeaderboardWindow.ThisMonth => (new DateTimeOffset(new DateTime(today.Year, today.Month, 1)), now),
            _ => (DateTimeOffset.MinValue, DateTimeOffset.MaxValue), // AllTime
        };
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        // Monday-based week (scene standard)
        var diff = (int)date.DayOfWeek - (int)DayOfWeek.Monday;
        if (diff < 0) diff += 7;
        return date.AddDays(-diff);
    }

    private static List<LeaderboardEntry> Aggregate(
        string logFilePath,
        DateTimeOffset from,
        DateTimeOffset to,
        string? sectionFilter,
        bool byGroup)
    {
        // key → (files, bytesUp, bytesDown, primaryGroup)
        var dict = new Dictionary<string, (long Files, long BytesUp, long BytesDown, string? Group)>(
            StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(logFilePath))
            return [];

        try
        {
            using var fs = File.Open(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                SessionLogEntry? entry;
                try { entry = JsonSerializer.Deserialize<SessionLogEntry>(line); }
                catch { continue; }

                if (entry is null) continue;
                if (entry.EventType is not (FtpEventType.Upload or FtpEventType.Download)) continue;
                if (entry.Timestamp < from || entry.Timestamp >= to) continue;

                // Section filter
                if (sectionFilter is not null &&
                    !string.Equals(entry.Section, sectionFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var bytes = entry.Bytes ?? 0L;
                var key = byGroup
                    ? (entry.Group ?? "<ungrouped>")
                    : (entry.User ?? "<unknown>");

                dict.TryGetValue(key, out var cur);

                long filesInc = entry.EventType == FtpEventType.Upload ? 1 : 0;
                long upInc = entry.EventType == FtpEventType.Upload ? bytes : 0;
                long dnInc = entry.EventType == FtpEventType.Download ? bytes : 0;

                dict[key] = (
                    cur.Files + filesInc,
                    cur.BytesUp + upInc,
                    cur.BytesDown + dnInc,
                    cur.Group ?? entry.Group
                );
            }
        }
        catch
        {
            return [];
        }

        return dict.Select(kv => new LeaderboardEntry(
            Name: kv.Key,
            Group: byGroup ? null : kv.Value.Group,
            Files: kv.Value.Files,
            BytesUploaded: kv.Value.BytesUp,
            BytesDownloaded: kv.Value.BytesDown))
            .ToList();
    }
}
