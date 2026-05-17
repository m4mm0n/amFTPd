/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           OnelineEntry.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-24
 *
 *  Description:
 *      A single shoutbox / oneliner entry.
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 * ==================================================================================================== */

namespace amFTPd.Core.Oneliners;

/// <summary>A single shoutbox / oneliner entry.</summary>
public sealed record OnelineEntry
{
    /// <summary>Auto-incrementing ID, 1-based.</summary>
    public int Id { get; init; }

    /// <summary>Username who posted the line.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>Group of the posting user (snapshot at post time).</summary>
    public string? GroupName { get; init; }

    /// <summary>The message text.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the line was posted.</summary>
    public DateTimeOffset PostedAt { get; init; } = DateTimeOffset.UtcNow;
}
