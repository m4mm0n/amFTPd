/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AcmeCertificateManager.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-25 00:00:00
 *  Last Modified:  2026-04-25 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      Manages the lifecycle of an ACME-provisioned TLS certificate.
 *      On startup it checks whether the current certificate is close to expiry and
 *      kicks off an ACME renewal if needed.  A background timer repeats the check
 *      every 24 hours.  When a new certificate is written to disk the supplied
 *      rehash callback is invoked so FtpServer hot-reloads TLS without restarting.
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

using System.Security.Cryptography.X509Certificates;
using amFTPd.Config.Daemon;
using amFTPd.Logging;

namespace amFTPd.Core.Tls;

/// <summary>
/// Manages ACME certificate provisioning and renewal for a single domain.
/// </summary>
/// <remarks>
/// Create one instance per server start (it is recreated on REHASH).
/// Call <see cref="StartAsync"/> once to perform an immediate check and arm the renewal timer.
/// </remarks>
public sealed class AcmeCertificateManager : IAsyncDisposable
{
    private readonly AmFtpdAcmeConfig _cfg;
    private readonly string _pfxPath;
    private readonly string _pfxPassword;
    private readonly string _accountKeyPath;
    private readonly IFtpLogger _log;
    private readonly Func<CancellationToken, Task> _onRenewed;

    private readonly CancellationTokenSource _cts = new();
    private Timer? _timer;

    // Prevent concurrent renewal attempts (e.g. timer fires while initial run is still going)
    private int _renewalRunning;

    /// <summary>
    /// Initialises the manager.
    /// </summary>
    /// <param name="cfg">ACME configuration block from the JSON config file.</param>
    /// <param name="pfxPath">
    /// Absolute path where the resulting PFX certificate should be written.
    /// This should match <c>Tls.PfxPath</c> so that a subsequent REHASH picks it up.
    /// </param>
    /// <param name="pfxPassword">Password to use when exporting the PFX (may be empty string).</param>
    /// <param name="configDir">
    /// Base directory for resolving relative <see cref="AmFtpdAcmeConfig.AccountKeyPath"/>.
    /// Typically the directory that contains <c>amftpd.json</c>.
    /// </param>
    /// <param name="log">Logger.</param>
    /// <param name="onRenewed">
    /// Callback invoked after a new certificate has been written to <paramref name="pfxPath"/>.
    /// Should trigger a config reload (REHASH) so the new cert is picked up without restart.
    /// </param>
    public AcmeCertificateManager(
        AmFtpdAcmeConfig cfg,
        string pfxPath,
        string pfxPassword,
        string configDir,
        IFtpLogger log,
        Func<CancellationToken, Task> onRenewed)
    {
        _cfg = cfg;
        _pfxPath = pfxPath;
        _pfxPassword = pfxPassword;
        _log = log;
        _onRenewed = onRenewed;

        // Resolve account key path relative to the config directory
        _accountKeyPath = Path.IsPathRooted(cfg.AccountKeyPath)
            ? cfg.AccountKeyPath
            : Path.GetFullPath(Path.Combine(configDir, cfg.AccountKeyPath));
    }

    /// <summary>
    /// Performs an immediate certificate check/renewal (if needed) and arms a 24-hour renewal timer.
    /// </summary>
    public async Task StartAsync(CancellationToken externalCt = default)
    {
        _log.Log(FtpLogLevel.Info,
            $"[ACME] Manager started for domain '{_cfg.Domain}'. " +
            $"Renewal threshold: {_cfg.RenewalThresholdDays} days.");

        // Immediate check — fire and forget errors so startup never blocks
        try
        {
            await CheckAndRenewAsync(externalCt).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Log(FtpLogLevel.Error,
                $"[ACME] Initial certificate check failed: {ex.Message}", ex);
        }

        // Arm daily renewal check
        _timer = new Timer(OnTimerTick, null,
            dueTime: TimeSpan.FromHours(24),
            period: TimeSpan.FromHours(24));
    }

    // ── timer callback (fire-and-forget) ──────────────────────────────────────

    private async void OnTimerTick(object? _)
    {
        try
        {
            await CheckAndRenewAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            _log.Log(FtpLogLevel.Error,
                $"[ACME] Scheduled renewal check failed: {ex.Message}", ex);
        }
    }

    // ── core renewal logic ────────────────────────────────────────────────────

    /// <summary>
    /// Checks the current certificate's expiry date and triggers an ACME renewal
    /// if it is within the configured threshold (or absent).
    /// </summary>
    public async Task CheckAndRenewAsync(CancellationToken ct = default)
    {
        // Guard against concurrent runs (timer overlap + manual REHASH)
        if (Interlocked.CompareExchange(ref _renewalRunning, 1, 0) != 0)
        {
            _log.Log(FtpLogLevel.Warn, "[ACME] Renewal already in progress; skipping this check.");
            return;
        }

        try
        {
            if (IsCertificateStillValid())
                return;

            await RenewCertificateAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _renewalRunning, 0);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private bool IsCertificateStillValid()
    {
        if (!File.Exists(_pfxPath))
        {
            _log.Log(FtpLogLevel.Info,
                $"[ACME] No certificate found at '{_pfxPath}'. Provisioning now.");
            return false;
        }

        try
        {
            var pwd = string.IsNullOrEmpty(_pfxPassword) ? null : _pfxPassword;

#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(_pfxPath, pwd);
#pragma warning restore SYSLIB0057

            var daysRemaining = (cert.NotAfter.ToUniversalTime() - DateTime.UtcNow).TotalDays;

            _log.Log(FtpLogLevel.Info,
                $"[ACME] Certificate '{cert.Subject}' expires " +
                $"{cert.NotAfter:yyyy-MM-dd} ({daysRemaining:F0} days remaining). " +
                $"Renewal threshold: {_cfg.RenewalThresholdDays} days.");

            if (daysRemaining > _cfg.RenewalThresholdDays)
            {
                _log.Log(FtpLogLevel.Info, "[ACME] Certificate is still valid; no renewal needed.");
                return true;
            }

            _log.Log(FtpLogLevel.Info,
                $"[ACME] Certificate will expire in {daysRemaining:F0} days — renewing.");
            return false;
        }
        catch (Exception ex)
        {
            _log.Log(FtpLogLevel.Warn,
                $"[ACME] Could not read existing certificate at '{_pfxPath}' ({ex.Message}). " +
                "Attempting ACME renewal.");
            return false;
        }
    }

    private async Task RenewCertificateAsync(CancellationToken ct)
    {
        _log.Log(FtpLogLevel.Info,
            $"[ACME] Requesting certificate from {_cfg.DirectoryUrl} for '{_cfg.Domain}'…");

        byte[] pfxBytes;
        try
        {
            pfxBytes = await AcmeClient.RequestCertificateAsync(
                directoryUrl: _cfg.DirectoryUrl,
                domain: _cfg.Domain,
                email: _cfg.Email,
                accountKeyPath: _accountKeyPath,
                challengePort: _cfg.ChallengePort,
                pfxPassword: _pfxPassword,
                log: _log,
                ct: ct).ConfigureAwait(false);
        }
        catch (AcmeException ex)
        {
            _log.Log(FtpLogLevel.Error,
                $"[ACME] Certificate provisioning failed: {ex.Message}", ex);
            throw;
        }

        // Write atomically: temp file → rename
        var tmp = _pfxPath + ".acme.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_pfxPath))!);
            await File.WriteAllBytesAsync(tmp, pfxBytes, ct).ConfigureAwait(false);
            File.Move(tmp, _pfxPath, overwrite: true);
            _log.Log(FtpLogLevel.Info,
                $"[ACME] New certificate written to '{_pfxPath}'.");
        }
        catch (Exception ex)
        {
            _log.Log(FtpLogLevel.Error,
                $"[ACME] Failed to write certificate to '{_pfxPath}': {ex.Message}", ex);
            try { File.Delete(tmp); } catch { }
            throw;
        }

        // Notify FtpServer to reload TLS (REHASH)
        try
        {
            _log.Log(FtpLogLevel.Info,
                "[ACME] Triggering configuration reload to pick up the new certificate.");
            await _onRenewed(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Log(FtpLogLevel.Warn,
                $"[ACME] Rehash after renewal failed: {ex.Message}. " +
                "The new certificate will be used the next time the server restarts.", ex);
        }
    }

    // ── IAsyncDisposable ─────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);

        if (_timer is not null)
        {
            await _timer.DisposeAsync().ConfigureAwait(false);
            _timer = null;
        }

        _cts.Dispose();
    }
}
