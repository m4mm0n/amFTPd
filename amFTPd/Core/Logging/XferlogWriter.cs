/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           XferlogWriter.cs
 *  Created:        2026-04-24
 *  Description:    Writes FTP transfer events to a wu-ftpd-compatible xferlog file.
 *                  Scene ecosystem tools (stats bots, log analysers) speak xferlog natively.
 *
 *  Format (per wu-ftpd):
 *    DDD MMM DD HH:MM:SS YYYY <xfer-secs> <remote-host> <bytes> <filename>
 *      <type> <flag> <direction> <access> <username> svc auth-type auth-id status
 *
 *  Example:
 *    Mon Apr 24 12:00:00 2026 1 192.168.1.1 1048576 /0DAY/Release/file.rar b _ i r n00bmk ftp 0 * c
 *
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using System.Text;
using amFTPd.Core.Events;

namespace amFTPd.Core.Logging;

/// <summary>
/// Subscribes to <see cref="EventBus"/> and appends one wu-ftpd xferlog line per
/// completed upload or download.
/// </summary>
public sealed class XferlogWriter
{
    private readonly string _logFilePath;
    private readonly Lock _lock = new();

    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] MonthNames = ["Jan","Feb","Mar","Apr","May","Jun",
                                                    "Jul","Aug","Sep","Oct","Nov","Dec"];

    public XferlogWriter(string logFilePath)
    {
        if (string.IsNullOrWhiteSpace(logFilePath))
            throw new ArgumentException("Log file path must not be empty.", nameof(logFilePath));

        _logFilePath = logFilePath;

        var dir = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
    }

    // ------------------------------------------------------------------
    // EventBus hook
    // ------------------------------------------------------------------

    /// <summary>
    /// Call as an EventBus subscriber. Writes one xferlog line for
    /// <see cref="FtpEventType.Upload"/> and <see cref="FtpEventType.Download"/> events.
    /// </summary>
    public void OnEvent(FtpEvent ev)
    {
        if (ev.Type is not (FtpEventType.Upload or FtpEventType.Download))
            return;

        try
        {
            var line = BuildLine(ev);
            lock (_lock)
                File.AppendAllText(_logFilePath, line + "\n", Encoding.ASCII);
        }
        catch { /* best-effort — never crash the daemon */ }
    }

    // ------------------------------------------------------------------
    // Format builder
    // ------------------------------------------------------------------

    private static string BuildLine(FtpEvent ev)
    {
        var ts = ev.Timestamp.LocalDateTime;  // xferlog uses local time
        var day = DayNames[(int)ts.DayOfWeek];
        var month = MonthNames[ts.Month - 1];
        var date = $"{day} {month} {ts.Day,2} {ts.Hour:D2}:{ts.Minute:D2}:{ts.Second:D2} {ts.Year}";

        // Transfer time: stored in Extra as "xfer_secs=N" if available, else default 1
        long xferSecs = 1;
        if (ev.Extra is not null)
        {
            var m = System.Text.RegularExpressions.Regex.Match(ev.Extra, @"xfer_secs=(\d+)");
            if (m.Success) xferSecs = long.Parse(m.Groups[1].Value);
        }

        var remoteHost = ev.RemoteHost ?? "0.0.0.0";
        var bytes = ev.Bytes ?? 0;
        var filename = ev.VirtualPath ?? "/unknown";

        // transfer type: b = binary (FTP almost always binary)
        const string type = "b";

        // special action flags: _ = none
        const string flags = "_";

        // direction: i = incoming (upload), o = outgoing (download)
        var direction = ev.Type == FtpEventType.Upload ? "i" : "o";

        // access mode: r = real user
        const string access = "r";

        var username = ev.User ?? "anonymous";

        // service, auth-type, auth-user-id
        const string service = "ftp";
        const string authType = "0";
        const string authId = "*";

        // completion: c = complete
        const string status = "c";

        return $"{date} {xferSecs} {remoteHost} {bytes} {filename} {type} {flags} {direction} {access} {username} {service} {authType} {authId} {status}";
    }
}
