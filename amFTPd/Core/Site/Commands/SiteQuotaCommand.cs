/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteQuotaCommand.cs
 *  Created:        2026-04-24
 *  Description:    SITE QUOTA [user] — show current upload quota usage.
 *
 *  Usage:
 *    SITE QUOTA          — shows your own quota usage
 *    SITE QUOTA <user>   — shows another user's usage (siteop only)
 *
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using amFTPd.Config.Ftpd;

namespace amFTPd.Core.Site.Commands;

/// <summary>
/// <c>SITE QUOTA [user]</c> — show upload quota usage for the current or named user.
/// </summary>
public sealed class SiteQuotaCommand : SiteCommandBase
{
    public override string Name => "QUOTA";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => false;
    public override string HelpText => "QUOTA [user]  - show upload quota usage";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken ct)
    {
        var s = context.Session;
        var runtime = context.Runtime;

        var quotaStore = runtime.UploadQuota;
        if (quotaStore is null)
        {
            await s.WriteAsync("550 Quota tracking not initialised.\r\n", ct);
            return;
        }

        // Resolve target user
        string targetName;
        FtpUser? targetUser;

        var arg = argument.Trim();
        if (string.IsNullOrEmpty(arg))
        {
            targetUser = s.Account;
            targetName = targetUser?.UserName ?? "<unknown>";
        }
        else
        {
            // Looking up another user requires siteop
            if (!context.IsSiteop)
            {
                await s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            targetName = arg;
            targetUser = context.Users.FindUser(targetName);
            if (targetUser is null)
            {
                await s.WriteAsync("550 User not found.\r\n", ct);
                return;
            }
        }

        // Resolve group config for quota limits
        GroupConfig? cfg = null;
        if (!string.IsNullOrEmpty(targetUser?.PrimaryGroup))
            runtime.TryGetGroup(targetUser.PrimaryGroup, out cfg);

        var usage = quotaStore.GetUsage(targetName);

        // Build response
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"200-Quota for {targetName} (group: {targetUser?.PrimaryGroup ?? "-"})");

        var dailyLimit = cfg?.DailyUploadLimitMb ?? 0;
        var weeklyLimit = cfg?.WeeklyUploadLimitMb ?? 0;
        var monthlyLimit = cfg?.MonthlyUploadLimitMb ?? 0;

        sb.AppendLine($"200-  Daily   : {FormatUsage(usage.DailyMbUsed, dailyLimit)}");
        sb.AppendLine($"200-  Weekly  : {FormatUsage(usage.WeeklyMbUsed, weeklyLimit)}  (window: {usage.WeeklyWindowStart})");
        sb.AppendLine($"200-  Monthly : {FormatUsage(usage.MonthlyMbUsed, monthlyLimit)}  (window: {usage.MonthlyWindowStart:yyyy-MM})");

        if (cfg is null || (dailyLimit == 0 && weeklyLimit == 0 && monthlyLimit == 0))
            sb.AppendLine("200-  No upload quota configured for this group.");

        sb.Append("200 End.");

        await s.WriteAsync(sb.ToString() + "\r\n", ct);
    }

    private static string FormatUsage(long usedMb, long limitMb)
    {
        if (limitMb == 0) return $"{usedMb} MB used  (unlimited)";

        var pct = limitMb > 0 ? (int)(usedMb * 100 / limitMb) : 0;
        return $"{usedMb} MB / {limitMb} MB  ({pct}%)";
    }
}
