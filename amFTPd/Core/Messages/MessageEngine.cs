/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           MessageEngine.cs
 *  Created:        2026-04-24
 *  Description:    Message / MOTD / CWD message system.
 *                  Reads message files from {configDir}/messages/ and from per-directory .message
 *                  files. Performs ioFTPD-compatible variable substitution.
 *
 *  Variable tokens (case-insensitive):
 *    %u  / %username   — logged-in username
 *    %g  / %group      — primary group
 *    %credits          — credits in MB (rounded)
 *    %credskb          — credits in KB
 *    %ratio            — "Leech" if NoRatio, else "1:" + ratio value
 *    %tagline          — (reserved, empty until tagline is added to FtpUser)
 *    %date             — current date  (UTC, yyyy-MM-dd)
 *    %time             — current time  (UTC, HH:mm:ss)
 *    %online           — number of active sessions
 *    %sitename         — site name from FtpConfig.SiteName (or "amFTPd")
 *
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using amFTPd.Config.Ftpd;

namespace amFTPd.Core.Messages;

/// <summary>
/// Reads and renders message/MOTD text files with variable substitution.
/// Stateless — reads from disk each time (OS caching handles repeated reads efficiently).
/// </summary>
public sealed class MessageEngine
{
    private readonly string _messagesDir;
    private readonly string _siteName;

    /// <summary>
    /// Standard MOTD filename, read from <see cref="_messagesDir"/>/motd.txt.
    /// </summary>
    public const string MotdFile = "motd.txt";

    /// <summary>
    /// Per-directory message filename. Placed in the physical directory it applies to.
    /// </summary>
    public const string DirMessageFile = ".message";

    public MessageEngine(string messagesDir, string siteName = "amFTPd")
    {
        _messagesDir = messagesDir ?? throw new ArgumentNullException(nameof(messagesDir));
        _siteName = siteName;
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>
    /// Render the MOTD file for <paramref name="account"/>.
    /// Returns null if the MOTD file does not exist.
    /// </summary>
    public string? RenderMotd(FtpUser? account, int activeSessions = 0)
    {
        var path = Path.Combine(_messagesDir, MotdFile);
        if (!File.Exists(path)) return null;

        try
        {
            var raw = File.ReadAllText(path);
            return Substitute(raw, account, activeSessions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Render the per-directory <c>.message</c> file found in <paramref name="physicalDir"/>.
    /// Returns null if no such file exists.
    /// </summary>
    public string? RenderDirMessage(string physicalDir, FtpUser? account, int activeSessions = 0)
    {
        if (string.IsNullOrWhiteSpace(physicalDir)) return null;

        var path = Path.Combine(physicalDir, DirMessageFile);
        if (!File.Exists(path)) return null;

        try
        {
            var raw = File.ReadAllText(path);
            return Substitute(raw, account, activeSessions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Wrap a rendered message block for sending on the FTP control channel.
    /// Multi-line responses use FTP "214-" / "214 " continuation format.
    /// </summary>
    public static string FormatAsMultiLine(string replyCode, string rendered)
    {
        var lines = rendered.Split('\n');
        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            bool isLast = i == lines.Length - 1;

            if (isLast && string.IsNullOrEmpty(line))
                break; // trailing newline — emit closing code without content

            if (i == 0 && lines.Length == 1)
                sb.Append($"{replyCode} {line}\r\n");
            else if (isLast)
                sb.Append($"{replyCode} {line}\r\n");
            else
                sb.Append($"{replyCode}-{line}\r\n");
        }

        // Always ensure a final "code " line
        if (!sb.ToString().Contains($"\r\n{replyCode} ") && !sb.ToString().StartsWith($"{replyCode} "))
            sb.Append($"{replyCode} \r\n");

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Variable substitution
    // ------------------------------------------------------------------

    private string Substitute(string template, FtpUser? account, int activeSessions)
    {
        var now = DateTimeOffset.UtcNow;

        var userName = account?.UserName ?? "?";
        var groupName = account?.GroupName ?? "-";
        var credsMb = account is not null ? $"{account.CreditsKb / 1024.0:F1} MB" : "0 MB";
        var credsKb = account?.CreditsKb.ToString() ?? "0";
        var ratio = account?.IsNoRatio == true ? "Leech" : "1:N/A";
        var online = activeSessions.ToString();

        return template
            .Replace("%username", userName, StringComparison.OrdinalIgnoreCase)
            .Replace("%u", userName, StringComparison.OrdinalIgnoreCase)
            .Replace("%group", groupName, StringComparison.OrdinalIgnoreCase)
            .Replace("%g", groupName, StringComparison.OrdinalIgnoreCase)
            .Replace("%credits", credsMb, StringComparison.OrdinalIgnoreCase)
            .Replace("%credskb", credsKb, StringComparison.OrdinalIgnoreCase)
            .Replace("%ratio", ratio, StringComparison.OrdinalIgnoreCase)
            .Replace("%tagline", "", StringComparison.OrdinalIgnoreCase)
            .Replace("%date", now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase)
            .Replace("%time", now.ToString("HH:mm:ss"), StringComparison.OrdinalIgnoreCase)
            .Replace("%online", online, StringComparison.OrdinalIgnoreCase)
            .Replace("%sitename", _siteName, StringComparison.OrdinalIgnoreCase);
    }
}
