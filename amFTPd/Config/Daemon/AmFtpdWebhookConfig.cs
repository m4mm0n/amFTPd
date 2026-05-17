/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AmFtpdWebhookConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-25 00:00:00
 *  Last Modified:  2026-04-25 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      Configuration for the outbound HTTP webhook system.
 *
 *      Webhooks are fired as HTTP POSTs (JSON body) on configured FTP events.
 *      Each event type can have its own URL; a DefaultUrl acts as a catch-all
 *      for event types without an explicit entry.
 *
 *      Payloads are signed with HMAC-SHA256 when SigningSecret is set:
 *          X-AmFTPd-Signature: sha256=<lowercase hex>
 *          X-AmFTPd-Event:     <EventTypeName>
 *          User-Agent:         amFTPd-webhook/1.0
 *
 *      JSON payload shape:
 *      {
 *          "event":     "Upload",
 *          "timestamp": "2026-04-25T12:00:00Z",
 *          "data": {
 *              "user":        "bob",
 *              "group":       "GRP",
 *              "section":     "0DAY",
 *              "virtualPath": "/0DAY/Release-GRP",
 *              "releaseName": "Release-GRP",
 *              "bytes":       12345678,
 *              "reason":      null,
 *              "remoteHost":  "1.2.3.4",
 *              "extra":       null
 *          }
 *      }
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

namespace amFTPd.Config.Daemon;

/// <summary>
/// Configuration for the outbound HTTP webhook dispatcher.
/// </summary>
public sealed record AmFtpdWebhookConfig
{
    /// <summary>Master switch. Default: true.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// HMAC-SHA256 secret used to sign every outbound payload.
    /// When set, the signature is included as:
    ///     X-AmFTPd-Signature: sha256=&lt;lowercase hex&gt;
    /// Leave null/empty to disable signing.
    /// </summary>
    public string? SigningSecret { get; init; }

    /// <summary>
    /// HTTP request timeout per attempt, in seconds. Default: 5.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 5;

    /// <summary>
    /// Maximum number of additional retry attempts on failure (0 = fire once, no retry).
    /// Default: 1.
    /// </summary>
    public int MaxRetries { get; init; } = 1;

    // ── Per-event URLs ──────────────────────────────────────────────────────
    // If a URL is null or empty, that event type is silently skipped.
    // AutoNuke shares the NukeUrl.

    /// <summary>Fired on STOR completion (upload finished successfully).</summary>
    public string? UploadUrl { get; init; }

    /// <summary>Fired on RETR completion (download finished successfully).</summary>
    public string? DownloadUrl { get; init; }

    /// <summary>Fired on SITE NUKE or auto-nuke.</summary>
    public string? NukeUrl { get; init; }

    /// <summary>Fired on SITE UNNUKE.</summary>
    public string? UnnukeUrl { get; init; }

    /// <summary>Fired on SITE PRE (approved pres only; pending pres fire when approved).</summary>
    public string? PreUrl { get; init; }

    /// <summary>Fired when a race reaches completion (all files present, SFV clean).</summary>
    public string? RaceCompleteUrl { get; init; }

    /// <summary>Fired on successful USER/PASS login.</summary>
    public string? LoginUrl { get; init; }

    /// <summary>Fired on session disconnect (normal or kicked).</summary>
    public string? LogoutUrl { get; init; }

    /// <summary>Fired on SITE REQUEST submission.</summary>
    public string? RequestUrl { get; init; }

    /// <summary>Fired on DELETE (file) or WIPE (directory tree).</summary>
    public string? DeleteUrl { get; init; }

    /// <summary>
    /// Catch-all URL for any event type that doesn't have a specific URL configured.
    /// Can be combined with per-event URLs (e.g. NukeUrl overrides DefaultUrl for nukes).
    /// </summary>
    public string? DefaultUrl { get; init; }
}
