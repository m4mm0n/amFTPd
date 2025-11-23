/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-23
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

using amFTPd.Config.Daemon;
using amFTPd.Config.Ftpd;
using amFTPd.Config.Ftpd.RatioRules;
using amFTPd.Core.Access;
using amFTPd.Core.Race;
using amFTPd.Core.Ratio;
using amFTPd.Credits;
using amFTPd.Db;
using amFTPd.Logging;
using amFTPd.Scripting;
using amFTPd.Security;
using System.Collections.Immutable;
using System.Diagnostics;
using FtpSection = amFTPd.Config.Ftpd.FtpSection;

namespace amFTPd.Core;

/// <summary>
/// Routes and handles FTP commands received from a client session.
/// </summary>
/// <remarks>This class processes FTP commands by interpreting the input command string,  executing the
/// corresponding operation, and sending appropriate responses  back to the client. It supports a wide range of FTP
/// commands, including  authentication, file system navigation, file transfers, and session management.  The <see
/// cref="FtpCommandRouter"/> relies on injected dependencies such as  session state, logging, file system access, and
/// configuration to perform its operations.</remarks>
internal sealed partial class FtpCommandRouter
{
    private const int DataBufferSize = 64 * 1024;
    private readonly FtpSession _s;
    private readonly IFtpLogger _log;
    private readonly FtpFileSystem _fs;
    private readonly FtpConfig _cfg;
    private readonly TlsConfig _tls;

    private readonly SectionManager _sections;

    private readonly CreditEngine _credits;
    private readonly IUserStore _users;
    private readonly IGroupStore _groups;

    private bool _isFxp;

    private AMScriptEngine? _creditScript;
    private AMScriptEngine? _fxpScript;
    private AMScriptEngine? _activeScript;
    private AMScriptEngine? _sectionRoutingScript;
    private AMScriptEngine? _siteScript;
    private AMScriptEngine? _userScript;
    private AMScriptEngine? _groupScript;

    private readonly DirectoryAccessEvaluator _directoryAccess;
    private readonly Dictionary<string, DirectoryRule> _directoryRules;

    private readonly RaceEngine _raceEngine;

    /// <summary>
    /// Gets the ratio calculation engine used to perform ratio-based computations.
    /// </summary>
    public RatioEngine RatioEngine { get; }
    /// <summary>
    /// Gets the ratio pipeline used to determine ratio adjustments.
    /// </summary>
    public RatioResolutionPipeline RatioPipeline { get; }
    /// <summary>
    /// Gets the rule engine used to evaluate directory-based rules.
    /// </summary>
    public DirectoryRuleEngine DirectoryRuleEngine { get; }

    /// <summary>
    /// Initializes a new instance of the FtpCommandRouter class, configuring FTP session routing, logging, file system
    /// access, and runtime behavior.
    /// </summary>
    /// <remarks>All dependencies must be fully initialized before calling this constructor. This class relies
    /// on the provided runtime configuration to enable advanced features such as ratio enforcement and directory rule
    /// evaluation.</remarks>
    /// <param name="s">The FTP session context used to route and process FTP commands.</param>
    /// <param name="log">The logger instance for recording FTP command activity and errors.</param>
    /// <param name="fs">The file system interface for managing file and directory operations within the FTP session.</param>
    /// <param name="cfg">The FTP server configuration settings that control command routing and behavior.</param>
    /// <param name="tls">The TLS configuration used to manage secure connections and encryption for FTP commands.</param>
    /// <param name="sections">The section manager responsible for handling configuration sections and related logic.</param>
    /// <param name="runtime">The runtime configuration providing engines and pipelines for ratio and directory rule processing.</param>
    public FtpCommandRouter(
        FtpSession s,
        IFtpLogger log,
        FtpFileSystem fs,
        FtpConfig cfg,
        TlsConfig tls,
        SectionManager sections,
        AmFtpdRuntimeConfig runtime)
    {
        _s = s;
        _log = log;
        _fs = fs;
        _cfg = cfg;
        _tls = tls;
        _sections = sections;

        RatioEngine = runtime.RatioEngine;
        RatioPipeline = runtime.RatioPipeline;
        DirectoryRuleEngine = runtime.DirectoryRuleEngine;

        _directoryAccess = new DirectoryAccessEvaluator(runtime.DirectoryRules);
        _directoryRules = runtime.DirectoryRules;

        _raceEngine = runtime.RaceEngine;
    }

    /// <summary>
    /// Handles an FTP command by parsing the input line, identifying the command, and executing the corresponding
    /// operation.
    /// </summary>
    /// <remarks>This method processes a wide range of FTP commands, such as authentication, file system
    /// operations, and data transfer commands. The command is extracted from the input line, converted to uppercase for
    /// case-insensitive matching, and dispatched to the appropriate handler. If the command is unrecognized, a default
    /// response indicating an unknown command is sent to the client. <para> The method ensures that the session is kept
    /// alive and logs the command for debugging purposes. Certain commands may alter the session state, such as marking
    /// the session as quit after processing a "QUIT" command. </para></remarks>
    /// <param name="line">The raw command line received from the client, including the command and optional arguments.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to cancel the operation.</param>
    /// <returns></returns>
    public async Task HandleAsync(string line, CancellationToken ct)
    {
        var sp = line.Split(' ', 2, StringSplitOptions.TrimEntries);
        var cmd = sp[0].ToUpperInvariant();
        var arg = sp.Length > 1 ? sp[1] : string.Empty;

        _s.Touch();
        _log.Log(FtpLogLevel.Debug, $"CMD: {cmd} ARG: {arg}");

        // ------------------------------------------------------------------
        // Unauthenticated command whitelist (central)
        // ------------------------------------------------------------------
        if (_s.Account is null &&
            !FtpAuthorization.IsCommandAllowedUnauthenticated(cmd))
        {
            await _s.WriteAsync("530 Please login with USER and PASS.\r\n", ct);
            return;
        }

        // ------------------------------------------------------------------
        // AMSCRIPT GROUP RULES (per-command)
        // ------------------------------------------------------------------
        if (_groupScript is not null &&
            _s.Account is not null &&
            !string.IsNullOrWhiteSpace(_s.Account.GroupName))
        {
            var gctx = BuildUserContext(cmd, arg);
            var gRes = _groupScript.EvaluateGroup(gctx);

            if (gRes.Action == AMRuleAction.Deny)
            {
                var msg = gRes.DenyReason ?? "550 Command denied by group policy.\r\n";
                if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                    msg += "\r\n";

                await _s.WriteAsync(msg, ct);
                return; // skip actual command handling
            }
        }

        // ------------------------------------------------------------------
        // Static per-user permission checks (FtpUser flags)
        // ------------------------------------------------------------------
        if (_s.Account is not null &&
            !FtpAuthorization.IsCommandAllowedForUser(_s.Account, cmd, arg))
        {
            await _s.WriteAsync("550 Permission denied.\r\n", ct);
            return;
        }

        // ------------------------------------------------------------------
        // Normal command handling
        // ------------------------------------------------------------------
        switch (cmd)
        {
            // Auth / TLS
            case "USER": await USER(arg, ct); break;
            case "PASS": await PASS(arg, ct); break;
            case "AUTH": await AUTH(arg, ct); break;
            case "PBSZ": await _s.WriteAsync(FtpResponses.PbszOk, ct); break;
            case "PROT": await PROT(arg, ct); break;

            // Info / meta
            case "FEAT": await FEAT(ct); break;
            case "SYST": await _s.WriteAsync("215 UNIX Type: L8\r\n", ct); break;
            case "OPTS": await OPTS(arg, ct); break;
            case "NOOP": await _s.WriteAsync(FtpResponses.Ok, ct); break;
            case "QUIT": await _s.WriteAsync(FtpResponses.Bye, ct); _s.MarkQuit(); break;
            case "HELP": await HELP(arg, ct); break;
            case "VERSION": await VERSION(ct); break;
            case "STAT": await STAT(arg, ct); break;
            case "ALLO": await _s.WriteAsync("202 ALLO command ignored.\r\n", ct); break;
            case "MODE": await _s.WriteAsync("200 Mode set to S.\r\n", ct); break;
            case "STRU": await _s.WriteAsync("200 Structure set to F.\r\n", ct); break;
            case "ABOR": await _s.WriteAsync("226 Abort OK.\r\n", ct); break;

            // Path / navigation
            case "PWD": await _s.WriteAsync($"257 \"{_s.Cwd}\" is the current directory.\r\n", ct); break;
            case "CWD": await CWD(arg, ct); break;
            case "CDUP": await CDUP(ct); break;

            // Transfer parameters
            case "TYPE": await TYPE(arg, ct); break;
            case "PASV": await PASV(ct); break;
            case "EPSV": await EPSV(ct); break;
            case "PORT": await PORT(arg, ct); break;
            case "EPRT": await EPRT(arg, ct); break;

            // Listing and transfer
            case "LIST": await LIST(arg, ct); break;
            case "NLST": await NLST(arg, ct); break;
            case "MLSD": await MLSD(arg, ct); break;
            case "MLST": await MLST(arg, ct); break;
            case "RETR": await RETR(arg, ct); break;
            case "STOR": await STOR(arg, ct); break;
            case "APPE": await APPE(arg, ct); break;
            case "REST": await REST(arg, ct); break;
            case "SIZE": await SIZE(arg, ct); break;
            case "MDTM": await MDTM(arg, ct); break;

            // File system ops
            case "DELE": await DELE(arg, ct); break;
            case "MKD": await MKD(arg, ct); break;
            case "RMD": await RMD(arg, ct); break;
            case "RNFR": _s.RenameFrom = arg; await _s.WriteAsync("350 Ready for RNTO.\r\n", ct); break;
            case "RNTO": await RNTO(arg, ct); break;

            // SITE command
            case "SITE": await SITE(arg, ct); break;

            default:
                await _s.WriteAsync(FtpResponses.UnknownCmd, ct);
                break;
        }
    }
    /// <summary>
    /// Attaches the specified script engines to their respective roles within the system.
    /// </summary>
    /// <remarks>This method assigns the provided script engines to their respective roles. Any parameter set
    /// to <see langword="null"/> indicates that the corresponding role will not have an associated script
    /// engine.</remarks>
    /// <param name="credit">The script engine responsible for handling credit-related operations. Can be <see langword="null"/> if not
    /// applicable.</param>
    /// <param name="fxp">The script engine responsible for handling FXP-related operations. Can be <see langword="null"/> if not
    /// applicable.</param>
    /// <param name="active">The script engine responsible for managing active operations. Cannot be <see langword="null"/>.</param>
    /// <param name="sectionRouting">The script engine responsible for section routing operations. Can be <see langword="null"/> if not applicable.</param>
    /// <param name="site">The script engine responsible for site-related operations. Can be <see langword="null"/> if not applicable.</param>
    /// <param name="users">The script engine responsible for user-related operations. Can be <see langword="null"/> if not applicable.</param>
    /// <param name="groups">The script engine responsible for group-related operations. Can be <see langword="null"/> if not applicable.</param>
    public void AttachScriptEngines(
        AMScriptEngine? credit,
        AMScriptEngine? fxp,
        AMScriptEngine? active,
        AMScriptEngine? sectionRouting = null,
        AMScriptEngine? site = null,
        AMScriptEngine? users = null,
        AMScriptEngine? groups = null)
    {
        _creditScript = credit;
        _fxpScript = fxp;
        _activeScript = active;
        _sectionRoutingScript = sectionRouting;
        _siteScript = site;
        _userScript = users;
        _groupScript = groups;
    }

    private FtpSection GetSectionForVirtual(string virtPath)
    {
        // Normal routing first
        var section = _sections.GetSectionForPath(virtPath);

        // No script? return original selection
        if (_sectionRoutingScript is null)
            return section;

        // physical path may fail → ignore
        string physPath;
        try
        {
            physPath = _fs.MapToPhysical(virtPath);
        }
        catch
        {
            physPath = "";
        }

        var ctx = BuildSectionRoutingContext(virtPath, physPath, section);

        var result = _sectionRoutingScript.EvaluateDownload(ctx);

        // Check for: return section "NAME"
        if (result.Message is string msg &&
            msg.StartsWith("SECTION_OVERRIDE::", StringComparison.Ordinal))
        {
            var secName = msg["SECTION_OVERRIDE::".Length..];

            var overrideSection = _sections
                .GetSections()
                .FirstOrDefault(s =>
                    s.Name.Equals(secName, StringComparison.OrdinalIgnoreCase));

            if (overrideSection != null)
                return overrideSection;
        }

        return section;
    }

    private static async Task<long> CopyWithThrottleAsync(
        Stream input,
        Stream output,
        int maxKbps,
        CancellationToken ct)
    {
        var buffer = new byte[DataBufferSize];
        long total = 0;

        // No limit? Just copy
        if (maxKbps <= 0)
        {
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
            }

            await output.FlushAsync(ct);
            return total;
        }

        // Throttled copy
        var maxBytesPerSecond = maxKbps * 1024L;
        long bytesThisWindow = 0;
        var sw = Stopwatch.StartNew();

        int r;
        while ((r = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, r), ct);
            total += r;
            bytesThisWindow += r;

            var elapsedMs = sw.ElapsedMilliseconds;

            if (elapsedMs >= 1000)
            {
                // New window
                sw.Restart();
                bytesThisWindow = 0;
            }
            else if (bytesThisWindow > maxBytesPerSecond)
            {
                // We’ve exceeded the budget for this second
                var remainingMs = 1000 - (int)elapsedMs;
                if (remainingMs > 0)
                    await Task.Delay(remainingMs, ct);

                sw.Restart();
                bytesThisWindow = 0;
            }
        }

        await output.FlushAsync(ct);
        return total;
    }

    private async Task<bool> CheckDownloadCreditsAsync(FtpSection section, long bytes, CancellationToken ct)
    {
        var account = _s.Account;
        if (account is null) return true;
        if (section.FreeLeech) return true;

        var kb = bytes / 1024;
        if (kb <= 0) return true;

        var cost = kb;

        // Apply section ratio first (your original logic)
        if (section.RatioUploadUnit > 0 && section.RatioDownloadUnit > 0)
        {
            var factor = (double)section.RatioDownloadUnit / section.RatioUploadUnit;
            cost = (long)Math.Round(kb * factor);
        }

        // AMScript: credits.msl
        if (_creditScript is not null)
        {
            var ctx = BuildCreditContext(section, bytes) with { CostDownload = cost };
            var result = _creditScript.EvaluateDownload(ctx);

            if (result.Action == AMRuleAction.Deny)
            {
                await _s.WriteAsync("550 Download denied by policy.\r\n", ct);
                return false;
            }

            if (result.Action == AMRuleAction.Allow)
                return true;

            cost = result.CostDownload;
        }

        if (account.CreditsKb < cost)
        {
            await _s.WriteAsync("550 Not enough credits for download.\r\n", ct);
            return false;
        }

        return true;
    }

    private void ApplyDownloadCredits(FtpSection section, long bytes)
    {
        var account = _s.Account;
        if (account is null) return;
        if (section.FreeLeech) return;

        var kb = bytes / 1024;
        if (kb <= 0) return;

        var cost = kb;

        if (section.RatioUploadUnit > 0 && section.RatioDownloadUnit > 0)
        {
            var factor = (double)section.RatioDownloadUnit / section.RatioUploadUnit;
            cost = (long)Math.Round(kb * factor);
        }

        if (_creditScript is not null)
        {
            var ctx = BuildCreditContext(section, bytes) with { CostDownload = cost };
            var result = _creditScript.EvaluateDownload(ctx);

            if (result.Action == AMRuleAction.Deny)
                return; // rule says "don't charge" or "block" silently here

            cost = result.CostDownload;
        }

        var updated = account with { CreditsKb = Math.Max(0, account.CreditsKb - cost) };
        if (_s.Users.TryUpdateUser(updated, out _))
            _s.SetAccount(updated);
    }

    private void ApplyUploadCredits(FtpSection section, long bytes)
    {
        var account = _s.Account;
        if (account is null) return;
        if (bytes <= 0) return;

        var kb = bytes / 1024;
        if (kb <= 0) return;

        long earned;

        if (!section.FreeLeech &&
            section.RatioUploadUnit > 0 &&
            section.RatioDownloadUnit > 0)
        {
            var factor = (double)section.RatioDownloadUnit / section.RatioUploadUnit;
            earned = (long)Math.Round(kb * factor);
        }
        else
        {
            earned = kb;
        }

        if (_creditScript is not null)
        {
            var ctx = BuildCreditContext(section, bytes) with { EarnedUpload = earned };
            var result = _creditScript.EvaluateUpload(ctx);

            if (result.Action == AMRuleAction.Deny)
                return; // no credits awarded

            earned = result.EarnedUpload;
        }

        var updated = account with { CreditsKb = account.CreditsKb + earned };
        if (_s.Users.TryUpdateUser(updated, out _))
            _s.SetAccount(updated);
    }

    private static FtpUser CreatePseudoUser() =>
        new(
            UserName: "UNKNOWN",
            PasswordHash: "",
            HomeDir: "/",
            IsAdmin: false,
            AllowFxp: false,
            AllowUpload: false,
            AllowDownload: false,
            AllowActiveMode: false,
            MaxConcurrentLogins: 0,
            IdleTimeout: TimeSpan.FromMinutes(5),
            MaxUploadKbps: 0,
            MaxDownloadKbps: 0,
            PrimaryGroup: "unknown",
            SecondaryGroups: ImmutableArray<string>.Empty,
            CreditsKb: 0,
            AllowedIpMask: null,
            RequireIdentMatch: false,
            RequiredIdent: null,
            FlagsRaw: string.Empty
        );
}