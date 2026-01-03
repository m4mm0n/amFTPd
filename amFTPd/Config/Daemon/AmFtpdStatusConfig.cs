/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AmFtpdStatusConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 17:14:10
 *  Last Modified:  2025-12-14 17:19:40
 *  CRC32:          0x993C0CE9
 *  
 *  Description:
 *      Configuration for the optional HTTP status endpoint.
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
/// Configuration for the optional HTTP status endpoint.
/// </summary>
/// <param name="Enabled">Whether the HTTP status endpoint should be started.</param>
/// <param name="BindAddress">IP or hostname to bind the status listener to, e.g. "127.0.0.1".</param>
/// <param name="Port">TCP port for the status listener, e.g. 8080.</param>
/// <param name="Path">
/// URL path for the status endpoint, e.g. "/amftpd-status/".
/// Will be normalized to start and end with a '/'.
/// </param>
public sealed record AmFtpdStatusConfig(
    bool Enabled,
    string BindAddress,
    int Port,
    string Path
)
{
    /// <summary>
    /// Optional token to protect status/metrics endpoints.
    /// If set, requests must include either:
    /// - Header: Authorization: Bearer &lt;token&gt;
    /// - Header: X-AmFTPd-Token: &lt;token&gt;
    /// - Query: ?token=&lt;token&gt;
    /// </summary>
    public string? AuthToken { get; init; }

    /// <summary>
    /// Whether the separate Prometheus metrics endpoint should be started.
    /// Default: true (if Status is enabled).
    /// </summary>
    public bool MetricsEnabled { get; init; } = true;

    /// <summary>
    /// Optional TCP port for the Prometheus metrics endpoint.
    /// If null, defaults to <see cref="Port"/> + 1.
    /// </summary>
    public int? MetricsPort { get; init; }

    /// <summary>
    /// Whether status JSON should include IP stats by default.
    /// You can still control this per-request via ?ips=true/false.
    /// Default: true.
    /// </summary>
    public bool IncludeIpStatsByDefault { get; init; } = true;

    /// <summary>
    /// Maximum number of IP entries to include (top-N by traffic).
    /// Remaining entries are rolled into an "_other" bucket.
    /// Default: 10.
    /// </summary>
    public int MaxIpEntries { get; init; } = 10;
}
