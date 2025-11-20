using amFTPd.Config.Ftpd;
using amFTPd.Credits;
using amFTPd.Db;
using amFTPd.Logging;
using amFTPd.Scripting;
using amFTPd.Security;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="FtpCommandRouter"/> class, which is responsible for routing and
    /// handling FTP commands within a session.
    /// </summary>
    /// <param name="s">The current FTP session context, which manages the state and communication for the session.</param>
    /// <param name="log">The logger instance used to record FTP activity and diagnostics.</param>
    /// <param name="fs">The file system abstraction used to interact with files and directories on the server.</param>
    /// <param name="cfg">The FTP server configuration settings that influence command behavior.</param>
    /// <param name="tls">The TLS configuration used to manage secure communication for the session.</param>
    /// <param name="sections">The section manager responsible for handling segmented or partitioned server resources.</param>
    public FtpCommandRouter(
        FtpSession s,
        IFtpLogger log,
        FtpFileSystem fs,
        FtpConfig cfg,
        TlsConfig tls,
        SectionManager sections)
    {
        _s = s;
        _log = log;
        _fs = fs;
        _cfg = cfg;
        _tls = tls;
        _sections = sections;
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
            case "RETR": await RETR(arg, ct); break;
            case "STOR": await STOR(arg, ct); break;
            case "APPE": await APPE(arg, ct); break;
            case "REST": await REST(arg, ct); break;

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

        var ctx = new AMScriptContext(
            IsFxp: _isFxp,
            Section: section.Name,
            FreeLeech: section.FreeLeech,
            UserName: _s.Account?.UserName ?? "",
            UserGroup: _s.Account?.GroupName ?? "",
            Bytes: 0,
            Kb: 0,
            CostDownload: 0,
            EarnedUpload: 0,
            VirtualPath: virtPath,
            PhysicalPath: physPath
        );

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

    private static bool IsIpAllowed(IPAddress addr, string mask)
    {
        // CIDR: 1.2.3.0/24
        if (mask.Contains('/'))
            return IsIpInCidr(addr, mask);

        // Wildcard v4: 1.2.3.* or 10.*.*.*
        if (mask.Contains('*'))
        {
            if (addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return false;

            var addrParts = addr.ToString().Split('.');
            var maskParts = mask.Split('.');

            if (addrParts.Length != 4 || maskParts.Length != 4)
                return false;

            for (var i = 0; i < 4; i++)
            {
                if (maskParts[i] == "*")
                    continue;
                if (!string.Equals(maskParts[i], addrParts[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        // Exact match
        return IPAddress.TryParse(mask, out var exact) && addr.Equals(exact);
    }

    private static bool IsIpInCidr(IPAddress addr, string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2)
            return false;

        if (!IPAddress.TryParse(parts[0], out var network))
            return false;

        if (!int.TryParse(parts[1], out var prefixLength))
            return false;

        var addrBytes = addr.GetAddressBytes();
        var netBytes = network.GetAddressBytes();

        if (addrBytes.Length != netBytes.Length)
            return false;

        var bits = prefixLength;
        for (var i = 0; i < addrBytes.Length && bits > 0; i++)
        {
            int mask;
            if (bits >= 8)
            {
                mask = 0xFF;
                bits -= 8;
            }
            else
            {
                mask = 0xFF << (8 - bits);
                bits = 0;
            }

            if ((addrBytes[i] & mask) != (netBytes[i] & mask))
                return false;
        }

        return true;
    }

    private static FtpUser CreatePseudoUser()
    {
        return new FtpUser(
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
            GroupName: null,
            CreditsKb: 0,
            AllowedIpMask: null,
            RequireIdentMatch: false,
            RequiredIdent: null
        );
    }

    private static async Task<string?> QueryIdentAsync(
        IPEndPoint localEp,
        IPEndPoint remoteEp,
        CancellationToken ct)
    {
        // RFC 1413: connect to remote port 113
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(3));

        using var client = new TcpClient();

        try
        {
            await client.ConnectAsync(remoteEp.Address, 113, cts.Token);
        }
        catch
        {
            // ident service not reachable
            return null;
        }

        await using var stream = client.GetStream();
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true);
        using var reader = new StreamReader(stream, Encoding.ASCII, false, leaveOpen: true);

        // Request: "server-port , client-port\r\n"
        // server-port = our local port; client-port = their remote port
        var query = $"{localEp.Port} , {remoteEp.Port}\r\n";
        await writer.WriteAsync(query);
        await writer.FlushAsync(cts.Token);

        var line = await reader.ReadLineAsync(cts.Token);
        if (string.IsNullOrEmpty(line))
            return null;

        // Expected: "<local> , <remote> : USERID : <OS> : <USER>"
        var parts = line.Split(':');
        if (parts.Length < 4)
            return null;

        var userPart = parts[3].Trim();
        return string.IsNullOrWhiteSpace(userPart) ? null : userPart;
    }
    private string ResolveSectionFromPath(string fullPath)
    {
        // Normalize
        fullPath = fullPath.Replace('\\', '/').TrimEnd('/');

        foreach (var s in _sections.GetSections())
        {
            // Sections define virtual roots like "/mp3", "/x264", "/0day"
            // If the path starts with that virtual root, we consider it in that section.
            if (fullPath.StartsWith(s.VirtualRoot, StringComparison.OrdinalIgnoreCase))
                return s.Name;
        }

        // fallback to default if unknown
        return "default";
    }
}