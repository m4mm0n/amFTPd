/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           RequestEntry.cs
 *  Created:        2026-04-24
 *  Description:    A single release request.
 *  License:        MIT License / https://opensource.org/licenses/MIT
 * ==================================================================================================== */

namespace amFTPd.Core.Requests;

/// <summary>A single release request.</summary>
public sealed record RequestEntry
{
    /// <summary>Auto-incrementing ID, 1-based.</summary>
    public int Id { get; init; }

    /// <summary>The requested release name.</summary>
    public string ReleaseName { get; init; } = string.Empty;

    /// <summary>Username who posted the request.</summary>
    public string RequestedBy { get; init; } = string.Empty;

    /// <summary>Group of the requesting user (snapshot at request time).</summary>
    public string? GroupName { get; init; }

    /// <summary>UTC timestamp when the request was created.</summary>
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>True if the request has been filled.</summary>
    public bool IsFilled { get; init; }

    /// <summary>Username who filled the request, if filled.</summary>
    public string? FilledBy { get; init; }

    /// <summary>UTC timestamp when the request was filled, if filled.</summary>
    public DateTimeOffset? FilledAt { get; init; }
}
