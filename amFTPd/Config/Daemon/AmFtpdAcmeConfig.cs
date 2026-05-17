/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AmFtpdAcmeConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-25 00:00:00
 *  Last Modified:  2026-04-25 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      JSON-deserialisable configuration for the ACME v2 (Let's Encrypt / ZeroSSL / etc.)
 *      automatic certificate provisioning subsystem.
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
/// Configuration for the ACME v2 automatic TLS certificate subsystem.
/// When <see cref="Enabled"/> is <c>true</c> the daemon provisions (and auto-renews) a certificate
/// from any ACME v2 CA — defaults to Let's Encrypt production.
/// </summary>
/// <param name="Enabled">
/// Set to <c>true</c> to activate automatic certificate management.
/// When <c>false</c> the daemon falls through to the normal <c>Tls.PfxPath</c> / self-signed behaviour.
/// </param>
/// <param name="Domain">
/// Fully-qualified domain name the certificate should be issued for, e.g. <c>ftp.example.com</c>.
/// Must resolve to this machine's public IP for HTTP-01 challenges to succeed.
/// </param>
/// <param name="Email">
/// Contact e-mail sent to the CA when registering the ACME account.
/// Let's Encrypt uses this to send expiry warnings if auto-renewal stops working.
/// </param>
/// <param name="AccountKeyPath">
/// Path to the PEM file used to persist the EC P-256 ACME account key.
/// Relative paths are resolved from the daemon's config directory.
/// Defaults to <c>acme-account.pem</c> next to the main config file.
/// </param>
/// <param name="DirectoryUrl">
/// ACME v2 directory endpoint.
/// Defaults to Let's Encrypt production: <c>https://acme-v02.api.letsencrypt.org/directory</c>.
/// Use <c>https://acme-staging-v02.api.letsencrypt.org/directory</c> for testing.
/// </param>
/// <param name="RenewalThresholdDays">
/// Renew the certificate when fewer than this many days remain before expiry.
/// Default: 30 days.
/// </param>
/// <param name="ChallengePort">
/// TCP port the built-in HTTP-01 challenge server binds to.
/// Must be accessible as port 80 from the CA (router port-forward if behind NAT).
/// Default: 80.
/// </param>
public sealed record AmFtpdAcmeConfig(
    bool Enabled,
    string Domain,
    string Email,
    string AccountKeyPath = "acme-account.pem",
    string DirectoryUrl = "https://acme-v02.api.letsencrypt.org/directory",
    int RenewalThresholdDays = 30,
    int ChallengePort = 80
);
