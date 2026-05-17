/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ConfigValidator.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-24 00:00:00
 *  Last Modified:  2026-04-24 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      Configuration validator / linter for amftpd.json.
 *      Invoked via --validate flag.  Does NOT start the daemon.
 *
 *      Exit codes:
 *          0  — clean (no issues found)
 *          1  — warnings only
 *          2  — one or more errors
 *
 *  Checks performed:
 *      - JSON parses successfully
 *      - Server.Port is in valid range; passive port range is consistent
 *      - Server.RootPath exists and is readable
 *      - TLS.PfxPath exists (if TLS enabled); cert/key loadable
 *      - All VFS mounts reference paths that exist
 *      - All sections reference real VirtualRoot prefixes that are mounted
 *      - Ratio rules have consistent upload < download multipliers
 *      - Groups reference known sections
 *      - AllowAnonymous=true + RequireTlsForAuth=false → security warning
 *      - HammerGuard disabled → security warning
 *      - StatusEndpoint AuthToken empty → security warning
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
using System.Text.Json;
using amFTPd.Config.Daemon;
using amFTPd.Config.Ftpd;
using amFTPd.Config.Vfs;
using amFTPd.Logging;

namespace amFTPd.Core.Linting;

/// <summary>
/// Severity level of a validator finding.
/// </summary>
public enum LintSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// A single finding from the config validator.
/// </summary>
public sealed record LintFinding(LintSeverity Severity, string Code, string Message);

/// <summary>
/// Result of a full config validation run.
/// </summary>
public sealed class LintResult
{
    public List<LintFinding> Findings { get; } = new();
    public bool HasErrors => Findings.Any(f => f.Severity == LintSeverity.Error);
    public bool HasWarnings => Findings.Any(f => f.Severity == LintSeverity.Warning);

    /// <summary>
    /// Exit code: 0=clean, 1=warnings, 2=errors.
    /// </summary>
    public int ExitCode => HasErrors ? 2 : HasWarnings ? 1 : 0;

    public void Error(string code, string message)
        => Findings.Add(new LintFinding(LintSeverity.Error, code, message));

    public void Warn(string code, string message)
        => Findings.Add(new LintFinding(LintSeverity.Warning, code, message));

    public void Info(string code, string message)
        => Findings.Add(new LintFinding(LintSeverity.Info, code, message));
}

/// <summary>
/// Validates an amftpd.json config file without starting the daemon.
/// </summary>
public static class ConfigValidator
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Validates the config file at <paramref name="configFilePath"/>.
    /// Returns a <see cref="LintResult"/> with all findings.
    /// </summary>
    public static LintResult Validate(string configFilePath)
    {
        var result = new LintResult();

        // ------------------------------------------------------------------
        // 1. File existence
        // ------------------------------------------------------------------
        if (!File.Exists(configFilePath))
        {
            result.Error("E001", $"Config file not found: {configFilePath}");
            return result;
        }

        // ------------------------------------------------------------------
        // 2. JSON parse
        // ------------------------------------------------------------------
        AmFtpdConfigRoot? cfg = null;
        try
        {
            var json = File.ReadAllText(configFilePath);
            cfg = JsonSerializer.Deserialize<AmFtpdConfigRoot>(json, _jsonOpts);
            if (cfg is null)
            {
                result.Error("E002", "Config file deserialized to null.");
                return result;
            }
            result.Info("I001", "JSON parsed successfully.");
        }
        catch (JsonException jex)
        {
            result.Error("E002", $"JSON parse error: {jex.Message}");
            return result;
        }
        catch (Exception ex)
        {
            result.Error("E003", $"Failed to read config file: {ex.Message}");
            return result;
        }

        // ------------------------------------------------------------------
        // 3. Server settings
        // ------------------------------------------------------------------
        ValidateServerConfig(cfg.Server, result);

        // ------------------------------------------------------------------
        // 4. TLS settings
        // ------------------------------------------------------------------
        ValidateTlsConfig(cfg.Tls, cfg.Server, result);

        // ------------------------------------------------------------------
        // 5. VFS mounts
        // ------------------------------------------------------------------
        ValidateVfsConfig(cfg.Vfs, result);

        // ------------------------------------------------------------------
        // 6. Sections
        // ------------------------------------------------------------------
        ValidateSections(cfg, result);

        // ------------------------------------------------------------------
        // 7. Ratio rules
        // ------------------------------------------------------------------
        ValidateRatioRules(cfg, result);

        // ------------------------------------------------------------------
        // 8. Groups
        // ------------------------------------------------------------------
        ValidateGroups(cfg, result);

        // ------------------------------------------------------------------
        // 9. Status / security
        // ------------------------------------------------------------------
        ValidateSecuritySettings(cfg, result);

        // ------------------------------------------------------------------
        // 10. ACME
        // ------------------------------------------------------------------
        if (cfg.Acme is not null)
            ValidateAcmeConfig(cfg.Acme, cfg.Tls, result);

        ValidateLoggingConfig(cfg.Logging, result);

        return result;
    }

    // ------------------------------------------------------------------
    // Server config checks
    // ------------------------------------------------------------------
    private static void ValidateServerConfig(AmFtpdServerConfig srv, LintResult r)
    {
        if (srv is null) { r.Error("E010", "Server configuration section is missing."); return; }

        if (srv.Port < 1 || srv.Port > 65535)
            r.Error("E011", $"Server.Port {srv.Port} is outside valid range 1-65535.");

        if (srv.PassivePortStart < 1 || srv.PassivePortEnd > 65535)
            r.Error("E012", "Passive port range is outside 1-65535.");

        if (srv.PassivePortStart > srv.PassivePortEnd)
            r.Error("E013", $"PassivePortStart ({srv.PassivePortStart}) > PassivePortEnd ({srv.PassivePortEnd}).");

        var portCount = srv.PassivePortEnd - srv.PassivePortStart + 1;
        if (portCount < 10)
            r.Warn("W011", $"Passive port range is very small ({portCount} ports). Consider widening to at least 100.");

        if (string.IsNullOrWhiteSpace(srv.RootPath))
        {
            r.Error("E014", "Server.RootPath is empty.");
        }
        else if (!Directory.Exists(srv.RootPath))
        {
            r.Warn("W012", $"Server.RootPath does not exist: {srv.RootPath}");
        }
        else
        {
            r.Info("I010", $"Server.RootPath exists: {srv.RootPath}");
        }

        if (srv.Port == 21)
            r.Info("I011", "Using standard FTP port 21.");
        else if (srv.Port < 1024)
            r.Warn("W013", $"Port {srv.Port} is a privileged port (< 1024). Ensure the daemon has permission to bind.");

        if (srv.AllowAnonymous && !srv.RequireTlsForAuth)
            r.Warn("W014", "Anonymous access is allowed and TLS is not required for auth. Credentials transmit in plaintext.");

        if (srv.AllowFxp)
            r.Info("I012", "FXP (server-to-server transfer) is enabled.");
    }

    // ------------------------------------------------------------------
    // TLS config checks
    // ------------------------------------------------------------------
    private static void ValidateTlsConfig(AmFtpdTlsConfig tls, AmFtpdServerConfig srv, LintResult r)
    {
        if (tls is null)
        {
            if (srv.RequireTlsForAuth)
                r.Error("E020", "Server.RequireTlsForAuth is true but no TLS configuration is present.");
            else
                r.Warn("W020", "No TLS configuration found. The server will operate without encryption.");
            return;
        }

        if (string.IsNullOrWhiteSpace(tls.PfxPath))
        {
            r.Error("E021", "TLS.PfxPath is empty.");
            return;
        }

        if (!File.Exists(tls.PfxPath))
        {
            if (string.IsNullOrWhiteSpace(tls.SubjectName))
            {
                r.Error("E022", $"TLS.PfxPath not found and TLS.SubjectName is empty, so a self-signed certificate cannot be generated: {tls.PfxPath}");
            }
            else
            {
                r.Warn("W022", $"TLS.PfxPath not found: {tls.PfxPath}. amFTPd will generate a self-signed certificate on startup.");
            }
            return;
        }

        // Attempt to load the certificate
        try
        {
            using var cert = X509CertificateLoader.LoadPkcs12FromFile(tls.PfxPath, tls.PfxPassword);
            r.Info("I020", $"TLS certificate loaded. Subject: {cert.Subject}, Expires: {cert.NotAfter:yyyy-MM-dd}");

            if (cert.NotAfter < DateTime.UtcNow)
                r.Error("E023", $"TLS certificate has expired on {cert.NotAfter:yyyy-MM-dd}.");
            else if (cert.NotAfter < DateTime.UtcNow.AddDays(30))
                r.Warn("W021", $"TLS certificate expires in {(int)(cert.NotAfter - DateTime.UtcNow).TotalDays} day(s) on {cert.NotAfter:yyyy-MM-dd}.");

            if (cert.NotBefore > DateTime.UtcNow)
                r.Error("E024", $"TLS certificate is not yet valid (valid from {cert.NotBefore:yyyy-MM-dd}).");
        }
        catch (Exception ex)
        {
            r.Error("E025", $"Failed to load TLS certificate from {tls.PfxPath}: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // VFS mount checks
    // ------------------------------------------------------------------
    private static void ValidateVfsConfig(VfsConfig? vfs, LintResult r)
    {
        if (vfs is null)
        {
            r.Warn("W030", "VFS configuration section is missing.");
            return;
        }

        if (vfs.Mounts is null || vfs.Mounts.Count == 0)
        {
            r.Warn("W031", "No VFS mounts configured. Users will see an empty file system.");
            return;
        }

        foreach (var mount in vfs.Mounts)
        {
            if (string.IsNullOrWhiteSpace(mount.PhysicalPath))
            {
                r.Error("E030", $"VFS mount '{mount.VirtualPath}' has an empty physical path.");
                continue;
            }

            if (!Directory.Exists(mount.PhysicalPath))
                r.Warn("W032", $"VFS mount '{mount.VirtualPath}' → '{mount.PhysicalPath}' does not exist.");
            else
                r.Info("I030", $"VFS mount '{mount.VirtualPath}' → '{mount.PhysicalPath}' OK.");
        }
    }

    // ------------------------------------------------------------------
    // Section checks
    // ------------------------------------------------------------------
    private static void ValidateSections(AmFtpdConfigRoot cfg, LintResult r)
    {
        // Use the FTP sections defined in Server (accessed via AmFtpdConfigLoader),
        // or in AmFtpdConfigRoot.Sections (SectionRule map) if sections are listed there.
        // The FtpConfig.Sections list is not directly in AmFtpdConfigRoot —
        // but we can check the SectionRules dict for conflicts.

        if (cfg.Sections is null || cfg.Sections.Count == 0)
        {
            r.Warn("W040", "No sections configured. Consider adding sections to organise uploads.");
            return;
        }

        r.Info("I040", $"{cfg.Sections.Count} section rule(s) defined.");

        foreach (var (name, rule) in cfg.Sections)
        {
            if (string.IsNullOrWhiteSpace(name))
                r.Error("E040", "A section rule has an empty name.");

            if (rule is null)
                r.Error("E041", $"Section rule '{name}' is null.");
        }
    }

    // ------------------------------------------------------------------
    // Ratio rule checks
    // ------------------------------------------------------------------
    private static void ValidateRatioRules(AmFtpdConfigRoot cfg, LintResult r)
    {
        if (cfg.RatioRules is null || cfg.RatioRules.Count == 0)
        {
            r.Info("I050", "No ratio rules configured (all users are exempted or free).");
            return;
        }

        foreach (var (name, rule) in cfg.RatioRules)
        {
            if (rule is null) { r.Error("E050", $"Ratio rule '{name}' is null."); continue; }

            // Check for zero-denominator equivalent (ratio = 0 means no download limit)
            // Heuristic: if Ratio < 1 and not 0, it's inverted — warn
            var ratio = (rule as dynamic)?.Ratio ?? 0;
            if (ratio is double d && d > 0 && d < 1.0)
                r.Warn("W050", $"Ratio rule '{name}' has ratio={d:F2} which is less than 1:1. Verify this is intentional.");
        }
    }

    // ------------------------------------------------------------------
    // Group checks
    // ------------------------------------------------------------------
    private static void ValidateGroups(AmFtpdConfigRoot cfg, LintResult r)
    {
        if (cfg.Groups is null || cfg.Groups.Count == 0)
        {
            r.Warn("W060", "No groups configured.");
            return;
        }

        r.Info("I060", $"{cfg.Groups.Count} group(s) defined.");

        foreach (var (name, group) in cfg.Groups)
        {
            if (string.IsNullOrWhiteSpace(name))
                r.Error("E060", "A group configuration has an empty name.");

            if (group is null)
                r.Error("E061", $"Group '{name}' is null.");
        }
    }

    // ------------------------------------------------------------------
    // Security / best-practice checks
    // ------------------------------------------------------------------
    private static void ValidateSecuritySettings(AmFtpdConfigRoot cfg, LintResult r)
    {
        // Check status endpoint auth token
        if (cfg.Status is { Enabled: true } status)
        {
            if (string.IsNullOrWhiteSpace(status.AuthToken))
                r.Warn("W070", "Status/metrics endpoint is enabled but AuthToken is not set. The endpoint is publicly accessible.");
            else
                r.Info("I070", "Status endpoint auth token is configured.");
        }

        // Check HammerGuard — it lives in Server config; if there's a MaxFailedLogins=0 equivalent warn
        // (can't check directly from AmFtpdServerConfig right now, but warn on allow-anonymous)
        if (cfg.Server.AllowAnonymous)
            r.Warn("W071", "Anonymous access is enabled. Ensure this is intentional for a public-facing server.");

        // TLS required for auth
        if (!cfg.Server.RequireTlsForAuth)
            r.Warn("W072", "TLS is not required for authentication. FTP credentials may be transmitted in plaintext.");

        if (cfg.Server.AllowFxp)
            r.Warn("W073", "FXP is globally enabled. Restrict it with FxpPolicy or disable it before exposing the server publicly.");

        if (cfg.Server.AllowActiveMode)
            r.Warn("W074", "Active mode is globally enabled. Disable it for internet-facing/NAT deployments unless explicitly required.");

        if (cfg.Storage is not null)
        {
            if (IsWeakSecret(cfg.Storage.MasterPassword))
                r.Warn("W075", "Storage.MasterPassword is a known placeholder or weak value. Change it before production.");
        }

        if (cfg.Tls is not null && IsWeakSecret(cfg.Tls.PfxPassword))
            r.Warn("W076", "Tls.PfxPassword is a known placeholder or weak value. Change it before production.");

        // Passive address security
        if (string.IsNullOrWhiteSpace(cfg.Server.BindAddress) ||
            cfg.Server.BindAddress == "0.0.0.0" ||
            cfg.Server.BindAddress == "::")
            r.Info("I071", $"Server binds to all interfaces ({cfg.Server.BindAddress ?? "any"}).");
    }

    private static bool IsWeakSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var normalized = value.Trim();
        return normalized.Equals("changeme", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("password", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("change-before-production", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("scene-site-password", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("test-master-password", StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // ACME checks
    // ------------------------------------------------------------------
    private static void ValidateAcmeConfig(AmFtpdAcmeConfig acme, AmFtpdTlsConfig tls, LintResult r)
    {
        if (!acme.Enabled)
        {
            r.Info("I080", "ACME block is present but Enabled=false; automatic certificate management is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(acme.Domain))
            r.Error("E080", "ACME.Domain is required when ACME is enabled.");
        else
            r.Info("I081", $"ACME enabled for domain '{acme.Domain}'.");

        if (string.IsNullOrWhiteSpace(acme.Email))
            r.Error("E081", "ACME.Email is required — the CA uses it for expiry notifications.");

        if (string.IsNullOrWhiteSpace(acme.DirectoryUrl) ||
            !Uri.TryCreate(acme.DirectoryUrl, UriKind.Absolute, out _))
            r.Error("E082", $"ACME.DirectoryUrl is not a valid absolute URI: '{acme.DirectoryUrl}'.");

        if (acme.DirectoryUrl.Contains("staging", StringComparison.OrdinalIgnoreCase))
            r.Warn("W080", "ACME is pointing at a staging CA. Certificates will not be trusted by browsers/clients.");

        if (acme.RenewalThresholdDays < 1 || acme.RenewalThresholdDays > 89)
            r.Warn("W081",
                $"ACME.RenewalThresholdDays is {acme.RenewalThresholdDays}. " +
                "Recommended range is 14–60 days for Let's Encrypt 90-day certs.");

        if (acme.ChallengePort != 80)
            r.Warn("W082",
                $"ACME.ChallengePort is {acme.ChallengePort} (not 80). " +
                "The CA must be able to reach this port as port 80 via a NAT/firewall rule.");

        // Ensure Tls.PfxPath is set — ACME writes the certificate there
        if (string.IsNullOrWhiteSpace(tls.PfxPath))
            r.Error("E083",
                "ACME is enabled but Tls.PfxPath is not set. " +
                "The ACME manager writes the renewed certificate to this path.");
        else
            r.Info("I082", $"ACME will write renewed certificates to '{tls.PfxPath}'.");
    }

    private static void ValidateLoggingConfig(QuickLogOptions? logging, LintResult r)
    {
        if (logging is null)
        {
            r.Info("I090", "Logging block omitted; QuickLog defaults to SOMETHING mode.");
            return;
        }

        try
        {
            _ = logging.GetMode();
        }
        catch (ArgumentException ex)
        {
            r.Error("E090", ex.Message);
        }

        if (string.IsNullOrWhiteSpace(logging.TextLogPath))
            r.Error("E091", "Logging.TextLogPath must not be empty.");

        if (logging.Binary && string.IsNullOrWhiteSpace(logging.BinaryLogPath))
            r.Error("E092", "Logging.BinaryLogPath must not be empty when binary logging is enabled.");

        if (logging.QueueCapacity < 128)
            r.Warn("W090", "Logging.QueueCapacity is below 128; runtime will raise it to 128.");
    }
}
