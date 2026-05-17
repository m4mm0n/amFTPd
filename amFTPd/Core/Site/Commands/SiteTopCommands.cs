/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteTopCommands.cs
 *  Created:        2026-04-24
 *  Description:    Section stats leaderboard SITE commands.
 *
 *  Commands (all siteop-accessible, available to all users):
 *    SITE TOP    [count] [section]  — top uploaders all-time
 *    SITE TOPDAY [count] [section]  — top uploaders today
 *    SITE TOPWK  [count] [section]  — top uploaders this week
 *    SITE TOPMTH [count] [section]  — top uploaders this month
 *    SITE TOPGRP [count] [section]  — top groups (by uploaded bytes)
 *
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using amFTPd.Core.Stats;

namespace amFTPd.Core.Site.Commands;

// -----------------------------------------------------------------------
// Shared formatter + argument parser — not a command itself
// -----------------------------------------------------------------------
internal static class TopCommandHelper
{
    internal const int DefaultCount = 10;
    internal const int MaxCount = 100;

    /// <summary>
    /// Parses optional argument: "[count] [section]"
    /// Both parts are optional. If count is omitted, returns DefaultCount.
    /// </summary>
    internal static (int Count, string? Section) ParseArgs(string argument)
    {
        var parts = argument.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        int count = DefaultCount;
        string? section = null;

        if (parts.Length >= 1)
        {
            if (int.TryParse(parts[0], out var n))
            {
                count = Math.Clamp(n, 1, MaxCount);
                if (parts.Length >= 2)
                    section = parts[1];
            }
            else
            {
                // First token is not a number → it's the section name
                section = parts[0];
                if (parts.Length >= 2 && int.TryParse(parts[1], out var n2))
                    count = Math.Clamp(n2, 1, MaxCount);
            }
        }

        return (count, section);
    }

    /// <summary>
    /// Format a leaderboard result set into a multi-line SITE response.
    /// </summary>
    internal static string FormatLeaderboard(
        IReadOnlyList<LeaderboardEntry> entries,
        string windowLabel,
        string? sectionLabel,
        bool isGroup)
    {
        var sb = new System.Text.StringBuilder();

        var sectionStr = sectionLabel is not null ? $" [{sectionLabel}]" : string.Empty;
        var title = isGroup
            ? $"Top {entries.Count} groups{sectionStr} — {windowLabel}"
            : $"Top {entries.Count} uploaders{sectionStr} — {windowLabel}";

        sb.AppendLine($"200-{title}");

        if (entries.Count == 0)
        {
            sb.Append("200 No data for this window.");
            return sb.ToString();
        }

        // Header
        if (isGroup)
            sb.AppendLine("200- # Group              Files     Uploaded      Downloaded");
        else
            sb.AppendLine("200- # User         Group       Files     Uploaded      Downloaded");

        sb.AppendLine("200- " + new string('-', 70));

        // Rows
        long maxBytes = entries.Max(e => e.BytesUploaded);

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var pos = (i + 1).ToString().PadLeft(2);
            var upMb = FormatSize(e.BytesUploaded);
            var dnMb = FormatSize(e.BytesDownloaded);

            if (isGroup)
            {
                var name = Truncate(e.Name, 18).PadRight(18);
                sb.AppendLine(
                    $"200- {pos} {name}  {e.Files,5}  {upMb,12}  {dnMb,12}");
            }
            else
            {
                var user = Truncate(e.Name, 12).PadRight(12);
                var grp = Truncate(e.Group ?? "-", 10).PadRight(10);
                sb.AppendLine(
                    $"200- {pos} {user}  {grp}  {e.Files,5}  {upMb,12}  {dnMb,12}");
            }
        }

        sb.AppendLine("200- " + new string('-', 70));
        sb.Append($"200 {entries.Count} entr{(entries.Count == 1 ? "y" : "ies")} shown.");
        return sb.ToString();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824L)
            return $"{bytes / 1_073_741_824.0:0.0} GB";
        if (bytes >= 1_048_576L)
            return $"{bytes / 1_048_576.0:0.0} MB";
        if (bytes >= 1024L)
            return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes} B";
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max];
}

// -----------------------------------------------------------------------
// SITE TOP
// -----------------------------------------------------------------------
/// <summary><c>SITE TOP [count] [section]</c> — top uploaders all time.</summary>
public sealed class SiteTopCommand : SiteCommandBase
{
    public override string Name => "TOP";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => false;
    public override string HelpText => "TOP [count] [section]  - top uploaders all-time";

    public override async Task ExecuteAsync(
        SiteCommandContext context, string argument, CancellationToken ct)
    {
        var (count, section) = TopCommandHelper.ParseArgs(argument);
        var logPath = SessionLogStatsService.GetDefaultLogPath(context.Runtime);
        var entries = LeaderboardService.TopUploaders(logPath, LeaderboardWindow.AllTime, count, section);
        var text = TopCommandHelper.FormatLeaderboard(entries, "all-time", section, isGroup: false);
        await context.Session.WriteAsync(text + "\r\n", ct);
    }
}

// -----------------------------------------------------------------------
// SITE TOPDAY
// -----------------------------------------------------------------------
/// <summary><c>SITE TOPDAY [count] [section]</c> — top uploaders today.</summary>
public sealed class SiteTopdayCommand : SiteCommandBase
{
    public override string Name => "TOPDAY";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => false;
    public override string HelpText => "TOPDAY [count] [section]  - top uploaders today";

    public override async Task ExecuteAsync(
        SiteCommandContext context, string argument, CancellationToken ct)
    {
        var (count, section) = TopCommandHelper.ParseArgs(argument);
        var logPath = SessionLogStatsService.GetDefaultLogPath(context.Runtime);
        var entries = LeaderboardService.TopUploaders(logPath, LeaderboardWindow.Today, count, section);
        var text = TopCommandHelper.FormatLeaderboard(entries, "today", section, isGroup: false);
        await context.Session.WriteAsync(text + "\r\n", ct);
    }
}

// -----------------------------------------------------------------------
// SITE TOPWK
// -----------------------------------------------------------------------
/// <summary><c>SITE TOPWK [count] [section]</c> — top uploaders this week.</summary>
public sealed class SiteTopwkCommand : SiteCommandBase
{
    public override string Name => "TOPWK";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => false;
    public override string HelpText => "TOPWK [count] [section]  - top uploaders this week";

    public override async Task ExecuteAsync(
        SiteCommandContext context, string argument, CancellationToken ct)
    {
        var (count, section) = TopCommandHelper.ParseArgs(argument);
        var logPath = SessionLogStatsService.GetDefaultLogPath(context.Runtime);
        var entries = LeaderboardService.TopUploaders(logPath, LeaderboardWindow.ThisWeek, count, section);
        var text = TopCommandHelper.FormatLeaderboard(entries, "this week", section, isGroup: false);
        await context.Session.WriteAsync(text + "\r\n", ct);
    }
}

// -----------------------------------------------------------------------
// SITE TOPMTH
// -----------------------------------------------------------------------
/// <summary><c>SITE TOPMTH [count] [section]</c> — top uploaders this month.</summary>
public sealed class SiteTopmthCommand : SiteCommandBase
{
    public override string Name => "TOPMTH";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => false;
    public override string HelpText => "TOPMTH [count] [section]  - top uploaders this month";

    public override async Task ExecuteAsync(
        SiteCommandContext context, string argument, CancellationToken ct)
    {
        var (count, section) = TopCommandHelper.ParseArgs(argument);
        var logPath = SessionLogStatsService.GetDefaultLogPath(context.Runtime);
        var entries = LeaderboardService.TopUploaders(logPath, LeaderboardWindow.ThisMonth, count, section);
        var text = TopCommandHelper.FormatLeaderboard(entries, "this month", section, isGroup: false);
        await context.Session.WriteAsync(text + "\r\n", ct);
    }
}

// -----------------------------------------------------------------------
// SITE TOPGRP
// -----------------------------------------------------------------------
/// <summary><c>SITE TOPGRP [count] [section]</c> — top groups by uploaded bytes.</summary>
public sealed class SiteTopgrpCommand : SiteCommandBase
{
    public override string Name => "TOPGRP";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => false;
    public override string HelpText => "TOPGRP [count] [section]  - top groups by uploaded bytes";

    public override async Task ExecuteAsync(
        SiteCommandContext context, string argument, CancellationToken ct)
    {
        var (count, section) = TopCommandHelper.ParseArgs(argument);
        var logPath = SessionLogStatsService.GetDefaultLogPath(context.Runtime);
        var entries = LeaderboardService.TopGroups(logPath, LeaderboardWindow.AllTime, count, section);
        var text = TopCommandHelper.FormatLeaderboard(entries, "all-time", section, isGroup: true);
        await context.Session.WriteAsync(text + "\r\n", ct);
    }
}
