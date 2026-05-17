/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AuditLogWriter.cs
 *  Created:        2026-04-24
 *  Description:    Structured mutation audit log.
 *                  One JSON-line per admin action (adduser, nuke, kick, rehash, …).
 *
 *  Format (JSONL):
 *    {"ts":"2026-04-24T12:00:00Z","actor":"n00bmk","action":"ADDUSER","target":"newuser",
 *     "detail":"group=SITEOP","ip":"192.168.1.1"}
 *
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace amFTPd.Core.Logging;

/// <summary>
/// A single admin-action audit entry.
/// </summary>
public sealed class AuditEntry
{
    /// <summary>UTC timestamp of the action.</summary>
    [JsonPropertyName("ts")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Username that performed the action ("*system*" for automated actions).</summary>
    [JsonPropertyName("actor")]
    public string Actor { get; init; } = "*system*";

    /// <summary>Action verb (uppercase: ADDUSER, DELUSER, NUKE, KICK, REHASH, …).</summary>
    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    /// <summary>Subject of the action (username, release path, IP address, …). May be empty.</summary>
    [JsonPropertyName("target")]
    public string? Target { get; init; }

    /// <summary>Free-form human-readable detail. May be empty.</summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    /// <summary>Remote IP of the session that issued the command. May be empty for system actions.</summary>
    [JsonPropertyName("ip")]
    public string? Ip { get; init; }
}

/// <summary>
/// Thread-safe append-only audit log writer.
/// Writes one JSON line per <see cref="AuditEntry"/> to a flat JSONL file.
/// </summary>
public sealed class AuditLogWriter
{
    private readonly string _logFilePath;
    private readonly Lock _lock = new();

    private static readonly JsonSerializerOptions SerOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AuditLogWriter(string logFilePath)
    {
        if (string.IsNullOrWhiteSpace(logFilePath))
            throw new ArgumentException("Log file path must not be empty.", nameof(logFilePath));

        _logFilePath = logFilePath;

        var dir = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
    }

    // ------------------------------------------------------------------
    // Write
    // ------------------------------------------------------------------

    /// <summary>
    /// Appends a single audit entry. Never throws — logging is best-effort.
    /// </summary>
    public void Log(AuditEntry entry)
    {
        try
        {
            var line = JsonSerializer.Serialize(entry, SerOpts);
            lock (_lock)
                File.AppendAllText(_logFilePath, line + "\n", Encoding.UTF8);
        }
        catch { /* best-effort — never crash the daemon */ }
    }

    /// <summary>
    /// Convenience overload for one-liners.
    /// </summary>
    public void Log(
        string actor,
        string action,
        string? target = null,
        string? detail = null,
        string? ip = null)
    {
        Log(new AuditEntry
        {
            Actor = actor,
            Action = action,
            Target = target,
            Detail = detail,
            Ip = ip,
        });
    }

    // ------------------------------------------------------------------
    // Read (for SITE AUDITLOG)
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the last <paramref name="count"/> lines from the audit log, newest last.
    /// Returns an empty list when the log does not exist yet.
    /// </summary>
    public IReadOnlyList<string> ReadLastLines(int count)
    {
        if (count <= 0) return [];

        try
        {
            if (!File.Exists(_logFilePath))
                return [];

            // Tail the file without reading the whole thing into memory.
            var results = new List<string>(count);

            lock (_lock)
            {
                using var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs, Encoding.UTF8);

                // Read all lines into a ring buffer of size `count`.
                var ring = new string[count];
                int pos = 0, total = 0;

                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    ring[pos % count] = line;
                    pos++;
                    total++;
                }

                int filled = Math.Min(total, count);
                int start = total > count ? pos % count : 0;

                for (int i = 0; i < filled; i++)
                    results.Add(ring[(start + i) % count]);
            }

            return results;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Parses a JSONL line back into an <see cref="AuditEntry"/>.
    /// Returns null when the line is malformed.
    /// </summary>
    public static AuditEntry? ParseLine(string line)
    {
        try { return JsonSerializer.Deserialize<AuditEntry>(line, SerOpts); }
        catch { return null; }
    }
}
