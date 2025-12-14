/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpCommandRouter.Commands.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-14 20:44:23
 *  CRC32:          0x0EDEAC80
 *  
 *  Description:
 *      Partial class for handling FTP commands within the FtpCommandRouter.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


using amFTPd.Core.Events;
using amFTPd.Core.Fxp;
using amFTPd.Core.Ident;
using amFTPd.Core.Site;
using amFTPd.Core.Vfs;
using amFTPd.Core.Zipscript;
using amFTPd.Logging;
using amFTPd.Scripting;
using amFTPd.Security;
using System.Net;
using System.Reflection;
using System.Text;

namespace amFTPd.Core
{
    /// <summary>
    /// Partial class for handling FTP commands within the FtpCommandRouter.
    /// </summary>
    public sealed partial class FtpCommandRouter
    {
        #region AMScript Context Builders
        private AMScriptContext BuildCreditContext(Config.Ftpd.FtpSection section, long bytes)
        {
            var account = _s.Account!;
            var kb = Math.Max(1L, bytes / 1024L);

            return new AMScriptContext(
                IsFxp: _isFxp,
                Section: section.Name,
                FreeLeech: section.FreeLeech,
                UserName: account.UserName,
                UserGroup: account.GroupName ?? string.Empty,
                Bytes: bytes,
                Kb: kb,
                CostDownload: kb,   // default cost = 1:1
                EarnedUpload: kb    // default earn = 1:1
            );
        }

        private AMScriptContext BuildSimpleContextForFxpAndActive(string command)
        {
            var account = _s.Account;
            var userName = account?.UserName ?? string.Empty;
            var userGroup = account?.GroupName ?? string.Empty;

            var virt = _s.Cwd;
            string? phys;
            try
            {
                phys = _fs.MapToPhysical(virt);
            }
            catch
            {
                phys = string.Empty;
            }

            var section = _sections.GetSectionForPath(virt);

            return new AMScriptContext(
                IsFxp: _isFxp,
                Section: section.Name,
                FreeLeech: section.FreeLeech,
                UserName: userName,
                UserGroup: userGroup,
                Bytes: 0,
                Kb: 0,
                CostDownload: 0,
                EarnedUpload: 0,
                VirtualPath: virt,
                PhysicalPath: phys,
                Event: command.ToUpperInvariant()   // "PASV", "EPSV", "PORT", "EPRT"
            );
        }

        private AMScriptContext BuildSectionRoutingContext(string? virtualPath, string? physicalPath, Config.Ftpd.FtpSection section)
        {
            var account = _s.Account!;

            return new AMScriptContext(
                IsFxp: _isFxp,
                Section: section.Name,
                FreeLeech: section.FreeLeech,
                UserName: account.UserName,
                UserGroup: account.GroupName ?? "",
                Bytes: 0,
                Kb: 0,
                CostDownload: 0,
                EarnedUpload: 0,
                VirtualPath: virtualPath,
                PhysicalPath: physicalPath,
                Event: "ROUTE"
            );
        }

        private AMScriptContext BuildSiteContext(string command, string args)
        {
            var acc = _s.Account!;

            // Virtual working directory = Cwd
            var virtPath = _s.Cwd;

            // Resolve physical path (safe fallback)
            string? physicalPath;
            try
            {
                physicalPath = _fs.MapToPhysical(virtPath);
            }
            catch
            {
                physicalPath = string.Empty;
            }

            // Resolve section based on virtual path
            var section = _sections.GetSectionForPath(virtPath);

            return new AMScriptContext(
                IsFxp: _isFxp,
                Section: section.Name,
                FreeLeech: section.FreeLeech,
                UserName: acc.UserName,
                UserGroup: acc.GroupName ?? "",
                Bytes: 0,
                Kb: 0,
                CostDownload: 0,
                EarnedUpload: 0,
                VirtualPath: virtPath,
                PhysicalPath: physicalPath,
                Event: $"SITE {command.ToUpperInvariant()}"
            );
        }

        private AMScriptContext BuildUserContext(string? cmd = "", string? args = "")
        {
            var acc = _s.Account ?? CreatePseudoUser();

            var virt = _s.Cwd;

            string? phys;
            try
            {
                phys = _fs.MapToPhysical(virt);
            }
            catch
            {
                phys = string.Empty;
            }

            var section = _sections.GetSectionForPath(virt);
            var evt = string.IsNullOrWhiteSpace(cmd) ? string.Empty : cmd.ToUpperInvariant();

            return new AMScriptContext(
                IsFxp: _isFxp,
                Section: section.Name,
                FreeLeech: section.FreeLeech,
                UserName: acc.UserName,
                UserGroup: acc.GroupName ?? "",
                Bytes: 0,
                Kb: 0,
                CostDownload: 0,
                EarnedUpload: 0,
                VirtualPath: virt,
                PhysicalPath: phys,
                Event: evt
            );
        }
        #endregion

        // --- Auth / TLS ---
        #region Auth / TLS
        private async Task USER(string arg, CancellationToken ct)
        {
            if (_cfg.RequireTlsForAuth && !_s.TlsActive)
            {
                await _s.WriteAsync("534 Policy requires TLS before authentication.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            _s.PendingUser = arg;
            _s.ClearRestOffset();

            if (_cfg.AllowAnonymous && arg.Equals("anonymous", StringComparison.OrdinalIgnoreCase))
            {
                await _s.WriteAsync("331 Anonymous login ok, send your email as password.\r\n", ct);
            }
            else
            {
                await _s.WriteAsync(FtpResponses.NeedPassword, ct);
            }
        }

        private async Task PASS(string arg, CancellationToken ct)
        {
            if (_s.PendingUser is null)
            {
                await _s.WriteAsync("503 Login with USER first.\r\n", ct);
                return;
            }

            var username = _s.PendingUser;

            // ---------------------------------------------------------------------
            // 0. TLS requirement BEFORE we even bother verifying password
            // ---------------------------------------------------------------------
            if (_cfg.RequireTlsForAuth && !_s.TlsActive)
            {
                await _s.WriteAsync("530 TLS is required for authentication.\r\n", ct);
                return;
            }

            if (!_s.Users.TryAuthenticate(username, arg, out var account) || account is null)
            {
                // Track failed login attempts for hammer/flood logic
                _s.NotifyLoginFailed();

                if (_s.RemoteEndPoint?.Address is IPAddress ip)
                {
                    _server.NotifyFailedLogin(ip);
                }

                await _s.WriteAsync("530 Login incorrect.\r\n", ct);
                return;
            }

            // ---------------------------------------------------------------------
            // 1. Disabled account?
            // ---------------------------------------------------------------------
            if (account.Disabled)
            {
                await _s.WriteAsync("530 Account disabled.\r\n", ct);
                return;
            }

            // ---------------------------------------------------------------------
            // 2. Anonymous policy
            // ---------------------------------------------------------------------
            var isAnonymousUser = username.Equals("anonymous", StringComparison.OrdinalIgnoreCase);
            if (isAnonymousUser && !_cfg.AllowAnonymous)
            {
                await _s.WriteAsync("530 Anonymous logins are disabled.\r\n", ct);
                return;
            }

            // ==================================================================
            // GLOBAL IDENT POLICY (via IdentManager) - SAFE VERSION
            // ==================================================================
            try
            {
                if (_s.IdentManager is not null)
                {
                    var identResult = await _s.IdentManager.QueryAndApplyPolicyAsync(
                        _s.Control,
                        username,
                        clientCert: null,                 // no client cert support yet
                        isUserInGroup: _ => false,        // no session-group tracking yet
                        addUserToGroup: _ => { },         // no-op for now
                        logInfo: msg => _log.Log(FtpLogLevel.Info, msg),
                        logWarn: msg => _log.Log(FtpLogLevel.Warn, msg),
                        ct);

                    // If you want, you can stash this:
                    // _s.RemoteIdent = identResult.Username ?? _s.RemoteIdent;
                }
            }
            catch (IdentPolicyException ex)
            {
                _log.Log(FtpLogLevel.Warn, $"IDENT policy denied user '{username}': {ex.Message}");
                await _s.WriteAsync("530 Login denied by IDENT policy.\r\n", ct);
                return;
            }

            // ==================================================================
            // PER-USER IDENT ENFORCEMENT (your existing logic)
            // ==================================================================
            if ((account.RequireIdentMatch || !string.IsNullOrWhiteSpace(account.RequiredIdent)))
            {
                var identName = await _s.QueryIdentAsync(ct);

                if (identName is null)
                {
                    if (account.RequireIdentMatch)
                    {
                        await _s.WriteAsync("530 Login denied: IDENT required but not available.\r\n", ct);
                        return;
                    }
                    // else: RequireIdentMatch == false, RequiredIdent may still be set – you
                    // decided earlier to treat "no ident" as "not fatal, just log".
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(account.RequiredIdent) &&
                        !identName.Equals(account.RequiredIdent, StringComparison.OrdinalIgnoreCase))
                    {
                        await _s.WriteAsync("530 Login denied: IDENT mismatch.\r\n", ct);
                        return;
                    }
                }

                _log.Log(FtpLogLevel.Debug,
                    $"IDENT: user={account.UserName}, ident={identName ?? "<none>"}, required={account.RequiredIdent ?? "<none>"}");
            }

            // ==================================================================
            // AMSCRIPT GROUP RULES (login)
            // ==================================================================
            if (_groupScript is not null && !string.IsNullOrWhiteSpace(account.GroupName))
            {
                var gctx = new AMScriptContext(
                    IsFxp: false,
                    Section: "/",
                    FreeLeech: false,
                    UserName: account.UserName,
                    UserGroup: account.GroupName ?? "",
                    Bytes: 0,
                    Kb: 0,
                    CostDownload: 0,
                    EarnedUpload: 0,
                    VirtualPath: "/",
                    PhysicalPath: "",
                    Event: "LOGIN"
                );

                var gRule = _groupScript.EvaluateGroup(gctx);

                if (gRule.Action == AMRuleAction.Deny)
                {
                    var reason = gRule.DenyReason ?? "530 Login denied by group policy.";
                    await _s.WriteAsync(reason + "\r\n", ct);
                    return;
                }

                if (gRule.NewUploadLimit is int gul)
                    account = account with { MaxUploadKbps = gul };

                if (gRule.NewDownloadLimit is int gdl)
                    account = account with { MaxDownloadKbps = gdl };

                if (gRule.CreditDelta is long gcd)
                    account = account with { CreditsKb = account.CreditsKb + gcd };
            }

            // ==================================================================
            // AMSCRIPT USER RULES (login)
            // ==================================================================
            if (_userScript is not null)
            {
                var ctx = new AMScriptContext(
                    IsFxp: false,
                    Section: "/",
                    FreeLeech: false,
                    UserName: account.UserName,
                    UserGroup: account.GroupName ?? "",
                    Bytes: 0,
                    Kb: 0,
                    CostDownload: 0,
                    EarnedUpload: 0,
                    VirtualPath: "/",
                    PhysicalPath: "",
                    Event: "LOGIN"
                );

                var rule = _userScript.EvaluateDownload(ctx);

                if (rule.Action == AMRuleAction.Deny)
                {
                    var reason = rule.DenyReason ?? "530 Login denied by policy.";
                    await _s.WriteAsync(reason + "\r\n", ct);
                    return;
                }

                if (rule.NewUploadLimit is int ul)
                    account = account with { MaxUploadKbps = ul };

                if (rule.NewDownloadLimit is int dl)
                    account = account with { MaxDownloadKbps = dl };

                if (rule.CreditDelta is long cd)
                    account = account with { CreditsKb = account.CreditsKb + cd };
            }

            // ==================================================================
            // RATIO LOGIN RULES
            // ==================================================================
            if (_ratioEngine is not null)
            {
                // Remote endpoint may not always be an IPEndPoint (e.g. tests),
                // so we guard and fall back to empty string.
                var remoteIp = (_s.Control.Client.RemoteEndPoint as IPEndPoint)
                    ?.Address.ToString()
                    ?? string.Empty;

                // Treat classic anonymous logins specially.
                var isAnonymous =
                    _cfg.AllowAnonymous &&
                    username.Equals("anonymous", StringComparison.OrdinalIgnoreCase);

                var rctx = new RatioLoginContext
                {
                    UserName = account.UserName,
                    GroupName = account.GroupName,
                    RemoteAddress = remoteIp,
                    RemoteHost = _s.RemoteIdent,       // filled by IDENT, if available
                    IsAnonymous = isAnonymous,
                    NowUtc = DateTimeOffset.UtcNow.UtcDateTime,

                    // For anonymous, PASS usually carries an email address;
                    // pass it into the context so rules can key off it if desired.
                    RealName = isAnonymous ? arg : null
                };

                var rRule = _ratioEngine.ResolveLoginRule(rctx);

                if (rRule.Action == AMRuleAction.Deny)
                {
                    var reason = rRule.DenyReason ?? "530 Login denied by ratio policy.";
                    await _s.WriteAsync(reason + "\r\n", ct);
                    return;
                }

                if (rRule.NewUploadLimit is int rul)
                    account = account with { MaxUploadKbps = rul };

                if (rRule.NewDownloadLimit is int rdl)
                    account = account with { MaxDownloadKbps = rdl };

                if (rRule.CreditDelta is long rcd)
                    account = account with { CreditsKb = account.CreditsKb + rcd };
            }

            // ==================================================================
            // NORMAL LOGIN
            // ==================================================================
            _s.SetAccount(account);

            // EventBus: announce login
            _runtime.EventBus?.Publish(new FtpEvent
            {
                Type = FtpEventType.Login,
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = _s.SessionId,
                User = account.UserName,
                Group = account.GroupName
            });

            await _s.WriteAsync("230 Login successful.\r\n", ct);
        }

        private async Task AUTH(string arg, CancellationToken ct)
        {
            if (!_cfg.EnableExplicitTls)
            {
                await _s.WriteAsync("502 AUTH not enabled.\r\n", ct);
                return;
            }

            if (!arg.Equals("TLS", StringComparison.OrdinalIgnoreCase))
            {
                await _s.WriteAsync("504 AUTH only supports TLS.\r\n", ct);
                return;
            }

            if (_tls?.Certificate is null)
            {
                _log.Log(FtpLogLevel.Error, "AUTH TLS requested but no TLS certificate is configured.");
                await _s.WriteAsync("534 TLS not available.\r\n", ct);
                return;
            }

            // Tell client to start TLS handshake
            await _s.WriteAsync(FtpResponses.TlsReady, ct); // "234 AUTH TLS successful.\r\n"

            try
            {
                await _s.UpgradeToTlsAsync(_tls, ct);
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Error, $"AUTH TLS handshake failed: {ex}");

                // After 234 we must not send more plaintext; close the connection.
                await _s.DisposeAsync();
            }
        }

        private async Task PROT(string arg, CancellationToken ct)
        {
            var v = arg.Trim().ToUpperInvariant();

            if (v == "C")
            {
                _s.Protection = "C";
                await _s.WriteAsync("200 Protection level set to Clear.\r\n", ct);
            }
            else if (v == "P")
            {
                // Require TLS on control + a certificate
                if (!_cfg.EnableExplicitTls || !_s.TlsActive || _tls.Certificate is null)
                {
                    await _s.WriteAsync("534 PROT P not available.\r\n", ct);
                    return;
                }

                _s.Protection = "P";
                await _s.WriteAsync("200 Protection level set to Private.\r\n", ct);
            }
            else
            {
                await _s.WriteAsync("504 PROT only supports C or P.\r\n", ct);
            }
        }
        //private async Task PROT(string arg, CancellationToken ct)
        //{
        //    var v = arg.Trim().ToUpperInvariant();
        //    if (v is "C" or "P")
        //    {
        //        _s.Protection = v;
        //        await _s.WriteAsync(FtpResponses.ProtOk, ct);
        //    }
        //    else
        //    {
        //        await _s.WriteAsync("536 Only C or P supported.\r\n", ct);
        //    }
        //}

        private Task FEAT(CancellationToken ct)
            => _s.WriteAsync(
                "211-Features:\r\n" +
                " UTF8\r\n EPSV\r\n EPRT\r\n PASV\r\n PBSZ\r\n PROT\r\n AUTH TLS\r\n" +
                " SIZE\r\n MDTM\r\n REST STREAM\r\n MLSD\r\n MLST\r\n" +
                "211 End\r\n", ct);

        private async Task OPTS(string arg, CancellationToken ct)
        {
            if (arg.Equals("UTF8 ON", StringComparison.OrdinalIgnoreCase))
                await _s.WriteAsync("200 UTF8 set to on.\r\n", ct);
            else
                await _s.WriteAsync(FtpResponses.CmdOkay, ct);
        }

        private async Task HELP(string arg, CancellationToken ct)
        {
            var sb = new StringBuilder();
            sb.AppendLine("214-The following commands are recognized:");
            sb.AppendLine(" ABOR APPE ALLO AUTH");
            sb.AppendLine(" CDUP CWD DELE EPRT EPSV FEAT HELP");
            sb.AppendLine(" LIST MDTM MKD MODE NLST NOOP OPTS PASS");
            sb.AppendLine(" PASV PBSZ PORT PROT PWD QUIT REST RETR");
            sb.AppendLine(" RMD RNFR RNTO SITE SIZE STAT STOR STRU SYST");
            sb.AppendLine(" TYPE USER");
            sb.AppendLine("214 Help OK.");

            await _s.WriteAsync(sb.ToString().Replace("\n", "\r\n"), ct);
        }

        private async Task STAT(string arg, CancellationToken ct)
        {
            // STAT with argument: behave like LIST (classic behaviour)
            if (!string.IsNullOrWhiteSpace(arg))
            {
                await LIST(arg, ct);
                return;
            }

            var sb = new StringBuilder();

            var remote = _s.RemoteEndPoint?.ToString() ?? "<unknown>";
            var ip = _s.RemoteEndPoint?.Address;
            var userName = _s.Account?.UserName ?? "<not logged in>";

            var isBanned = false;
            if (ip is not null)
            {
                isBanned = _server.BanList.IsBanned(ip, out _);
            }

            // Classic 211 multi-line STAT response + short security line
            sb.Append("211- FTP status\r\n");
            sb.Append($"211- Remote     : {remote}\r\n");
            sb.Append($"211- User       : {userName}\r\n");
            sb.Append($"211- CWD        : {_s.Cwd}\r\n");

            // Short, single-line security summary:
            // rep=Good/Suspect/Blocked, cpm, failed, aborted, ipBanned
            var bannedFlag = ip is null ? "n/a" : (isBanned ? "yes" : "no");
            sb.Append(
                $"211- Security   : rep={_s.Reputation}, cpm={_s.CommandsPerMinute}, " +
                $"failed={_s.FailedLoginAttempts}, aborted={_s.AbortedTransfers}, ipBanned={bannedFlag}\r\n");

            sb.Append("211 End of status.\r\n");

            await _s.WriteAsync(sb.ToString(), ct);
        }
        private async Task ABOR(CancellationToken ct)
        {
            // Try to cancel the current data transfer (if any).
            var hadTransfer = _s.CancelActiveDataTransfer();

            if (hadTransfer)
            {
                // CancelActiveDataTransfer() already bumped aborted counters and reputation.
                // Just tell the client it worked.
                await _s.WriteAsync("226 Abort command successful; transfer cancelled.\r\n", ct);
            }
            else
            {
                // No active transfer; still respond with a friendly OK.
                await _s.WriteAsync("226 No transfer in progress.\r\n", ct);
            }
        }

        #endregion

        // --- Navigation ---
        #region Navigation
        private async Task CWD(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            // AMScript user-based rule
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("CWD", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.Message ?? "550 CWD denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            var newV = FtpPath.Normalize(_s.Cwd, arg);
            string? phys;
            try
            {
                phys = _fs.MapToPhysical(newV);
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            if (Directory.Exists(phys))
            {
                _s.Cwd = newV;
                await _s.WriteAsync(FtpResponses.ActionOk, ct);
            }
            else
            {
                await _s.WriteAsync("550 Directory not found.\r\n", ct);
            }
        }

        private async Task CDUP(CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            var newV = FtpPath.Normalize(_s.Cwd, "..");
            string? phys;
            try
            {
                phys = _fs.MapToPhysical(newV);
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            if (Directory.Exists(phys))
            {
                _s.Cwd = newV;
                await _s.WriteAsync(FtpResponses.ActionOk, ct);
            }
            else
            {
                await _s.WriteAsync("550 Directory not found.\r\n", ct);
            }
        }
        #endregion

        // --- Transfer parameters ---
        #region Transfer parameters
        private async Task TYPE(string arg, CancellationToken ct)
        {
            if (arg.StartsWith("I", StringComparison.OrdinalIgnoreCase))
                await _s.WriteAsync(FtpResponses.TypeSetBinary, ct);
            else
                await _s.WriteAsync(FtpResponses.TypeSetAscii, ct);
        }

        private async Task PASV(CancellationToken ct)
        {
            var port = await _s.OpenPassiveAsync(ct);

            var ep = (IPEndPoint)_s.Control.Client.LocalEndPoint!;
            var ip = ep.Address;
            var remote = (IPEndPoint)_s.Control.Client.RemoteEndPoint!;

            // FXP detection: passive IP != control connection IP
            // FXP script hook (if any)
            if (_fxpScript is not null && _isFxp)
            {
                var ctx = BuildSimpleContextForFxpAndActive("PASV");
                var result = _fxpScript.EvaluateDownload(ctx);
                if (result.Action == AMRuleAction.Deny)
                {
                    await _s.WriteAsync("504 FXP not allowed in PASV by rule.\r\n", ct);
                    return;
                }
            }

            // FXP policy engine (if configured)
            if (_fxpPolicy is not null && _isFxp)
            {
                var req = BuildFxpRequest(remote.Address, FxpDirection.Incoming);
                var decision = _fxpPolicy.Evaluate(req);
                _log.Log(FtpLogLevel.Info,
                    $"FXP {(decision.Allowed ? "ALLOW" : "DENY")} [PASV] user={req.UserName} admin={req.IsAdmin} " +
                    $"section={req.SectionName ?? "-"} vpath={req.VirtualPath} " +
                    $"dir={req.Direction} remoteHost={req.RemoteHost} remoteIp={req.RemoteIp} " +
                    $"ctlTls={(req.ControlTlsActive ? (req.ControlProtocol?.ToString() ?? "tls") : "plain")} " +
                    $"dataTls={(req.DataTlsActive ? (req.DataProtocol?.ToString() ?? "tls") : (req.DataChannelProtected ? "prot-only" : "plain"))} " +
                    $"reason={decision.DenyReason ?? "OK"}");
                if (!decision.Allowed)
                {
                    var reason = decision.DenyReason ?? "FXP not allowed in PASV by policy.";
                    await _s.WriteAsync("504 " + reason + "\r\n", ct);
                    return;
                }
            }

            // Built-in FXP policy
            var allowFxp = _s.Account?.AllowFxp ?? _cfg.AllowFxp;
            if (!allowFxp && _isFxp)
            {
                await _s.WriteAsync("504 FXP not allowed in PASV.\r\n", ct);
                return;
            }

            var bytes = ip.GetAddressBytes();
            var h = string.Join(",", bytes);
            var p1 = port / 256;
            var p2 = port % 256;
            await _s.WriteAsync($"227 Entering Passive Mode ({h},{p1},{p2}).\r\n", ct);
        }

        private async Task EPSV(CancellationToken ct)
        {
            var port = await _s.OpenPassiveAsync(ct);

            var ep = (IPEndPoint)_s.Control.Client.LocalEndPoint!;
            var ip = ep.Address;
            var remote = (IPEndPoint)_s.Control.Client.RemoteEndPoint!;

            // Treat EPSV similarly for FXP detection
            if (_fxpScript is not null && _isFxp)
            {
                var ctx = BuildSimpleContextForFxpAndActive("EPSV");
                var result = _fxpScript.EvaluateDownload(ctx);
                if (result.Action == AMRuleAction.Deny)
                {
                    await _s.WriteAsync("504 FXP not allowed in EPSV by rule.\r\n", ct);
                    return;
                }
            }

            if (_fxpPolicy is not null && _isFxp)
            {
                var req = BuildFxpRequest(remote.Address, FxpDirection.Incoming);
                var decision = _fxpPolicy.Evaluate(req);
                _log.Log(FtpLogLevel.Info,
                    $"FXP {(decision.Allowed ? "ALLOW" : "DENY")} [EPSV] user={req.UserName} admin={req.IsAdmin} " +
                    $"section={req.SectionName ?? "-"} vpath={req.VirtualPath} " +
                    $"dir={req.Direction} remoteHost={req.RemoteHost} remoteIp={req.RemoteIp} " +
                    $"ctlTls={(req.ControlTlsActive ? (req.ControlProtocol?.ToString() ?? "tls") : "plain")} " +
                    $"dataTls={(req.DataTlsActive ? (req.DataProtocol?.ToString() ?? "tls") : (req.DataChannelProtected ? "prot-only" : "plain"))} " +
                    $"reason={decision.DenyReason ?? "OK"}");

                if (!decision.Allowed)
                {
                    var reason = decision.DenyReason ?? "FXP not allowed in EPSV by policy.";
                    await _s.WriteAsync("504 " + reason + "\r\n", ct);
                    return;
                }
            }

            var allowFxp = _s.Account?.AllowFxp ?? _cfg.AllowFxp;
            if (!allowFxp && _isFxp)
            {
                await _s.WriteAsync("504 FXP not allowed in EPSV.\r\n", ct);
                return;
            }

            await _s.WriteAsync($"229 Entering Extended Passive Mode (|||{port}|)\r\n", ct);
        }

        private async Task PORT(string arg, CancellationToken ct)
        {
            var allowActive = _s.Account?.AllowActiveMode ?? _cfg.AllowActiveMode;
            if (!allowActive)
            {
                await _s.WriteAsync("502 Active mode disabled by policy.\r\n", ct);
                return;
            }

            var parts = arg.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 6)
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var ipString = string.Join('.', parts[0], parts[1], parts[2], parts[3]);
            var port = (int.Parse(parts[4]) << 8) + int.Parse(parts[5]);

            var requestedIp = IPAddress.Parse(ipString);
            var remote = (IPEndPoint)_s.Control.Client.RemoteEndPoint!;

            // FXP detection: active target != control IP
            _isFxp = !requestedIp.Equals(remote.Address);

            // Active-mode policy via script
            if (_activeScript is not null)
            {
                var ctx = BuildSimpleContextForFxpAndActive("PORT");
                var result = _activeScript.EvaluateDownload(ctx);
                if (result.Action == AMRuleAction.Deny)
                {
                    await _s.WriteAsync("504 Active mode denied by rule.\r\n", ct);
                    return;
                }
            }

            // FXP via script
            if (_fxpScript is not null && _isFxp)
            {
                var ctx = BuildSimpleContextForFxpAndActive("PORT");
                var result = _fxpScript.EvaluateDownload(ctx);
                if (result.Action == AMRuleAction.Deny)
                {
                    await _s.WriteAsync("504 FXP not allowed by rule.\r\n", ct);
                    return;
                }
            }

            // FXP policy engine (if configured)
            if (_fxpPolicy is not null && _isFxp)
            {
                var req = BuildFxpRequest(requestedIp, FxpDirection.Outgoing);
                var decision = _fxpPolicy.Evaluate(req);
                _log.Log(FtpLogLevel.Info,
                    $"FXP {(decision.Allowed ? "ALLOW" : "DENY")} [PORT] user={req.UserName} admin={req.IsAdmin} " +
                    $"section={req.SectionName ?? "-"} vpath={req.VirtualPath} " +
                    $"dir={req.Direction} remoteHost={req.RemoteHost} remoteIp={req.RemoteIp} " +
                    $"ctlTls={(req.ControlTlsActive ? (req.ControlProtocol?.ToString() ?? "tls") : "plain")} " +
                    $"dataTls={(req.DataTlsActive ? (req.DataProtocol?.ToString() ?? "tls") : (req.DataChannelProtected ? "prot-only" : "plain"))} " +
                    $"reason={decision.DenyReason ?? "OK"}");

                if (!decision.Allowed)
                {
                    var reason = decision.DenyReason ?? "FXP not allowed by policy.";
                    await _s.WriteAsync("504 " + reason + "\r\n", ct);
                    return;
                }
            }

            // Built-in FXP policy
            var allowFxp = _s.Account?.AllowFxp ?? _cfg.AllowFxp;
            if (!allowFxp && _isFxp)
            {
                await _s.WriteAsync("504 FXP not allowed: IP mismatch.\r\n", ct);
                return;
            }

            await _s.OpenActiveAsync(requestedIp, port, ct);
            await _s.WriteAsync(FtpResponses.CmdOkay, ct);
        }

        private async Task EPRT(string arg, CancellationToken ct)
        {
            var allowActive = _s.Account?.AllowActiveMode ?? _cfg.AllowActiveMode;
            if (!allowActive)
            {
                await _s.WriteAsync("502 Active mode disabled by policy.\r\n", ct);
                return;
            }

            var tok = arg.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length < 3)
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            // tok[0] = proto (1 = IPv4, 2 = IPv6, etc.)
            var ip = IPAddress.Parse(tok[1]);
            var port = int.Parse(tok[2]);

            var remote = (IPEndPoint)_s.Control.Client.RemoteEndPoint!;

            // FXP detection
            _isFxp = !ip.Equals(remote.Address);

            // Active-mode policy via AMScript (optional)
            if (_activeScript is not null)
            {
                var ctx = BuildSimpleContextForFxpAndActive("EPRT");
                var result = _activeScript.EvaluateDownload(ctx);
                if (result.Action == AMRuleAction.Deny)
                {
                    await _s.WriteAsync("504 Active mode denied by rule.\r\n", ct);
                    return;
                }
            }

            // FXP via AMScript (optional)
            if (_fxpScript is not null && _isFxp)
            {
                var ctx = BuildSimpleContextForFxpAndActive("EPRT");
                var result = _fxpScript.EvaluateDownload(ctx);
                if (result.Action == AMRuleAction.Deny)
                {
                    await _s.WriteAsync("504 FXP not allowed by rule.\r\n", ct);
                    return;
                }
            }

            // FXP policy engine (if configured)
            if (_fxpPolicy is not null && _isFxp)
            {
                var req = BuildFxpRequest(ip, FxpDirection.Outgoing);
                var decision = _fxpPolicy.Evaluate(req);
                _log.Log(FtpLogLevel.Info,
                    $"FXP {(decision.Allowed ? "ALLOW" : "DENY")} [EPRT] user={req.UserName} admin={req.IsAdmin} " +
                    $"section={req.SectionName ?? "-"} vpath={req.VirtualPath} " +
                    $"dir={req.Direction} remoteHost={req.RemoteHost} remoteIp={req.RemoteIp} " +
                    $"ctlTls={(req.ControlTlsActive ? (req.ControlProtocol?.ToString() ?? "tls") : "plain")} " +
                    $"dataTls={(req.DataTlsActive ? (req.DataProtocol?.ToString() ?? "tls") : (req.DataChannelProtected ? "prot-only" : "plain"))} " +
                    $"reason={decision.DenyReason ?? "OK"}");

                if (!decision.Allowed)
                {
                    var reason = decision.DenyReason ?? "FXP not allowed by policy.";
                    await _s.WriteAsync("504 " + reason + "\r\n", ct);
                    return;
                }
            }

            // Built-in FXP policy: simple IP mismatch check if account/cfg says FXP is not allowed.
            var allowFxp = _s.Account?.AllowFxp ?? _cfg.AllowFxp;
            if (!allowFxp && _isFxp)
            {
                await _s.WriteAsync("504 FXP not allowed: IP mismatch.\r\n", ct);
                return;
            }

            await _s.OpenActiveAsync(ip, port, ct);
            await _s.WriteAsync(FtpResponses.CmdOkay, ct);
        }
        #endregion

        // --- Listing / transfer ---
        #region Listing / transfer
        private async Task LIST(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            // AMScript user-based rule
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("LIST", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.Message ?? "550 LIST denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            var target = string.IsNullOrWhiteSpace(arg) ? "." : arg;

            var vfsResult = _s.VfsManager?.Resolve(target, _s.Account);
            if (vfsResult != null && (!vfsResult.Success || vfsResult.Node is null))
            {
                await _s.WriteAsync(vfsResult.ErrorMessage ?? "550 Not found.\r\n", ct);
                return;
            }

            if (vfsResult != null)
            {
                var node = vfsResult.Node;

                if (node != null)
                {
                    var access = _directoryAccess.Evaluate(node.VirtualPath);
                    if (!access.CanList)
                    {
                        await _s.WriteAsync("550 Listing not allowed in this directory.\r\n", ct);
                        return;
                    }
                }

                await _s.WriteAsync(FtpResponses.FileOk, ct);

                await _s.WithDataAsync(async stream =>
                {
                    await using var wr = new StreamWriter(
                        stream,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        bufferSize: 64 * 1024,
                        leaveOpen: true);

                    if (node != null)
                        switch (node.Type)
                        {
                            case VfsNodeType.PhysicalDirectory:
                            {
                                var dirPath = node.PhysicalPath!;
                                foreach (var dir in Directory.EnumerateDirectories(dirPath))
                                    await wr.WriteLineAsync(_fs.ToUnixListLine(new DirectoryInfo(dir)));

                                foreach (var file in Directory.EnumerateFiles(dirPath))
                                    await wr.WriteLineAsync(_fs.ToUnixListLine(new FileInfo(file)));
                                break;
                            }

                            case VfsNodeType.PhysicalFile:
                            {
                                await wr.WriteLineAsync(_fs.ToUnixListLine((FileInfo)node.FileSystemInfo!));
                                break;
                            }

                            case VfsNodeType.VirtualDirectory:
                            {
                                var name = Path.GetFileName(node.VirtualPath.TrimEnd('/'));
                                if (string.IsNullOrEmpty(name))
                                    name = "/";
                                // Simple synthetic directory listing line
                                await wr.WriteLineAsync($"drwxr-xr-x 1 owner group 0 Jan 01 00:00 {name}");
                                break;
                            }

                            case VfsNodeType.VirtualFile:
                            {
                                var name = Path.GetFileName(node.VirtualPath);
                                var size = node.VirtualContent is null
                                    ? 0
                                    : Encoding.UTF8.GetByteCount(node.VirtualContent);
                                await wr.WriteLineAsync($"-rw-r--r-- 1 owner group {size} Jan 01 00:00 {name}");
                                break;
                            }
                        }
                }, ct);
            }

            await _s.WriteAsync(FtpResponses.ClosingData, ct);
        }

        private async Task NLST(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            // AMScript user-based rule
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("NLST", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.Message ?? "550 NLST denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            var target = string.IsNullOrWhiteSpace(arg) ? "." : arg;

            var vfsResult = _s.VfsManager?.Resolve(target, _s.Account);
            if (vfsResult != null && (!vfsResult.Success || vfsResult.Node is null))
            {
                await _s.WriteAsync(vfsResult.ErrorMessage ?? "550 Not found.\r\n", ct);
                return;
            }

            if (vfsResult != null)
            {
                var node = vfsResult.Node;

                if (node != null)
                {
                    var access = _directoryAccess.Evaluate(node.VirtualPath);
                    if (!access.CanList)
                    {
                        await _s.WriteAsync("550 Listing not allowed in this directory.\r\n", ct);
                        return;
                    }
                }

                await _s.WriteAsync(FtpResponses.FileOk, ct);

                await _s.WithDataAsync(async stream =>
                {
                    await using var wr = new StreamWriter(
                        stream,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        bufferSize: 64 * 1024,
                        leaveOpen: true);

                    if (node != null)
                        switch (node.Type)
                        {
                            case VfsNodeType.PhysicalDirectory:
                            {
                                var dirPath = node.PhysicalPath!;
                                foreach (var dir in Directory.EnumerateDirectories(dirPath))
                                    await wr.WriteLineAsync(Path.GetFileName(dir));

                                foreach (var file in Directory.EnumerateFiles(dirPath))
                                    await wr.WriteLineAsync(Path.GetFileName(file));
                                break;
                            }

                            case VfsNodeType.PhysicalFile:
                            case VfsNodeType.VirtualFile:
                            case VfsNodeType.VirtualDirectory:
                            {
                                var name = Path.GetFileName(node.VirtualPath.TrimEnd('/'));
                                if (string.IsNullOrEmpty(name))
                                    name = "/";
                                await wr.WriteLineAsync(name);
                                break;
                            }
                        }
                }, ct);
            }

            await _s.WriteAsync(FtpResponses.ClosingData, ct);
        }

        private async Task MLSD(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            // AMScript user-based rule
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("MLSD", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.Message ?? "550 MLSD denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            var target = string.IsNullOrWhiteSpace(arg) ? "." : arg;

            var vfsResult = _s.VfsManager?.Resolve(target, _s.Account);
            if (vfsResult != null && (!vfsResult.Success || vfsResult.Node is null))
            {
                await _s.WriteAsync(vfsResult.ErrorMessage ?? "550 MLSD failed.\r\n", ct);
                return;
            }

            var node = vfsResult?.Node;

            if (node?.VirtualPath != null)
            {
                var access = _directoryAccess.Evaluate(node?.VirtualPath);
                if (!access.CanList)
                {
                    await _s.WriteAsync("550 Listing not allowed in this directory.\r\n", ct);
                    return;
                }
            }

            await _s.WriteAsync(FtpResponses.FileOk, ct);

            await _s.WithDataAsync(async stream =>
            {
                await using var wr = new StreamWriter(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 64 * 1024,
                    leaveOpen: true);

                switch (node?.Type)
                {
                    case VfsNodeType.PhysicalDirectory:
                        {
                            var dirPath = node.PhysicalPath!;
                            foreach (var dir in Directory.EnumerateDirectories(dirPath))
                                await wr.WriteLineAsync(_fs.ToMlsdLine(new DirectoryInfo(dir)));

                            foreach (var file in Directory.EnumerateFiles(dirPath))
                                await wr.WriteLineAsync(_fs.ToMlsdLine(new FileInfo(file)));
                            break;
                        }

                    case VfsNodeType.PhysicalFile:
                        {
                            await wr.WriteLineAsync(_fs.ToMlsdLine((FileInfo)node.FileSystemInfo!));
                            break;
                        }

                    case VfsNodeType.VirtualDirectory:
                        {
                            var name = Path.GetFileName(node.VirtualPath.TrimEnd('/'));
                            if (string.IsNullOrEmpty(name))
                                name = "/";
                            await wr.WriteLineAsync($"type=dir;perm=el; {name}");
                            break;
                        }

                    case VfsNodeType.VirtualFile:
                        {
                            var name = Path.GetFileName(node.VirtualPath);
                            var size = node.VirtualContent is null ? 0 : Encoding.UTF8.GetByteCount(node.VirtualContent);
                            await wr.WriteLineAsync($"type=file;size={size};perm=rl; {name}");
                            break;
                        }
                }
            }, ct);

            await _s.WriteAsync(FtpResponses.ClosingData, ct);
        }

        private async Task MLST(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            // AMScript user-based rule
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("MLST", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.Message ?? "550 MLST denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            var target = string.IsNullOrWhiteSpace(arg) ? "." : arg;

            var vfsResult = _s.VfsManager?.Resolve(target, _s.Account);
            if (vfsResult != null && (!vfsResult.Success || vfsResult.Node is null))
            {
                await _s.WriteAsync(vfsResult.ErrorMessage ?? "550 MLST failed.\r\n", ct);
                return;
            }

            var node = vfsResult?.Node;

            var access = _directoryAccess.Evaluate(node?.VirtualPath);
            if (!access.CanList)
            {
                await _s.WriteAsync("550 Listing not allowed in this directory.\r\n", ct);
                return;
            }

            string facts;
            switch (node?.Type)
            {
                case VfsNodeType.PhysicalDirectory:
                case VfsNodeType.PhysicalFile:
                    facts = _fs.ToMlsdLine(node.FileSystemInfo!);
                    break;

                case VfsNodeType.VirtualDirectory:
                    {
                        var name = Path.GetFileName(node.VirtualPath.TrimEnd('/'));
                        if (string.IsNullOrEmpty(name))
                            name = "/";
                        facts = $"type=dir;perm=el; {name}";
                        break;
                    }

                case VfsNodeType.VirtualFile:
                default:
                    {
                        var name = Path.GetFileName(node?.VirtualPath);
                        var size = node?.VirtualContent is null ? 0 : Encoding.UTF8.GetByteCount(node.VirtualContent);
                        facts = $"type=file;size={size};perm=rl; {name}";
                        break;
                    }
            }

            var sb = new StringBuilder();
            sb.Append("250-Listing\r\n");
            sb.Append(' ');
            sb.AppendLine(facts);
            sb.Append("250 End.\r\n");

            await _s.WriteAsync(sb.ToString(), ct);
        }

        private async Task RETR(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            if (_s.Account is { AllowDownload: false })
            {
                await _s.WriteAsync("550 Download not allowed for this user.\r\n", ct);
                return;
            }

            // AMScript user-based rule
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("RETR", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.Message ?? "550 RETR denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var vfsResult = _s.VfsManager?.Resolve(arg, _s.Account);
            if (vfsResult != null && (!vfsResult.Success || vfsResult.Node is null))
            {
                await _s.WriteAsync(vfsResult.ErrorMessage ?? "550 File not found.\r\n", ct);
                return;
            }

            var node = vfsResult?.Node;

            if (node != null && node.Type != VfsNodeType.PhysicalFile && node.Type != VfsNodeType.VirtualFile)
            {
                await _s.WriteAsync("550 File not found.\r\n", ct);
                return;
            }

            var virtTarget = node?.VirtualPath;

            var access = _directoryAccess.Evaluate(virtTarget);
            if (!access.CanDownload)
            {
                await _s.WriteAsync("550 Download not allowed in this directory.\r\n", ct);
                return;
            }

            long length;
            Stream sourceStream;
            try
            {
                if (node != null && node.Type == VfsNodeType.PhysicalFile)
                {
                    var phys = node.PhysicalPath!;
                    if (!File.Exists(phys))
                    {
                        await _s.WriteAsync("550 File not found.\r\n", ct);
                        return;
                    }

                    var fi = new FileInfo(phys);
                    length = fi.Length;
                    sourceStream = new FileStream(phys, FileMode.Open, FileAccess.Read, FileShare.Read);
                }
                else
                {
                    var content = node?.VirtualContent ?? string.Empty;
                    var buffer = Encoding.UTF8.GetBytes(content);
                    length = buffer.LongLength;
                    sourceStream = new MemoryStream(buffer, writable: false);
                }
            }
            catch
            {
                await _s.WriteAsync("550 File not accessible.\r\n", ct);
                return;
            }

            var rest = _s.RestOffset;
            if (rest.HasValue && rest.Value > 0 && rest.Value < length)
                length -= rest.Value;

            var section = GetSectionForVirtual(virtTarget);

            if (!await CheckDownloadCreditsAsync(virtTarget, section, length, ct))
            {
                await sourceStream.DisposeAsync();
                return;
            }

            await _s.WriteAsync(FtpResponses.FileOk, ct);

            var offset = rest;
            _s.ClearRestOffset();

            await _s.WithDataAsync(async s =>
            {
                await using (sourceStream)
                {
                    if (offset.HasValue && offset.Value > 0 && sourceStream.CanSeek)
                        sourceStream.Seek(offset.Value, SeekOrigin.Begin);

                    var maxKbps = _s.Account?.MaxDownloadKbps ?? 0;
                    var transferred = await CopyWithThrottleAsync(sourceStream, s, maxKbps, isDownload: true, ct);
                    ApplyDownloadCredits(virtTarget, section, transferred);
                    if (transferred > 0) FireSiteEvent("onDownload", virtTarget, section, _s.Account?.UserName);
                }
            }, ct);

            await _s.WriteAsync(FtpResponses.ClosingData, ct);
        }

        private async Task STOR(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            if (_s.Account is { AllowUpload: false })
            {
                await _s.WriteAsync("550 Upload not allowed for this user.\r\n", ct);
                return;
            }

            // AMScript user-based rule
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("STOR", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.Message ?? "550 STOR denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            // Normalize the virtual path using existing logic
            var virtTarget = FtpPath.Normalize(_s.Cwd, arg);

            // Resolve the parent directory through VFS so mounts/overlays are respected
            var dirVirtRaw = Path.GetDirectoryName(virtTarget);
            var dirVirt = string.IsNullOrEmpty(dirVirtRaw) || dirVirtRaw == "\\"
                ? "/"
                : dirVirtRaw.Replace('\\', '/');
            var fileName = Path.GetFileName(virtTarget);

            var access = _directoryAccess.Evaluate(dirVirt);
            if (!access.CanUpload)
            {
                await _s.WriteAsync("550 Upload not allowed in this directory.\r\n", ct);
                return;
            }

            string physDir;
            try
            {
                var dirResult = _s.VfsManager?.Resolve(dirVirt, _s.Account);
                if (dirResult != null && dirResult.Success && dirResult.Node is { Type: VfsNodeType.PhysicalDirectory } node)
                {
                    physDir = node.PhysicalPath!;
                }
                else
                {
                    // Fallback to legacy behaviour if VFS has no opinion
                    physDir = Path.GetDirectoryName(_fs.MapToPhysical(virtTarget))
                              ?? throw new UnauthorizedAccessException();
                }
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            var phys = Path.Combine(physDir, fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(phys)!);

            var section = GetSectionForVirtual(virtTarget);

            await _s.WriteAsync(FtpResponses.FileOk, ct);

            var rest = _s.RestOffset;
            _s.ClearRestOffset();

            await _s.WithDataAsync(async s =>
            {
                var mode = rest.HasValue && rest.Value > 0 ? FileMode.OpenOrCreate : FileMode.Create;

                await using var fs = new FileStream(phys, mode, FileAccess.Write, FileShare.None);
                if (rest.HasValue && rest.Value > 0)
                    fs.Seek(rest.Value, SeekOrigin.Begin);

                var maxKbps = _s.Account?.MaxUploadKbps ?? 0;
                var transferred = await CopyWithThrottleAsync(s, fs, maxKbps, isDownload: false, ct);
                ApplyUploadCredits(virtTarget, section, transferred);

                if (transferred > 0)
                {
                    // AMScript / legacy hooks
                    FireSiteEvent("onUpload", virtTarget, section, _s.Account?.UserName);

                    // Zipscript integration
                    if (_runtime.Zipscript is not null)
                    {
                        var ctx = new ZipscriptUploadContext(
                            section.Name,
                            virtTarget,
                            phys,
                            transferred,
                            _s.Account?.UserName,
                            DateTimeOffset.UtcNow);

                        _runtime.Zipscript.OnUploadComplete(ctx);
                    }

                    if (_s.Account is { } acc)
                    {
                        _raceEngine.RegisterUpload(acc.UserName, dirVirt, section.Name, transferred);
                        UpdateDupeOnUpload(section, dirVirt, acc, transferred);
                    }

                    // EventBus: announce upload for IRC / other listeners
                    var releaseName = Path.GetFileName(
                        (dirVirt ?? string.Empty).TrimEnd('/', '\\'));

                    _runtime.EventBus?.Publish(new FtpEvent
                    {
                        Type = FtpEventType.Upload,
                        Timestamp = DateTimeOffset.UtcNow,
                        SessionId = _s.SessionId,
                        User = _s.Account?.UserName,
                        Group = _s.Account?.GroupName,
                        Section = section?.Name,
                        VirtualPath = virtTarget,
                        ReleaseName = string.IsNullOrEmpty(releaseName) ? virtTarget : releaseName,
                        Bytes = transferred
                    });
                }
            }, ct);

            await _s.WriteAsync(FtpResponses.ClosingData, ct);
        }

        private async Task APPE(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            if (_s.Account is { AllowUpload: false })
            {
                await _s.WriteAsync("550 Upload not allowed for this user.\r\n", ct);
                return;
            }

            // AMScript user-based rule
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("APPE", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.Message ?? "550 APPE denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            // Normalize the virtual path
            var virtTarget = FtpPath.Normalize(_s.Cwd, arg);

            // Resolve parent dir through VFS
            var dirVirtRaw = Path.GetDirectoryName(virtTarget);
            var dirVirt = string.IsNullOrEmpty(dirVirtRaw) || dirVirtRaw == "\\"
                ? "/"
                : dirVirtRaw.Replace('\\', '/');
            var fileName = Path.GetFileName(virtTarget);

            var access = _directoryAccess.Evaluate(dirVirt);
            if (!access.CanUpload)
            {
                await _s.WriteAsync("550 Upload not allowed in this directory.\r\n", ct);
                return;
            }

            string physDir;
            try
            {
                var dirResult = _s.VfsManager?.Resolve(dirVirt, _s.Account);
                if (dirResult != null && dirResult.Success && dirResult.Node is { Type: VfsNodeType.PhysicalDirectory } node)
                {
                    physDir = node.PhysicalPath!;
                }
                else
                {
                    // Fallback to legacy behaviour if VFS has no opinion
                    physDir = Path.GetDirectoryName(_fs.MapToPhysical(virtTarget))
                              ?? throw new UnauthorizedAccessException();
                }
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            var phys = Path.Combine(physDir, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(phys)!);

            var section = GetSectionForVirtual(virtTarget);

            await _s.WriteAsync(FtpResponses.FileOk, ct);

            // APPE ignores REST by convention
            _s.ClearRestOffset();

            await _s.WithDataAsync(async s =>
            {
                await using var fs = new FileStream(phys, FileMode.Append, FileAccess.Write, FileShare.None);

                var maxKbps = _s.Account?.MaxUploadKbps ?? 0;
                var transferred = await CopyWithThrottleAsync(s, fs, maxKbps, isDownload: false, ct);
                ApplyUploadCredits(virtTarget, section, transferred);

                if (transferred > 0)
                {
                    // AMScript / legacy hooks
                    FireSiteEvent("onUpload", virtTarget, section, _s.Account?.UserName);

                    // Zipscript integration
                    if (_runtime.Zipscript is not null)
                    {
                        var ctx = new ZipscriptUploadContext(
                            section.Name,
                            virtTarget,
                            phys,
                            transferred,
                            _s.Account?.UserName,
                            DateTimeOffset.UtcNow);

                        _runtime.Zipscript.OnUploadComplete(ctx);
                    }


                    if (_s.Account is { } acc)
                    {
                        // Use the directory virtual path as race key, same as STOR
                        _raceEngine.RegisterUpload(acc.UserName, dirVirt, section.Name, transferred);
                        UpdateDupeOnUpload(section, dirVirt, acc, transferred);
                    }

                    // EventBus: announce upload for IRC / other listeners
                    var releaseName = Path.GetFileName(
                        (dirVirt ?? string.Empty).TrimEnd('/', '\\'));

                    _runtime.EventBus?.Publish(new FtpEvent
                    {
                        Type = FtpEventType.Upload,
                        Timestamp = DateTimeOffset.UtcNow,
                        SessionId = _s.SessionId,
                        User = _s.Account?.UserName,
                        Group = _s.Account?.GroupName,
                        Section = section?.Name,
                        VirtualPath = virtTarget,
                        ReleaseName = string.IsNullOrEmpty(releaseName) ? virtTarget : releaseName,
                        Bytes = transferred
                    });
                }
            }, ct);

            await _s.WriteAsync(FtpResponses.ClosingData, ct);
        }

        private async Task REST(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            // AMScript user-based rule (optional)
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("REST", arg);
                var res = _userScript.EvaluateDownload(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.DenyReason ?? "550 REST denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            if (!long.TryParse(arg, out var offset) || offset < 0)
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            _s.RestOffset = offset;
            await _s.WriteAsync($"350 Restarting at {offset}. Send STORE or RETRIEVE.\r\n", ct);
        }
        #endregion

        // --- File system ops ---
        #region File system ops
        private async Task DELE(string arg, CancellationToken ct)
        {
            // AMScript user-based rule
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("DELE", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.Message ?? "550 DELE denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var virtTarget = FtpPath.Normalize(_s.Cwd, arg);

            var vfsResult = _s.VfsManager?.Resolve(virtTarget, _s.Account);
            if (vfsResult != null && (!vfsResult.Success || vfsResult.Node is null))
            {
                await _s.WriteAsync(vfsResult.ErrorMessage ?? "550 File not found.\r\n", ct);
                return;
            }

            var node = vfsResult?.Node;

            // Only allow deleting physical files for now
            if (node != null && node.Type != VfsNodeType.PhysicalFile)
            {
                await _s.WriteAsync("550 File not found.\r\n", ct);
                return;
            }

            // Directory flags: treat delete as a “modify” operation -> CanUpload
            var access = _directoryAccess.Evaluate(node?.VirtualPath);
            if (!access.CanUpload)
            {
                await _s.WriteAsync("550 Delete not allowed in this directory.\r\n", ct);
                return;
            }

            var section = GetSectionForVirtual(node?.VirtualPath);
            var phys = node?.PhysicalPath!;

            try
            {
                if (File.Exists(phys))
                {
                    File.Delete(phys);
                    FireSiteEvent("onDelete", node?.VirtualPath, section, _s.Account?.UserName);
                    if (_runtime.Zipscript is not null && node?.VirtualPath is not null)
                    {
                        var delCtx = new ZipscriptDeleteContext(
                            section.Name,
                            node.VirtualPath,
                            phys,
                            IsDirectory: false,
                            _s.Account?.UserName,
                            DateTimeOffset.UtcNow);

                        _runtime.Zipscript.OnDelete(delCtx);
                    }
                    await _s.WriteAsync(FtpResponses.ActionOk, ct);
                }
                else
                {
                    await _s.WriteAsync("550 File not found.\r\n", ct);
                }
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
            }
        }

        private async Task MKD(string arg, CancellationToken ct)
        {
            // AMScript user-based rule
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("MKD", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.Message ?? "550 MKD denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var virtTarget = FtpPath.Normalize(_s.Cwd, arg);
            var section = GetSectionForVirtual(virtTarget);

            // Find parent directory in VFS
            var dirVirtRaw = Path.GetDirectoryName(virtTarget);
            var dirVirt = string.IsNullOrEmpty(dirVirtRaw) || dirVirtRaw == "\\"
                ? "/"
                : dirVirtRaw.Replace('\\', '/');
            var newName = Path.GetFileName(virtTarget);

            var access = _directoryAccess.Evaluate(dirVirt);
            if (!access.CanUpload)
            {
                await _s.WriteAsync("550 MKD not allowed in this directory.\r\n", ct);
                return;
            }

            string physDir;
            try
            {
                var dirResult = _s.VfsManager?.Resolve(dirVirt, _s.Account);
                if (dirResult != null && dirResult.Success && dirResult.Node is { Type: VfsNodeType.PhysicalDirectory } node)
                {
                    physDir = node.PhysicalPath!;
                }
                else
                {
                    // Fallback to legacy behaviour if VFS has no opinion
                    physDir = Path.GetDirectoryName(_fs.MapToPhysical(virtTarget))
                              ?? throw new UnauthorizedAccessException();
                }
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            var phys = Path.Combine(physDir, newName);

            try
            {
                Directory.CreateDirectory(phys);
                FireSiteEvent("onMkdir", virtTarget, section, _s.Account?.UserName);
                await _s.WriteAsync(FtpResponses.PathCreated, ct);
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
            }
        }

        private async Task RMD(string arg, CancellationToken ct)
        {
            // AMScript user-based rule
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("RMD", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.Message ?? "550 RMD denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var virtTarget = FtpPath.Normalize(_s.Cwd, arg);
            var section = GetSectionForVirtual(virtTarget);

            var vfsResult = _s.VfsManager?.Resolve(virtTarget, _s.Account);
            if (vfsResult != null && (!vfsResult.Success || vfsResult.Node is null))
            {
                await _s.WriteAsync(vfsResult.ErrorMessage ?? "550 Directory not found.\r\n", ct);
                return;
            }

            var node = vfsResult?.Node;

            if (node != null && node.Type != VfsNodeType.PhysicalDirectory)
            {
                await _s.WriteAsync("550 Directory not found.\r\n", ct);
                return;
            }

            var access = _directoryAccess.Evaluate(node?.VirtualPath);
            if (!access.CanUpload)
            {
                await _s.WriteAsync("550 RMD not allowed in this directory.\r\n", ct);
                return;
            }

            var phys = node?.PhysicalPath!;

            try
            {
                if (Directory.Exists(phys))
                {
                    Directory.Delete(phys, recursive: true);
                    FireSiteEvent("onRmdir", node?.VirtualPath, section, _s.Account?.UserName);
                    if (_runtime.Zipscript is not null && node?.VirtualPath is not null)
                    {
                        var delCtx = new ZipscriptDeleteContext(
                            section.Name,
                            node.VirtualPath,
                            phys,
                            IsDirectory: true,
                            _s.Account?.UserName,
                            DateTimeOffset.UtcNow);

                        _runtime.Zipscript.OnDelete(delCtx);
                    }
                    await _s.WriteAsync(FtpResponses.ActionOk, ct);
                }
                else
                {
                    await _s.WriteAsync("550 Directory not found.\r\n", ct);
                }
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
            }
        }

        private async Task RNTO(string arg, CancellationToken ct)
        {
            // AMScript user-based rule
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("RNTO", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.Message ?? "550 RNTO denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(_s.RenameFrom))
            {
                await _s.WriteAsync(FtpResponses.BadSeq, ct);
                return;
            }

            var fromVirt = FtpPath.Normalize(_s.Cwd, _s.RenameFrom);
            var toVirt = FtpPath.Normalize(_s.Cwd, arg);

            // Resolve source via VFS
            var fromResult = _s.VfsManager?.Resolve(fromVirt, _s.Account);
            if (fromResult != null && (!fromResult.Success || fromResult.Node is null))
            {
                await _s.WriteAsync(fromResult.ErrorMessage ?? "550 Not found.\r\n", ct);
                return;
            }

            var fromNode = fromResult?.Node;
            if (fromNode != null &&
                fromNode.Type != VfsNodeType.PhysicalFile &&
                fromNode.Type != VfsNodeType.PhysicalDirectory)
            {
                await _s.WriteAsync("550 Not found.\r\n", ct);
                return;
            }

            var fromPhys = fromNode?.PhysicalPath!;
            if (string.IsNullOrEmpty(fromPhys))
            {
                await _s.WriteAsync("550 Not found.\r\n", ct);
                return;
            }

            // Destination: resolve target directory via VFS
            var toDirVirtRaw = Path.GetDirectoryName(toVirt);
            var toDirVirt = string.IsNullOrEmpty(toDirVirtRaw) || toDirVirtRaw == "\\"
                ? "/"
                : toDirVirtRaw.Replace('\\', '/');
            var toName = Path.GetFileName(toVirt);

            var access = _directoryAccess.Evaluate(toDirVirt);
            if (!access.CanUpload)
            {
                await _s.WriteAsync("550 RNTO not allowed in this directory.\r\n", ct);
                return;
            }

            string? toPhys;
            try
            {
                var toDirResult = _s.VfsManager?.Resolve(toDirVirt, _s.Account);
                if (toDirResult != null && toDirResult.Success && toDirResult.Node is { Type: VfsNodeType.PhysicalDirectory } toDirNode)
                {
                    var physDir = toDirNode.PhysicalPath!;
                    toPhys = Path.Combine(physDir, toName);
                }
                else
                {
                    // fallback: old behaviour
                    toPhys = _fs.MapToPhysical(toVirt);
                }
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            if (string.IsNullOrEmpty(toPhys))
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            // Determine what we're renaming BEFORE we move it
            var isFile = File.Exists(fromPhys);
            var isDir = Directory.Exists(fromPhys);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(toPhys)!);

                if (isFile)
                {
                    File.Move(fromPhys, toPhys, overwrite: true);
                }
                else if (isDir)
                {
                    Directory.Move(fromPhys, toPhys);
                }
                else
                {
                    await _s.WriteAsync("550 Not found.\r\n", ct);
                    return;
                }
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            // Zipscript integration for renames
            try
            {
                if (_runtime.Zipscript is not null)
                {
                    var section = GetSectionForVirtual(toVirt);
                    var now = DateTimeOffset.UtcNow;
                    var user = _s.Account?.UserName;

                    if (isDir)
                    {
                        // Directory rename: remove old release state and rescan new path
                        var delCtx = new ZipscriptDeleteContext(
                            section.Name,
                            fromVirt,
                            fromPhys,
                            IsDirectory: true,
                            user,
                            now);

                        _runtime.Zipscript.OnDelete(delCtx);

                        var rescanCtx = new ZipscriptRescanContext(
                            section.Name,
                            toVirt,
                            toPhys,
                            user,
                            IncludeSubdirs: false,
                            RequestedAt: now);

                        _runtime.Zipscript.OnRescanDir(rescanCtx);
                    }
                    else if (isFile)
                    {
                        // File rename: delete old file entry, then treat new path as uploaded
                        var delCtx = new ZipscriptDeleteContext(
                            section.Name,
                            fromVirt,
                            fromPhys,
                            IsDirectory: false,
                            user,
                            now);

                        _runtime.Zipscript.OnDelete(delCtx);

                        long size = 0;
                        try
                        {
                            var fi = new FileInfo(toPhys);
                            size = fi.Length;
                        }
                        catch
                        {
                            // ignore size failure, still notify zipscript
                        }

                        var upCtx = new ZipscriptUploadContext(
                            section.Name,
                            toVirt,
                            toPhys,
                            size,
                            user,
                            now);

                        _runtime.Zipscript.OnUploadComplete(upCtx);
                    }
                }
            }
            catch
            {
                // zipscript is best-effort; don't fail RNTO if it blows up
            }

            _s.RenameFrom = null;
            await _s.WriteAsync(FtpResponses.ActionOk, ct);
        }

        private async Task SIZE(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            // AMScript user-based rule
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("SIZE", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.Message ?? "550 SIZE denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var virtTarget = FtpPath.Normalize(_s.Cwd, arg);

            var access = _directoryAccess.Evaluate(virtTarget);
            if (!access.CanDownload)
            {
                await _s.WriteAsync("550 SIZE not allowed in this directory.\r\n", ct);
                return;
            }

            var vfsResult = _s.VfsManager?.Resolve(virtTarget, _s.Account);
            if (vfsResult != null && (!vfsResult.Success || vfsResult.Node is null))
            {
                await _s.WriteAsync(vfsResult.ErrorMessage ?? "550 File not found.\r\n", ct);
                return;
            }

            var node = vfsResult?.Node;

            if (node?.Type != VfsNodeType.PhysicalFile)
            {
                await _s.WriteAsync("550 File not found.\r\n", ct);
                return;
            }

            var phys = node.PhysicalPath!;
            if (!File.Exists(phys))
            {
                await _s.WriteAsync("550 File not found.\r\n", ct);
                return;
            }

            var fi = new FileInfo(phys);
            await _s.WriteAsync($"213 {fi.Length}\r\n", ct);
        }

        private async Task MDTM(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            // AMScript user-based rule
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("MDTM", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.Message ?? "550 MDTM denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var virtTarget = FtpPath.Normalize(_s.Cwd, arg);

            var access = _directoryAccess.Evaluate(virtTarget);
            if (!access.CanDownload)
            {
                await _s.WriteAsync("550 MDTM not allowed in this directory.\r\n", ct);
                return;
            }

            var vfsResult = _s.VfsManager?.Resolve(virtTarget, _s.Account);
            if (vfsResult != null && (!vfsResult.Success || vfsResult.Node is null))
            {
                await _s.WriteAsync(vfsResult.ErrorMessage ?? "550 File not found.\r\n", ct);
                return;
            }

            var node = vfsResult?.Node;

            if (node?.Type != VfsNodeType.PhysicalFile)
            {
                await _s.WriteAsync("550 File not found.\r\n", ct);
                return;
            }

            var phys = node.PhysicalPath!;
            if (!File.Exists(phys))
            {
                await _s.WriteAsync("550 File not found.\r\n", ct);
                return;
            }

            var utc = File.GetLastWriteTimeUtc(phys);
            var stamp = utc.ToString("yyyyMMddHHmmss");
            await _s.WriteAsync($"213 {stamp}\r\n", ct);
        }

        private async Task RNFR(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            // AMScript user-based rule
            if (_userScript is not null && _s.Account is not null)
            {
                var ctx = BuildUserContext("RNFR", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    var msg = res.Message ?? "550 RNFR denied by policy.\r\n";
                    if (!msg.EndsWith("\r\n", StringComparison.Ordinal))
                        msg += "\r\n";
                    await _s.WriteAsync(msg, ct);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var virtPath = FtpPath.Normalize(_s.Cwd, arg);

            // Treat rename as a “write” operation → upload permission
            var access = _directoryAccess.Evaluate(virtPath);
            if (!access.CanUpload)
            {
                await _s.WriteAsync("550 RNFR not allowed in this directory.\r\n", ct);
                return;
            }

            var vfsResult = _s.VfsManager?.Resolve(virtPath, _s.Account);
            if (vfsResult != null && (!vfsResult.Success || vfsResult.Node is null))
            {
                await _s.WriteAsync(vfsResult.ErrorMessage ?? "550 File not found.\r\n", ct);
                return;
            }

            var node = vfsResult?.Node;
            if (node != null &&
                node.Type != VfsNodeType.PhysicalFile &&
                node.Type != VfsNodeType.PhysicalDirectory)
            {
                await _s.WriteAsync("550 RNFR only supported for physical files/directories.\r\n", ct);
                return;
            }

            _s.RenameFrom = virtPath;
            await _s.WriteAsync("350 File exists, ready for destination name.\r\n", ct);
        }

        #endregion

        // --- SITE stub ---

        private async Task SITE(string arg, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync("500 SITE requires a subcommand.\r\n", ct);
                return;
            }

            // Parse original input
            var parts = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
            var rawSub = parts[0];
            var rest = parts.Length > 1 ? parts[1] : string.Empty;

            // Compatibility-aware verb normalization
            var compat = _cfg.Compatibility;
            var sub = SiteCommandRouterCompat.NormalizeVerb(rawSub, compat);

            // --------------------------------------------------------------------------------------------------
            // AMSCRIPT PRE-HOOK: Allow scripts to block, override, or output custom responses.
            // --------------------------------------------------------------------------------------------------
            if (_siteScript is not null)
            {
                var ctx = BuildSiteContext(sub, rest);
                var result = _siteScript.EvaluateUpload(ctx);

                // 1) Script rule BLOCKS (DENY)
                if (result.Action == AMRuleAction.Deny)
                {
                    await _s.WriteAsync("550 SITE command denied by rule.\r\n", ct);
                    return;
                }

                // 2) Script provides custom OUTPUT
                if (!string.IsNullOrEmpty(result.SiteOutput))
                {
                    await _s.WriteAsync($"200 {result.SiteOutput}\r\n", ct);
                    return;
                }

                // 3) Script provides OVERRIDE (return override)
                if (result.Message == "SITE_OVERRIDE")
                {
                    // The override is complete; script handles output. We simply return success.
                    await _s.WriteAsync("200 OK\r\n", ct);
                    return;
                }
            }

            // --------------------------------------------------------------------------------------------------
            // NO SCRIPT OVERRIDE OCCURRED → PROCESS BUILT-IN SITE COMMANDS VIA FRAMEWORK
            // --------------------------------------------------------------------------------------------------

            var account = _s.Account;
            if (account is null)
            {
                await _s.WriteAsync("530 Please login first.\r\n", ct);
                return;
            }

            // Context for authorization: has verb, args, router, session, etc.
            var authCtx = new SiteCommandContext(this, sub, rest);

            if (!FtpAuthorization.CanUseSiteCommand(account, sub, authCtx))
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                _log.Log(FtpLogLevel.Warn,
                    $"SITE {sub} rejected for user {account.UserName} from {_s.RemoteEndPoint}");
                return;
            }

            // Special-case: SITE SECURITY is implemented directly here, not via _siteCommands
            if (sub.Equals("SECURITY", StringComparison.OrdinalIgnoreCase))
            {
                await HandleSiteSecurityAsync(rest, ct);
                return;
            }

            if (!_siteCommands.TryGetValue(sub, out var cmd))
            {
                // Show the original verb in the error so scripts see what they typed
                await _s.WriteAsync($"502 Unknown SITE command '{rawSub}'.\r\n", ct);
                return;
            }

            // Existing per-command RequiresAdmin flag (extra guard, still valid)
            if (cmd.RequiresAdmin && !account.IsAdmin)
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            await cmd.ExecuteAsync(_siteContext, rest, ct);
        }

        private async Task HandleSiteSecurityAsync(string args, CancellationToken ct)
        {
            var sb = new StringBuilder();

            var remote = _s.RemoteEndPoint?.ToString() ?? "<unknown>";
            var ip = _s.RemoteEndPoint?.Address;

            var isBanned = false;
            string? banReason = null;

            if (ip is not null)
            {
                isBanned = _server.BanList.IsBanned(ip, out banReason);
            }

            // Multi-line 211 response per RFC-style
            sb.Append("211- Security status\r\n");
            sb.Append($"211- Remote              : {remote}\r\n");
            sb.Append($"211- Session reputation  : {_s.Reputation}\r\n");
            sb.Append($"211- Failed logins       : {_s.FailedLoginAttempts}\r\n");
            sb.Append($"211- Aborted transfers   : {_s.AbortedTransfers}\r\n");
            sb.Append($"211- Commands/min (sess) : {_s.CommandsPerMinute}\r\n");
            sb.Append($"211- Total commands      : {_s.TotalCommandCount}\r\n");

            if (ip is not null)
            {
                var banLine = isBanned
                    ? $"yes{(string.IsNullOrWhiteSpace(banReason) ? string.Empty : $" ({banReason})")}"
                    : "no";

                sb.Append($"211- IP banned           : {banLine}\r\n");
            }
            else
            {
                sb.Append("211- IP banned           : <n/a>\r\n");
            }

            sb.Append("211-\r\n");
            sb.Append($"211- MaxConnectionsGlobal            : {_cfg.MaxConnectionsGlobal}\r\n");
            sb.Append($"211- MaxConnectionsPerIp             : {_cfg.MaxConnectionsPerIp}\r\n");
            sb.Append($"211- MaxFailedLoginsPerIp            : {_cfg.MaxFailedLoginsPerIp}\r\n");
            sb.Append($"211- MaxCommandsPerMinute            : {_cfg.MaxCommandsPerMinute}\r\n");
            sb.Append($"211- FailedLoginSuspectThreshold     : {_cfg.FailedLoginSuspectThreshold}\r\n");
            sb.Append($"211- FailedLoginBlockThreshold       : {_cfg.FailedLoginBlockThreshold}\r\n");
            sb.Append($"211- AbortedTransferSuspectThreshold : {_cfg.AbortedTransferSuspectThreshold}\r\n");
            sb.Append($"211- AbortedTransferBlockThreshold   : {_cfg.AbortedTransferBlockThreshold}\r\n");
            sb.Append("211 End of security status.\r\n");

            await _s.WriteAsync(sb.ToString(), ct);
        }

        private async Task VERSION(CancellationToken ct)
        {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? asm.GetName().Version?.ToString()
                      ?? "unknown";

            // Same logo spirit as Program.cs, but sent as 211- lines
            var logo = new[]
            {
                @".______  ._____.___ .____________._._______ .______  ",
                @":      \ :         |:_ ____/\__ _:|: ____  |:_ _   \ ",
                @"|   .   ||   \  /  ||   _/    |  :||    :  ||   |   |",
                @"|   :   ||   |\/   ||   |     |   ||   |___|| . |   |",
                @"|___|   ||___| |   ||_. |     |   ||___|    |. ____/ ",
                @"    |___|      |___|  :/      |___|        :/       ",
                @"                    :                     :         "
            };

            await _s.WriteAsync("211- amFTPd VERSION\r\n", ct);
            foreach (var line in logo)
            {
                await _s.WriteAsync("211- " + line + "\r\n", ct);
            }

            await _s.WriteAsync($"211- amFTPd - a managed FTP daemon v{ver}\r\n", ct);
            await _s.WriteAsync("211 End\r\n", ct);
        }

        private static string FormatDirFlag(bool? value) =>
            value is true ? "ALLOW"
            : value is false ? "DENY"
            : "INHERIT";

        /// <summary>
        /// Fires a logical SITE event into the site AMScript engine (site.msl),
        /// using the release/virtual path as context. This is used for things
        /// like onNuke / onRaceComplete that are not direct SITE commands.
        /// </summary>
        internal void FireSiteEvent(
            string eventName,
            string? releaseVirtPath,
            Config.Ftpd.FtpSection? section,
            string? userName)
        {
            if (_siteScript is null)
                return;

            // Resolve physical path if possible
            string? phys;
            try
            {
                phys = _fs.MapToPhysical(releaseVirtPath);
            }
            catch
            {
                phys = string.Empty;
            }

            var acc = _s.Account;
            var userGroup = acc?.GroupName ?? string.Empty;

            var ctx = new AMScriptContext(
                IsFxp: _isFxp,
                Section: section?.Name ?? string.Empty,
                FreeLeech: section?.FreeLeech ?? false,
                UserName: userName ?? acc?.UserName ?? string.Empty,
                UserGroup: userGroup,
                Bytes: 0,
                Kb: 0,
                CostDownload: 0,
                EarnedUpload: 0,
                VirtualPath: releaseVirtPath,
                PhysicalPath: phys,
                Event: eventName
            );

            // We treat these as fire-and-forget notifications.
            // Scripts can log / trigger external actions; we ignore returned action.
            try
            {
                _siteScript.EvaluateUpload(ctx);
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Error, $"AMScript site event '{eventName}' failed: {ex.Message}");
            }
        }
    }
}
