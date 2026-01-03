/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpEvent.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 03:45:47
 *  Last Modified:  2025-12-14 16:27:20
 *  CRC32:          0x2C28F1E0
 *  
 *  Description:
 *      Represents an event in an FTP system, encapsulating details such as the event type, timestamp, user information, and...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

namespace amFTPd.Core.Events
{
    /// <summary>
    /// Represents an event in an FTP system, encapsulating details such as the event type, timestamp, user information,
    /// and other contextual data.
    /// </summary>
    /// <remarks>This class is immutable and provides a structured way to capture and log FTP-related events. 
    /// It includes optional fields for additional context, such as the virtual path, release name, and bytes
    /// transferred. Use this class to represent events like uploads, downloads, or administrative actions in an FTP
    /// system.</remarks>
    public sealed class FtpEvent
    {
        public FtpEventType Type { get; init; }

        /// <summary>UTC timestamp when the event occurred.</summary>
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

        /// <summary>Numeric session identifier (if known).</summary>
        public int? SessionId { get; init; }

        /// <summary>User nick / login name associated with the event (if any).</summary>
        public string? User { get; init; }

        /// <summary>Primary group name (if known).</summary>
        public string? Group { get; init; }

        /// <summary>Section name (e.g. "MP3") if applicable.</summary>
        public string? Section { get; init; }

        /// <summary>Virtual path (e.g. "/MP3/Artist-Album-2025-GRP").</summary>
        public string? VirtualPath { get; init; }

        /// <summary>Release name (last path segment) if relevant.</summary>
        public string? ReleaseName { get; init; }

        /// <summary>Number of bytes transferred / affected (for upload/download/race).</summary>
        public long? Bytes { get; init; }

        /// <summary>Optional human-readable reason text (e.g. nuke reason).</summary>
        public string? Reason { get; init; }

        /// <summary>Remote host/IP if relevant.</summary>
        public string? RemoteHost { get; init; }

        /// <summary>Free-form extra info (JSON-ish or "k=v;..."), for scripts/logging.</summary>
        public string? Extra { get; init; }
    }
}
