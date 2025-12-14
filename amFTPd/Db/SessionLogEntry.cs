/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SessionLogEntry.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 16:25:47
 *  Last Modified:  2025-12-14 16:26:41
 *  CRC32:          0xA4ADAF48
 *  
 *  Description:
 *      Persistent, machine-readable audit entry for a notable FTP event. Typically derived from <see cref="FtpEvent"/> and w...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
using amFTPd.Core.Events;
using amFTPd.Logging;
using System.Text;
using System.Text.Json;

namespace amFTPd.Db
{
    /// <summary>
    /// Persistent, machine-readable audit entry for a notable FTP event.
    /// Typically derived from <see cref="FtpEvent"/> and written as JSON lines
    /// to a session log file.
    /// </summary>
    public sealed record SessionLogEntry
    {
        /// <summary>UTC timestamp when the event occurred.</summary>
        public DateTimeOffset Timestamp { get; init; }

        /// <summary>Numeric session identifier (if known).</summary>
        public int? SessionId { get; init; }

        /// <summary>User login name associated with the event (if any).</summary>
        public string? User { get; init; }

        /// <summary>Primary group name (if any).</summary>
        public string? Group { get; init; }

        /// <summary>Type of event (upload, login, nuke, etc.).</summary>
        public FtpEventType EventType { get; init; }

        /// <summary>Section name (e.g. "MP3") if applicable.</summary>
        public string? Section { get; init; }

        /// <summary>Virtual path (e.g. "/MP3/Artist-Album-2025-GRP").</summary>
        public string? VirtualPath { get; init; }

        /// <summary>Release name (last path segment) if relevant.</summary>
        public string? ReleaseName { get; init; }

        /// <summary>Number of bytes transferred / affected.</summary>
        public long? Bytes { get; init; }

        /// <summary>Remote host / IP, if resolved at the time of event.</summary>
        public string? RemoteHost { get; init; }

        /// <summary>Optional human-readable reason text (e.g. nuke reason).</summary>
        public string? Reason { get; init; }

        /// <summary>Free-form extra info (JSON-ish or "k=v;...").</summary>
        public string? Extra { get; init; }

        /// <summary>
        /// Builds a <see cref="SessionLogEntry"/> from a <see cref="FtpEvent"/>.
        /// </summary>
        public static SessionLogEntry FromEvent(FtpEvent ev)
            => new SessionLogEntry
            {
                Timestamp = ev.Timestamp,
                SessionId = ev.SessionId,
                User = ev.User,
                Group = ev.Group,
                EventType = ev.Type,
                Section = ev.Section,
                VirtualPath = ev.VirtualPath,
                ReleaseName = ev.ReleaseName,
                Bytes = ev.Bytes,
                RemoteHost = ev.RemoteHost,
                Reason = ev.Reason,
                Extra = ev.Extra
            };
    }
    /// <summary>
    /// Very simple JSON-lines writer for <see cref="SessionLogEntry"/> objects.
    /// Subscribes to the <see cref="EventBus"/> and appends one JSON object per line.
    /// </summary>
    public sealed class SessionLogWriter
    {
        private readonly string _logFilePath;
        private readonly IFtpLogger? _debugLog;
        private readonly object _fileLock = new();

        public SessionLogWriter(string logFilePath, IFtpLogger? debugLog = null)
        {
            if (string.IsNullOrWhiteSpace(logFilePath))
                throw new ArgumentException("Log file path must be non-empty.", nameof(logFilePath));

            _logFilePath = logFilePath;
            _debugLog = debugLog;

            var dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        /// <summary>
        /// EventBus handler. Converts the <paramref name="ev"/> into a <see cref="SessionLogEntry"/>
        /// and appends it to the JSONL file.
        /// </summary>
        public void OnEvent(FtpEvent ev)
        {
            try
            {
                var entry = SessionLogEntry.FromEvent(ev);
                Append(entry);
            }
            catch (Exception ex)
            {
                // Best-effort only; do not crash the daemon if logging fails.
                _debugLog?.Log(
                    FtpLogLevel.Debug,
                    $"SessionLogWriter failed for event {ev.Type}: {ex.Message}",
                    ex);
            }
        }

        private void Append(SessionLogEntry entry)
        {
            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            lock (_fileLock)
            {
                File.AppendAllText(_logFilePath, json + Environment.NewLine, Encoding.UTF8);
            }
        }
    }
}
