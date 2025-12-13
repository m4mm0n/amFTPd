/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpCommandRouter.Commands.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-13 04:18:09
 *  CRC32:          0x1485CC36
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
 * ==================================================================================================== */









using amFTPd.Core.Events;
using amFTPd.Core.Fxp;
using amFTPd.Core.Ident;
using amFTPd.Core.Site;
using amFTPd.Core.Vfs;
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
                // Transfer was in progress and is now cancelled.
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
                    await using var wr = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);

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
                    await using var wr = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);

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
                await using var wr = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);

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
                    var transferred = await CopyWithThrottleAsync(sourceStream, s, maxKbps, ct);
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
                var transferred = await CopyWithThrottleAsync(s, fs, maxKbps, ct);
                ApplyUploadCredits(virtTarget, section, transferred);

                if (transferred > 0)
                {
                    // AMScript / legacy hooks
                    FireSiteEvent("onUpload", virtTarget, section, _s.Account?.UserName);

                    // Zipscript integration
                    if (_runtime.Zipscript is not null)
                    {
                        var physicalPath = phys;
                        _runtime.Zipscript.OnFileUploaded(
                            virtTarget,
                            physicalPath,
                            section.Name,
                            transferred);
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
                var transferred = await CopyWithThrottleAsync(s, fs, maxKbps, ct);
                ApplyUploadCredits(virtTarget, section, transferred);

                if (transferred > 0)
                {
                    // AMScript / legacy hooks
                    FireSiteEvent("onUpload", virtTarget, section, _s.Account?.UserName);

                    // Zipscript integration
                    if (_runtime.Zipscript is not null)
                    {
                        var physicalPath = phys;
                        _runtime.Zipscript.OnFileUploaded(
                            virtTarget,
                            physicalPath,
                            section.Name,
                            transferred);
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

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(toPhys)!);

                if (File.Exists(fromPhys))
                {
                    if (toPhys != null) File.Move(fromPhys, toPhys, overwrite: true);
                }
                else if (Directory.Exists(fromPhys))
                {
                    if (toPhys != null) Directory.Move(fromPhys, toPhys);
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
            var sub = parts[0].ToUpperInvariant();
            var rest = parts.Length > 1 ? parts[1] : string.Empty;

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
                await _s.WriteAsync($"502 Unknown SITE command '{sub}'.\r\n", ct);
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

        #region Old SITE commands (commented out)

        //private async Task SITE(string arg, CancellationToken ct)
        //{
        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync("500 SITE requires a subcommand.\r\n", ct);
        //        return;
        //    }

        //    // Parse original input
        //    var parts = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
        //    var sub = parts[0].ToUpperInvariant();
        //    var rest = parts.Length > 1 ? parts[1] : string.Empty;

        //    if (sub is "DBFSCK" or "DBBACKUP" or "DBSUMMARY")
        //    {
        //        var account = _s.Account;
        //        if (account is null || !account.IsAdmin)
        //        {
        //            await _s.WriteAsync("550 Permission denied.\r\n", ct);
        //            return;
        //        }

        //        if (_runtime.Database is null)
        //        {
        //            await _s.WriteAsync("550 Database backend not enabled.\r\n", ct);
        //            return;
        //        }

        //        var maint = new DatabaseMaintenance(_runtime.Database, _log);

        //        switch (sub)
        //        {
        //            case "DBFSCK":
        //                maint.RunFsck();
        //                await _s.WriteAsync("200 DBFSCK completed.\r\n", ct);
        //                return;

        //            case "DBBACKUP":
        //                maint.CreateBackup();
        //                await _s.WriteAsync("200 Database backup created.\r\n", ct);
        //                return;

        //            case "DBSUMMARY":
        //                _runtime.Database.PrintSummary();
        //                await _s.WriteAsync("200 Database summary logged.\r\n", ct);
        //                return;
        //        }
        //    }

        //    // --------------------------------------------------------------------------------------------------
        //    // AMSCRIPT PRE-HOOK: Allow scripts to block, override, or output custom responses.
        //    // --------------------------------------------------------------------------------------------------
        //    if (_siteScript is not null)
        //    {
        //        var ctx = BuildSiteContext(sub, rest);
        //        var result = _siteScript.EvaluateUpload(ctx);

        //        // 1) Script rule BLOCKS (DENY)
        //        if (result.Action == AMRuleAction.Deny)
        //        {
        //            await _s.WriteAsync("550 SITE command denied by rule.\r\n", ct);
        //            return;
        //        }

        //        // 2) Script provides custom OUTPUT
        //        if (!string.IsNullOrEmpty(result.SiteOutput))
        //        {
        //            await _s.WriteAsync($"200 {result.SiteOutput}\r\n", ct);
        //            return;
        //        }

        //        // 3) Script provides OVERRIDE (return override)
        //        if (result.Message == "SITE_OVERRIDE")
        //        {
        //            // The override is complete; script handles output. We simply return success.
        //            await _s.WriteAsync("200 OK\r\n", ct);
        //            return;
        //        }
        //    }

        //    // --------------------------------------------------------------------------------------------------
        //    // NO SCRIPT OVERRIDE OCCURRED → PROCESS BUILT-IN SITE COMMANDS AS NORMAL
        //    // --------------------------------------------------------------------------------------------------
        //    switch (sub)
        //    {
        //        case "HELP":
        //            await _s.WriteAsync(
        //                "214-SITE commands:\r\n" +
        //                " SITE HELP\r\n" +
        //                " SITE ADDIP <user> <mask>\r\n" +
        //                " SITE ADDUSER <user> <password> <homedir> [group]\r\n" +
        //                " SITE CHMOD <mode> <path>\r\n" +
        //                " SITE CHGRP <user> <primary-group> [secondary-groups...]\r\n" +
        //                " SITE CHPASS <user> <newpassword>\r\n" +
        //                " SITE CREDITS <user>\r\n" +
        //                " SITE DELIP <user>\r\n" +
        //                " SITE DIRFLAGS <path> [flags]\r\n" +
        //                " SITE GADDUSER <group> <user>\r\n" +
        //                " SITE GIVECRED <user> <amount>\r\n" +
        //                " SITE GROUPS\r\n" +
        //                " SITE IDENT <user> <ident>\r\n" +
        //                " SITE KILL <id>\r\n" +
        //                " SITE LASTRACES [max]\r\n" +
        //                " SITE MOVE <src> <dst>\r\n" +
        //                " SITE NUKE <path> <reason...>\r\n" +
        //                " SITE RACE <path>\r\n" +
        //                " SITE RACELOG [max]\r\n" +
        //                " SITE RACESTATS <path>\r\n" +      
        //                " SITE REQIDENT <user> <on|off>\r\n" +
        //                " SITE SECTIONS\r\n" +
        //                " SITE SETFLAGS <user> <flags>\r\n" +
        //                " SITE SETLIMITS <user> <upKbps> <downKbps>\r\n" +
        //                " SITE SHOWUSER <user>\r\n" +
        //                " SITE TAKECRED <user> <amount>\r\n" +
        //                " SITE USERS\r\n" +
        //                " SITE WHO\r\n" +
        //                " SITE WIPE <path> [reason...]\r\n" +
        //                "214 End\r\n"
        //                , ct);
        //            break;

        //        case "WHO":
        //            await SITE_WHO(ct);
        //            break;

        //        case "USERS":
        //            await SITE_USERS(ct);
        //            break;

        //        case "GROUPS":
        //            await SITE_GROUPS(ct);
        //            break;

        //        case "KILL":
        //            await SITE_KILL(rest, ct);
        //            break;

        //        case "CHMOD":
        //            await SITE_CHMOD(rest, ct);
        //            break;

        //        case "ADDUSER":
        //            await SITE_ADDUSER(rest, ct);
        //            break;

        //        case "GADDUSER":
        //            await SITE_GADDUSER(rest, ct);
        //            break;

        //        case "CHGRP":
        //            await SITE_CHGRP(rest, ct);
        //            break;

        //        case "CHPASS":
        //            await SITE_CHPASS(rest, ct);
        //            break;

        //        case "SETLIMITS":
        //            await SITE_SETLIMITS(rest, ct);
        //            break;

        //        case "ADDIP":
        //            await SITE_ADDIP(rest, ct);
        //            break;

        //        case "DELIP":
        //            await SITE_DELIP(rest, ct);
        //            break;

        //        case "IDENT":
        //            await SITE_IDENT(rest, ct);
        //            break;

        //        case "REQIDENT":
        //            await SITE_REQIDENT(rest, ct);
        //            break;

        //        case "SHOWUSER":
        //            await SITE_SHOWUSER(rest, ct);
        //            break;

        //        case "SETFLAGS":
        //            await SITE_SETFLAGS(rest, ct);
        //            break;

        //        case "CREDITS":
        //            await SITE_CREDITS(rest, ct);
        //            break;

        //        case "GIVECRED":
        //            await SITE_GIVECRED(rest, ct);
        //            break;

        //        case "TAKECRED":
        //            await SITE_TAKECRED(rest, ct);
        //            break;

        //        case "SECTIONS":
        //            await SITE_SECTIONS(ct);
        //            break;

        //        case "DIRFLAGS":
        //            await SITE_DIRFLAGS(rest, ct);
        //            break;

        //        case "NUKE":
        //            await SITE_NUKE(rest, ct);
        //            break;

        //        case "WIPE":
        //            await SITE_WIPE(rest, ct);
        //            break;

        //        case "MOVE":
        //            await SITE_MOVE(rest, ct);
        //            break;

        //        case "RACE":
        //            await SITE_RACE(rest, ct);
        //            break;

        //        case "RACESTATS":
        //            await SITE_RACESTATS(rest, ct);
        //            break;

        //        case "LASTRACES":
        //            await SITE_LASTRACES(rest, ct);
        //            break;

        //        case "RACELOG":
        //            await SITE_RACELOG(rest, ct);
        //            break;

        //        default:
        //            await _s.WriteAsync("502 SITE subcommand not implemented.\r\n", ct);
        //            break;
        //    }
        //}

        //internal async Task SITE_SHOWUSER(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is null)
        //    {
        //        await _s.WriteAsync("550 Not logged in.\r\n", ct);
        //        return;
        //    }

        //    // If no argument: show current user
        //    string targetName;
        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        targetName = _s.Account.UserName;
        //    }
        //    else
        //    {
        //        targetName = arg.Trim();

        //        // Non-admins can only see themselves
        //        if (!_s.Account.IsAdmin &&
        //            !targetName.Equals(_s.Account.UserName, StringComparison.OrdinalIgnoreCase))
        //        {
        //            await _s.WriteAsync("550 Only admin can inspect other users.\r\n", ct);
        //            return;
        //        }
        //    }

        //    var user = _s.Users.FindUser(targetName);
        //    if (user is null)
        //    {
        //        await _s.WriteAsync("550 User not found.\r\n", ct);
        //        return;
        //    }

        //    var sb = new StringBuilder();
        //    sb.AppendLine("211-User information:");
        //    sb.AppendLine($" USER       : {user.UserName}");
        //    sb.AppendLine($" GROUP      : {user.GroupName ?? "-"}");
        //    sb.AppendLine($" HOME       : {user.HomeDir}");
        //    sb.AppendLine($" ADMIN      : {(user.IsAdmin ? "YES" : "NO")}");
        //    sb.AppendLine($" FXP        : {(user.AllowFxp ? "YES" : "NO")}");
        //    sb.AppendLine($" UPLOAD     : {(user.AllowUpload ? "YES" : "NO")}");
        //    sb.AppendLine($" DOWNLOAD   : {(user.AllowDownload ? "YES" : "NO")}");
        //    sb.AppendLine($" ACTIVE MODE: {(user.AllowActiveMode ? "YES" : "NO")}");
        //    sb.AppendLine($" MAX LOGINS : {user.MaxConcurrentLogins}");
        //    sb.AppendLine($" IDLE TIME  : {(int)user.IdleTimeout.TotalSeconds} seconds");
        //    sb.AppendLine($" UL LIMIT   : {user.MaxUploadKbps} KB/s");
        //    sb.AppendLine($" DL LIMIT   : {user.MaxDownloadKbps} KB/s");
        //    sb.AppendLine($" CREDITS    : {user.CreditsKb} KB");
        //    sb.AppendLine($" IP MASK    : {user.AllowedIpMask ?? "-"}");
        //    sb.AppendLine($" REQ IDENT  : {(user.RequireIdentMatch ? "YES" : "NO")}");
        //    sb.AppendLine($" IDENT NAME : {user.RequiredIdent ?? "-"}");
        //    sb.AppendLine("211 End");

        //    await _s.WriteAsync(sb.ToString().Replace("\n", "\r\n"), ct);
        //}

        //internal async Task SITE_ADDIP(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE ADDIP requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync("501 Usage: SITE ADDIP <user> <mask>\r\n", ct);
        //        return;
        //    }

        //    var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        //    if (parts.Length < 2)
        //    {
        //        await _s.WriteAsync("501 Usage: SITE ADDIP <user> <mask>\r\n", ct);
        //        return;
        //    }

        //    var userName = parts[0];
        //    var mask = parts[1];

        //    var user = _s.Users.FindUser(userName);
        //    if (user is null)
        //    {
        //        await _s.WriteAsync("550 User not found.\r\n", ct);
        //        return;
        //    }

        //    var updated = user with { AllowedIpMask = mask };

        //    if (_s.Users.TryUpdateUser(updated, out var error))
        //    {
        //        await _s.WriteAsync($"200 IP mask set to '{mask}'.\r\n", ct);
        //    }
        //    else
        //    {
        //        await _s.WriteAsync($"550 Failed to update user: {error}\r\n", ct);
        //    }
        //}

        //internal async Task SITE_DELIP(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE DELIP requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync("501 Usage: SITE DELIP <user>\r\n", ct);
        //        return;
        //    }

        //    var userName = arg.Trim();

        //    var user = _s.Users.FindUser(userName);
        //    if (user is null)
        //    {
        //        await _s.WriteAsync("550 User not found.\r\n", ct);
        //        return;
        //    }

        //    var updated = user with { AllowedIpMask = null };

        //    if (_s.Users.TryUpdateUser(updated, out var error))
        //    {
        //        await _s.WriteAsync("200 IP mask cleared.\r\n", ct);
        //    }
        //    else
        //    {
        //        await _s.WriteAsync($"550 Failed to update user: {error}\r\n", ct);
        //    }
        //}

        //internal async Task SITE_IDENT(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE IDENT requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync("501 Usage: SITE IDENT <user> <ident>\r\n", ct);
        //        return;
        //    }

        //    var parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        //    if (parts.Length < 2)
        //    {
        //        await _s.WriteAsync("501 Usage: SITE IDENT <user> <ident>\r\n", ct);
        //        return;
        //    }

        //    var userName = parts[0];
        //    var ident = parts[1];

        //    var user = _s.Users.FindUser(userName);
        //    if (user is null)
        //    {
        //        await _s.WriteAsync("550 User not found.\r\n", ct);
        //        return;
        //    }

        //    var updated = user with { RequiredIdent = ident };

        //    if (_s.Users.TryUpdateUser(updated, out var error))
        //    {
        //        await _s.WriteAsync($"200 Required ident set to '{ident}'.\r\n", ct);
        //    }
        //    else
        //    {
        //        await _s.WriteAsync($"550 Failed to update user: {error}\r\n", ct);
        //    }
        //}

        //internal async Task SITE_REQIDENT(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE REQIDENT requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync("501 Usage: SITE REQIDENT <user> <on|off>\r\n", ct);
        //        return;
        //    }

        //    var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        //    if (parts.Length < 2)
        //    {
        //        await _s.WriteAsync("501 Usage: SITE REQIDENT <user> <on|off>\r\n", ct);
        //        return;
        //    }

        //    var userName = parts[0];
        //    var modeStr = parts[1].ToLowerInvariant();

        //    bool value;
        //    if (modeStr is "on" or "1" or "true" or "yes")
        //        value = true;
        //    else if (modeStr is "off" or "0" or "false" or "no")
        //        value = false;
        //    else
        //    {
        //        await _s.WriteAsync("501 Mode must be on|off.\r\n", ct);
        //        return;
        //    }

        //    var user = _s.Users.FindUser(userName);
        //    if (user is null)
        //    {
        //        await _s.WriteAsync("550 User not found.\r\n", ct);
        //        return;
        //    }

        //    var updated = user with { RequireIdentMatch = value };

        //    if (_s.Users.TryUpdateUser(updated, out var error))
        //    {
        //        await _s.WriteAsync($"200 Require ident set to {(value ? "ON" : "OFF")}.\r\n", ct);
        //    }
        //    else
        //    {
        //        await _s.WriteAsync($"550 Failed to update user: {error}\r\n", ct);
        //    }
        //}

        //internal async Task SITE_CREDITS(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is null)
        //    {
        //        await _s.WriteAsync("550 Not logged in.\r\n", ct);
        //        return;
        //    }

        //    FtpUser? target;
        //    string who;

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        target = _s.Account;
        //        who = target.UserName;
        //    }
        //    else
        //    {
        //        if (_s.Account is not { IsAdmin: true })
        //        {
        //            await _s.WriteAsync("550 Only admin can query other users' credits.\r\n", ct);
        //            return;
        //        }

        //        target = _s.Users.FindUser(arg);
        //        if (target is null)
        //        {
        //            await _s.WriteAsync("550 User not found.\r\n", ct);
        //            return;
        //        }
        //        who = target.UserName;
        //    }

        //    await _s.WriteAsync($"200 CREDITS {who}: {target.CreditsKb} KB\r\n", ct);
        //}

        //internal async Task SITE_GIVECRED(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE GIVECRED requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync("501 Usage: SITE GIVECRED <user> <MB>\r\n", ct);
        //        return;
        //    }

        //    var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        //    if (parts.Length < 2 || !long.TryParse(parts[1], out var mb) || mb <= 0)
        //    {
        //        await _s.WriteAsync("501 Usage: SITE GIVECRED <user> <MB>\r\n", ct);
        //        return;
        //    }

        //    var userName = parts[0];
        //    var user = _s.Users.FindUser(userName);
        //    if (user is null)
        //    {
        //        await _s.WriteAsync("550 User not found.\r\n", ct);
        //        return;
        //    }

        //    var deltaKb = mb * 1024;
        //    var updated = user with { CreditsKb = user.CreditsKb + deltaKb };

        //    if (_s.Users.TryUpdateUser(updated, out var error))
        //    {
        //        await _s.WriteAsync($"200 Credits added: {mb} MB (new {updated.CreditsKb} KB).\r\n", ct);
        //    }
        //    else
        //    {
        //        await _s.WriteAsync($"550 Failed to update credits: {error}\r\n", ct);
        //    }
        //}

        //internal async Task SITE_TAKECRED(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE TAKECRED requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync("501 Usage: SITE TAKECRED <user> <MB>\r\n", ct);
        //        return;
        //    }

        //    var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        //    if (parts.Length < 2 || !long.TryParse(parts[1], out var mb) || mb <= 0)
        //    {
        //        await _s.WriteAsync("501 Usage: SITE TAKECRED <user> <MB>\r\n", ct);
        //        return;
        //    }

        //    var userName = parts[0];
        //    var user = _s.Users.FindUser(userName);
        //    if (user is null)
        //    {
        //        await _s.WriteAsync("550 User not found.\r\n", ct);
        //        return;
        //    }

        //    var deltaKb = mb * 1024;
        //    var updated = user with { CreditsKb = Math.Max(0, user.CreditsKb - deltaKb) };

        //    if (_s.Users.TryUpdateUser(updated, out var error))
        //    {
        //        await _s.WriteAsync($"200 Credits removed: {mb} MB (new {updated.CreditsKb} KB).\r\n", ct);
        //    }
        //    else
        //    {
        //        await _s.WriteAsync($"550 Failed to update credits: {error}\r\n", ct);
        //    }
        //}

        //internal async Task SITE_SECTIONS(CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE SECTIONS requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    var secs = _sections.GetSections();

        //    var sb = new StringBuilder();
        //    sb.AppendLine("211-Sections:");
        //    foreach (var s in secs)
        //    {
        //        sb.Append(" NAME=").Append(s.Name);
        //        sb.Append(" ROOT=").Append(s.VirtualRoot);
        //        sb.Append(" FREE=").Append(s.FreeLeech ? "Y" : "N");
        //        sb.Append(" RATIO=").Append(s.RatioUploadUnit).Append(':').Append(s.RatioDownloadUnit);
        //        sb.AppendLine();
        //    }
        //    sb.AppendLine("211 End");

        //    await _s.WriteAsync(sb.ToString().Replace("\n", "\r\n"), ct);
        //}

        //internal async Task SITE_USERS(CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE USERS requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    var users = _s.Users.GetAllUsers();

        //    var sb = new StringBuilder();
        //    sb.AppendLine("211-Configured users:");
        //    foreach (var u in users)
        //    {
        //        sb.Append(" USER=").Append(u.UserName);
        //        sb.Append(" GROUP=").Append(u.GroupName ?? "-");
        //        sb.Append(" ADMIN=").Append(u.IsAdmin ? "Y" : "N");
        //        sb.Append(" FXP=").Append(u.AllowFxp ? "Y" : "N");
        //        sb.Append(" UP=").Append(u.AllowUpload ? "Y" : "N");
        //        sb.Append(" DOWN=").Append(u.AllowDownload ? "Y" : "N");
        //        sb.Append(" ACTIVE=").Append(u.AllowActiveMode ? "Y" : "N");
        //        sb.Append(" MAXLOGINS=").Append(u.MaxConcurrentLogins);
        //        sb.Append(" IDLE=").Append((int)u.IdleTimeout.TotalSeconds).Append("s");
        //        sb.Append(" UL=").Append(u.MaxUploadKbps).Append("kB/s");
        //        sb.Append(" DL=").Append(u.MaxDownloadKbps).Append("kB/s");
        //        sb.AppendLine();
        //    }
        //    sb.AppendLine("211 End");

        //    await _s.WriteAsync(sb.ToString().Replace("\n", "\r\n"), ct);
        //}

        //internal async Task SITE_GROUPS(CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE GROUPS requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    var users = _s.Users.GetAllUsers();
        //    var groups = users
        //        .GroupBy(u => u.GroupName ?? "(none)")
        //        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        //    var sb = new StringBuilder();
        //    sb.AppendLine("211-Groups:");
        //    foreach (var g in groups)
        //    {
        //        sb.Append(" GROUP=").Append(g.Key);
        //        sb.Append(" USERS=").Append(g.Count());
        //        sb.AppendLine();
        //    }
        //    sb.AppendLine("211 End");

        //    await _s.WriteAsync(sb.ToString().Replace("\n", "\r\n"), ct);
        //}

        //internal async Task SITE_CHPASS(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE CHPASS requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync("501 Usage: SITE CHPASS <user> <newpassword>\r\n", ct);
        //        return;
        //    }

        //    var parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        //    if (parts.Length < 2)
        //    {
        //        await _s.WriteAsync("501 Usage: SITE CHPASS <user> <newpassword>\r\n", ct);
        //        return;
        //    }

        //    var userName = parts[0];
        //    var newPass = parts[1];

        //    var existing = _s.Users.FindUser(userName);
        //    if (existing is null)
        //    {
        //        await _s.WriteAsync("550 User not found.\r\n", ct);
        //        return;
        //    }

        //    var newHash = PasswordHasher.HashPassword(newPass);
        //    var updated = existing with { PasswordHash = newHash };

        //    if (_s.Users.TryUpdateUser(updated, out var error))
        //    {
        //        await _s.WriteAsync("200 Password changed.\r\n", ct);
        //    }
        //    else
        //    {
        //        await _s.WriteAsync($"550 Failed to change password: {error}\r\n", ct);
        //    }
        //}

        //internal async Task SITE_SETLIMITS(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE SETLIMITS requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync("501 Usage: SITE SETLIMITS <user> <upKbps> <downKbps>\r\n", ct);
        //        return;
        //    }

        //    var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        //    if (parts.Length < 3)
        //    {
        //        await _s.WriteAsync("501 Usage: SITE SETLIMITS <user> <upKbps> <downKbps>\r\n", ct);
        //        return;
        //    }

        //    var userName = parts[0];
        //    if (!int.TryParse(parts[1], out var up) || up < 0 ||
        //        !int.TryParse(parts[2], out var down) || down < 0)
        //    {
        //        await _s.WriteAsync("501 Invalid limits.\r\n", ct);
        //        return;
        //    }

        //    var existing = _s.Users.FindUser(userName);
        //    if (existing is null)
        //    {
        //        await _s.WriteAsync("550 User not found.\r\n", ct);
        //        return;
        //    }

        //    var updated = existing with
        //    {
        //        MaxUploadKbps = up,
        //        MaxDownloadKbps = down
        //    };

        //    if (_s.Users.TryUpdateUser(updated, out var error))
        //    {
        //        await _s.WriteAsync("200 Limits updated.\r\n", ct);
        //    }
        //    else
        //    {
        //        await _s.WriteAsync($"550 Failed to update limits: {error}\r\n", ct);
        //    }
        //}

        //internal async Task SITE_SETFLAGS(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE SETFLAGS requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync("501 Usage: SITE SETFLAGS <user> <flags>\r\n", ct);
        //        return;
        //    }

        //    var parts = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
        //    if (parts.Length < 2)
        //    {
        //        await _s.WriteAsync("501 Usage: SITE SETFLAGS <user> <flags>\r\n", ct);
        //        return;
        //    }

        //    var userName = parts[0];
        //    var flagsStr = parts[1];

        //    var existing = _s.Users.FindUser(userName);
        //    if (existing is null)
        //    {
        //        await _s.WriteAsync("550 User not found.\r\n", ct);
        //        return;
        //    }

        //    var isAdmin = existing.IsAdmin;
        //    var allowFxp = existing.AllowFxp;
        //    var allowUpload = existing.AllowUpload;
        //    var allowDownload = existing.AllowDownload;
        //    var allowActive = existing.AllowActiveMode;

        //    var flags = flagsStr.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        //    foreach (var raw in flags)
        //    {
        //        var f = raw.Trim();
        //        if (f.Length < 2) continue;

        //        var op = f[0];
        //        var name = f[1..].ToLowerInvariant();

        //        var value = op == '+';

        //        switch (name)
        //        {
        //            case "admin":
        //                isAdmin = value;
        //                break;
        //            case "fxp":
        //                allowFxp = value;
        //                break;
        //            case "upload":
        //                allowUpload = value;
        //                break;
        //            case "download":
        //                allowDownload = value;
        //                break;
        //            case "active":
        //                allowActive = value;
        //                break;
        //        }
        //    }

        //    var updated = existing with
        //    {
        //        IsAdmin = isAdmin,
        //        AllowFxp = allowFxp,
        //        AllowUpload = allowUpload,
        //        AllowDownload = allowDownload,
        //        AllowActiveMode = allowActive
        //    };

        //    if (_s.Users.TryUpdateUser(updated, out var error))
        //    {
        //        await _s.WriteAsync("200 Flags updated.\r\n", ct);
        //    }
        //    else
        //    {
        //        await _s.WriteAsync($"550 Failed to update flags: {error}\r\n", ct);
        //    }
        //}

        //internal async Task SITE_ADDUSER(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE ADDUSER requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync("501 Usage: SITE ADDUSER <user> <password> <homedir> [group]\r\n", ct);
        //        return;
        //    }

        //    var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        //    if (parts.Length < 3)
        //    {
        //        await _s.WriteAsync("501 Usage: SITE ADDUSER <user> <password> <homedir> [group]\r\n", ct);
        //        return;
        //    }

        //    var userName = parts[0];
        //    var password = parts[1];
        //    var homeDir = parts[2];
        //    var group = parts.Length >= 4 ? parts[3] : null;

        //    var passwordHash = PasswordHasher.HashPassword(password);

        //    var user = new FtpUser(
        //        UserName: userName,
        //        PasswordHash: passwordHash,
        //        HomeDir: homeDir,
        //        IsAdmin: false,
        //        AllowFxp: false,          // default: FXP off
        //        AllowUpload: true,
        //        AllowDownload: true,
        //        AllowActiveMode: true,
        //        MaxConcurrentLogins: 3,
        //        IdleTimeout: TimeSpan.FromMinutes(30),
        //        MaxUploadKbps: 0,
        //        MaxDownloadKbps: 0,
        //        PrimaryGroup: string.IsNullOrWhiteSpace(group) ? "users" : group,
        //        SecondaryGroups: ImmutableArray<string>.Empty,
        //        CreditsKb: 0,             // start with 0 credits, admin can GIVECRED/SITE CREDITS
        //        AllowedIpMask: null,
        //        RequireIdentMatch: false,
        //        RequiredIdent: null,
        //        FlagsRaw: string.Empty
        //    );

        //    if (_s.Users.TryAddUser(user, out var error))
        //        await _s.WriteAsync("200 User added.\r\n", ct);
        //    else
        //        await _s.WriteAsync($"550 Failed to add user: {error}\r\n", ct);
        //}

        //internal async Task SITE_GADDUSER(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE GADDUSER requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync("501 Usage: SITE GADDUSER <group> <user>\r\n", ct);
        //        return;
        //    }

        //    var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        //    if (parts.Length < 2)
        //    {
        //        await _s.WriteAsync("501 Usage: SITE GADDUSER <group> <user>\r\n", ct);
        //        return;
        //    }

        //    var group = parts[0];
        //    var userName = parts[1];

        //    var existing = _s.Users.FindUser(userName);
        //    if (existing is null)
        //    {
        //        await _s.WriteAsync("550 User not found.\r\n", ct);
        //        return;
        //    }

        //    var updated = existing with { PrimaryGroup = group };

        //    if (_s.Users.TryUpdateUser(updated, out var error))
        //    {
        //        await _s.WriteAsync("200 User assigned to group.\r\n", ct);
        //    }
        //    else
        //    {
        //        await _s.WriteAsync($"550 Failed to update user: {error}\r\n", ct);
        //    }
        //}

        //internal async Task SITE_WHO(CancellationToken ct)
        //{
        //    // Admin-only
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE WHO requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    var sessions = FtpSession.GetActiveSessions();

        //    var sb = new StringBuilder();
        //    sb.AppendLine("211-Active sessions:");
        //    foreach (var sess in sessions.OrderBy(x => x.SessionId))
        //    {
        //        var ep = sess.Control.Client.RemoteEndPoint?.ToString() ?? "unknown";
        //        var user = sess.UserName ?? "(not logged in)";
        //        var idle = (DateTimeOffset.UtcNow - sess.LastActivity).TotalSeconds;
        //        sb.AppendLine($" ID={sess.SessionId} USER={user} IP={ep} IDLE={idle:F0}s");
        //    }
        //    sb.AppendLine("211 End");

        //    await _s.WriteAsync(sb.ToString().Replace("\n", "\r\n"), ct);
        //}

        //internal async Task SITE_KILL(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE KILL requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg) || !int.TryParse(arg, out var id))
        //    {
        //        await _s.WriteAsync("501 Usage: SITE KILL <session-id>\r\n", ct);
        //        return;
        //    }

        //    var target = FtpSession.GetActiveSessions().FirstOrDefault(s => s.SessionId == id);
        //    if (target is null)
        //    {
        //        await _s.WriteAsync("550 No such session.\r\n", ct);
        //        return;
        //    }

        //    if (ReferenceEquals(target, _s))
        //    {
        //        await _s.WriteAsync("550 Cannot kill your own session with SITE KILL.\r\n", ct);
        //        return;
        //    }

        //    try
        //    {
        //        target.MarkQuit();
        //        try { target.Control.Close(); } catch { /* ignore */ }
        //        await _s.WriteAsync("200 Session killed.\r\n", ct);
        //    }
        //    catch
        //    {
        //        await _s.WriteAsync("550 Failed to kill session.\r\n", ct);
        //    }
        //}

        //internal async Task SITE_CHMOD(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE CHMOD requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync("501 Usage: SITE CHMOD <mode> <path>\r\n", ct);
        //        return;
        //    }

        //    var parts = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
        //    if (parts.Length < 2)
        //    {
        //        await _s.WriteAsync("501 Usage: SITE CHMOD <mode> <path>\r\n", ct);
        //        return;
        //    }

        //    var modeStr = parts[0];
        //    var pathArg = parts[1];

        //    if (!int.TryParse(modeStr, out var mode) || mode <= 0)
        //    {
        //        await _s.WriteAsync("501 Invalid mode.\r\n", ct);
        //        return;
        //    }

        //    var virt = FtpPath.Normalize(_s.Cwd, pathArg);
        //    string phys;
        //    try
        //    {
        //        phys = _fs.MapToPhysical(virt);
        //    }
        //    catch
        //    {
        //        await _s.WriteAsync("550 Permission denied.\r\n", ct);
        //        return;
        //    }

        //    if (!File.Exists(phys) && !Directory.Exists(phys))
        //    {
        //        await _s.WriteAsync("550 File or directory not found.\r\n", ct);
        //        return;
        //    }

        //    try
        //    {
        //        // Very rough semantics:
        //        // If owner's write bit is 0 (e.g. 444), mark as ReadOnly.
        //        // If owner's write bit is 1 (e.g. 644, 755, 777), clear ReadOnly.
        //        var ownerWritable = ((mode / 10) % 10) >= 2; // second digit, >=2 => write

        //        if (File.Exists(phys))
        //        {
        //            var attrs = File.GetAttributes(phys);

        //            if (ownerWritable)
        //                attrs &= ~FileAttributes.ReadOnly;
        //            else
        //                attrs |= FileAttributes.ReadOnly;

        //            File.SetAttributes(phys, attrs);
        //        }
        //        else if (Directory.Exists(phys))
        //        {
        //            var attrs = File.GetAttributes(phys);

        //            if (ownerWritable)
        //                attrs &= ~FileAttributes.ReadOnly;
        //            else
        //                attrs |= FileAttributes.ReadOnly;

        //            File.SetAttributes(phys, attrs);
        //        }

        //        await _s.WriteAsync("200 CHMOD applied (best effort).\r\n", ct);
        //    }
        //    catch
        //    {
        //        await _s.WriteAsync("550 Failed to change mode.\r\n", ct);
        //    }
        //}

        //internal async Task SITE_DIRFLAGS(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE DIRFLAGS requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync(
        //            "501 Usage: SITE DIRFLAGS <path> [flags]\r\n" +
        //            "501 Flags syntax: +list/-list +upload/-upload +download/-download\r\n" +
        //            "501 LIST flag affects: LIST, NLST, MLSD, MLST\r\n" +
        //            "501 UPLOAD flag affects: STOR, APPE\r\n" +
        //            "501 DOWNLOAD flag affects: RETR, SIZE, MDTM\r\n",
        //            ct);
        //        return;
        //    }

        //    var parts = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
        //    var pathArg = parts[0];
        //    var flagsStr = parts.Length > 1 ? parts[1] : string.Empty;

        //    // Normalize against current working dir, like other path-handling code
        //    var virtPath = FtpPath.Normalize(_s.Cwd, pathArg);

        //    if (!_directoryRules.TryGetValue(virtPath, out var rule)) rule = DirectoryRule.Empty;

        //    var allowUpload = rule.AllowUpload;
        //    var allowDownload = rule.AllowDownload;
        //    var allowList = rule.AllowList;

        //    // If no flags specified, just show current state
        //    if (string.IsNullOrWhiteSpace(flagsStr))
        //    {
        //        await _s.WriteAsync(
        //            $"211-Directory flags for {virtPath}\r\n" +
        //            $" LIST:     {FormatDirFlag(allowList)} (affects LIST, NLST, MLSD, MLST)\r\n" +
        //            $" UPLOAD:   {FormatDirFlag(allowUpload)} (affects STOR, APPE)\r\n" +
        //            $" DOWNLOAD: {FormatDirFlag(allowDownload)} (affects RETR, SIZE, MDTM)\r\n" +
        //            "211 End\r\n",
        //            ct);
        //        return;
        //    }

        //    var tokens = flagsStr.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        //    foreach (var raw in tokens)
        //    {
        //        var t = raw.Trim();
        //        if (t.Length < 2)
        //            continue;

        //        var op = t[0];
        //        var name = t[1..].ToLowerInvariant();

        //        var value = op == '+';

        //        switch (name)
        //        {
        //            case "list":
        //            case "l":
        //                allowList = value;
        //                break;

        //            case "upload":
        //            case "up":
        //            case "u":
        //                allowUpload = value;
        //                break;

        //            case "download":
        //            case "down":
        //            case "d":
        //                allowDownload = value;
        //                break;
        //        }
        //    }

        //    var updated = new DirectoryRule(
        //        AllowUpload: allowUpload,
        //        AllowDownload: allowDownload,
        //        IsFree: rule.IsFree,
        //        MultiplyCost: rule.MultiplyCost,
        //        UploadBonus: rule.UploadBonus,
        //        Ratio: rule.Ratio,
        //        AllowList: allowList
        //    );

        //    _directoryRules[virtPath] = updated;

        //    _log.Log(
        //        FtpLogLevel.Info,
        //        $"SITE DIRFLAGS: {virtPath} LIST={FormatDirFlag(allowList)} UPLOAD={FormatDirFlag(allowUpload)} DOWNLOAD={FormatDirFlag(allowDownload)} set by {_s.Account?.UserName ?? "unknown"}");

        //    await _s.WriteAsync(
        //        $"200 Directory flags updated for {virtPath}: LIST={FormatDirFlag(allowList)} UPLOAD={FormatDirFlag(allowUpload)} DOWNLOAD={FormatDirFlag(allowDownload)}\r\n",
        //        ct);
        //}

        //internal async Task SITE_CHGRP(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE CHGRP requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync(
        //            "501 Usage: SITE CHGRP <user> <primary-group> [secondary-groups...]\r\n",
        //            ct);
        //        return;
        //    }

        //    var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        //    if (parts.Length < 2)
        //    {
        //        await _s.WriteAsync(
        //            "501 Usage: SITE CHGRP <user> <primary-group> [secondary-groups...]\r\n",
        //            ct);
        //        return;
        //    }

        //    var userName = parts[0];
        //    var primaryGroup = parts[1];
        //    var secondary = parts.Length > 2
        //        ? parts.Skip(2)
        //               .Where(p => !string.IsNullOrWhiteSpace(p))
        //               .Distinct(StringComparer.OrdinalIgnoreCase)
        //               .ToArray()
        //        : Array.Empty<string>();

        //    var user = _s.Users.FindUser(userName);
        //    if (user is null)
        //    {
        //        await _s.WriteAsync("550 User not found.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(primaryGroup))
        //    {
        //        await _s.WriteAsync("550 Primary group cannot be empty.\r\n", ct);
        //        return;
        //    }

        //    // Create updated user with new primary + secondary groups
        //    var updated = user with
        //    {
        //        PrimaryGroup = primaryGroup,
        //        SecondaryGroups = secondary.Length == 0
        //            ? ImmutableArray<string>.Empty
        //            : ImmutableArray.CreateRange(secondary)
        //    };

        //    if (_s.Users.TryUpdateUser(updated, out var error))
        //    {
        //        await _s.WriteAsync(
        //            $"200 Group(s) updated for {userName}. PRIMARY={primaryGroup} SECONDARY={string.Join(',', secondary)}\r\n",
        //            ct);
        //    }
        //    else
        //    {
        //        await _s.WriteAsync($"550 Failed to update user groups: {error}\r\n", ct);
        //    }
        //}

        //internal async Task SITE_NUKE(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE NUKE requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync(
        //            "501 Usage: SITE NUKE <path> <reason...>\r\n",
        //            ct);
        //        return;
        //    }

        //    var parts = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
        //    if (parts.Length < 2)
        //    {
        //        await _s.WriteAsync(
        //            "501 Usage: SITE NUKE <path> <reason...>\r\n",
        //            ct);
        //        return;
        //    }

        //    var pathArg = parts[0];
        //    var reason = parts[1];

        //    var virt = FtpPath.Normalize(_s.Cwd, pathArg);
        //    string phys;
        //    try
        //    {
        //        phys = _fs.MapToPhysical(virt);
        //    }
        //    catch
        //    {
        //        await _s.WriteAsync("550 Permission denied.\r\n", ct);
        //        return;
        //    }

        //    var isDir = Directory.Exists(phys);
        //    var isFile = File.Exists(phys);

        //    if (!isDir && !isFile)
        //    {
        //        await _s.WriteAsync("550 File or directory not found.\r\n", ct);
        //        return;
        //    }

        //    // Determine release path key for race/nuke credit logic
        //    string releaseVirt;
        //    if (isDir)
        //    {
        //        releaseVirt = virt;
        //    }
        //    else
        //    {
        //        var dirVirtRaw = Path.GetDirectoryName(virt);
        //        releaseVirt = string.IsNullOrEmpty(dirVirtRaw) || dirVirtRaw == "\\"
        //            ? "/"
        //            : dirVirtRaw.Replace('\\', '/');
        //    }

        //    // Try to resolve section for the release (used for ratio context)
        //    Config.Ftpd.FtpSection? section = null;
        //    try
        //    {
        //        section = GetSectionForVirtual(releaseVirt);
        //    }
        //    catch
        //    {
        //        // best-effort; section can be null
        //    }

        //    var nuker = _s.Account?.UserName ?? "unknown";
        //    var nukeMultiplier = 3.0; // classic 3x nuke penalty

        //    if (section?.NukeMultiplier is { } sectionMult && sectionMult > 0)
        //    {
        //        nukeMultiplier = sectionMult;
        //    }
        //    else if (_cfg.DefaultNukeMultiplier > 0)
        //    {
        //        nukeMultiplier = _cfg.DefaultNukeMultiplier;
        //    }

        //    // credit penalties, based on race info if present
        //    var penalties = new List<(string User, long Bytes, long PenaltyKb, long NewCredits)>();

        //    if (_raceEngine.TryGetRace(releaseVirt, out var race))
        //    {
        //        foreach (var kv in race.UserBytes)
        //        {
        //            var userName = kv.Key;
        //            var bytesUploaded = kv.Value;

        //            var user = _s.Users.FindUser(userName);
        //            if (user is null)
        //                continue;

        //            // resolve effective ratio rule for this path/user
        //            var rule = RatioEngine.ResolveRule(releaseVirt, user);
        //            var earnedKb = RatioEngine.ComputeUploadEarnedKb(bytesUploaded, rule, user);

        //            if (earnedKb <= 0)
        //                continue;

        //            var penaltyKb = (long)Math.Round(earnedKb * nukeMultiplier, MidpointRounding.AwayFromZero);
        //            var newCredits = Math.Max(0, user.CreditsKb - penaltyKb);

        //            var updated = user with { CreditsKb = newCredits };
        //            if (_s.Users.TryUpdateUser(updated, out _))
        //            {
        //                // update in-session account if we nuked ourselves
        //                if (_s.Account is { UserName: var un } && string.Equals(un, userName, StringComparison.OrdinalIgnoreCase))
        //                    _s.SetAccount(updated);

        //                penalties.Add((userName, bytesUploaded, penaltyKb, newCredits));
        //            }
        //        }
        //    }

        //    // Perform the actual physical NUKE (rename)
        //    try
        //    {
        //        var parent = Path.GetDirectoryName(phys) ?? phys;
        //        var name = Path.GetFileName(phys);
        //        var baseNukedName = name + ".NUKED";
        //        var target = Path.Combine(parent, baseNukedName);

        //        if (Directory.Exists(target) || File.Exists(target))
        //        {
        //            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        //            target = Path.Combine(parent, $"{name}.NUKED-{stamp}");
        //        }

        //        if (isDir)
        //        {
        //            Directory.Move(phys, target);
        //        }
        //        else
        //        {
        //            File.Move(phys, target);
        //        }

        //        // Log to main log
        //        _log.Log(
        //            FtpLogLevel.Warn,
        //            $"SITE NUKE by {nuker}: {virt} => {target} (Reason: {reason}, Penalties: {penalties.Count})");

        //        // Append to nukes.log (scene-style nuke log stub)
        //        try
        //        {
        //            Directory.CreateDirectory("logs");
        //            var sb = new StringBuilder();
        //            var now = DateTimeOffset.UtcNow;

        //            sb.Append(now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
        //              .Append(" | NUKE | ")
        //              .Append($"path={virt} | nuker={nuker} | reason={reason} | mult={nukeMultiplier}");

        //            if (race is not null)
        //            {
        //                sb.Append($" | totalBytes={race.TotalBytes} | files={race.FileCount}");
        //            }

        //            if (penalties.Count > 0)
        //            {
        //                sb.Append(" | penalties=");
        //                sb.Append(string.Join(";", penalties.Select(p =>
        //                    $"{p.User}:{p.Bytes}B:-{p.PenaltyKb}KB=>{p.NewCredits}KB")));
        //            }

        //            sb.AppendLine();

        //            File.AppendAllText("logs/nukes.log", sb.ToString());
        //        }
        //        catch
        //        {
        //            // logging failure shouldn't break NUKE
        //        }

        //        // AMScript notification hooks:
        //        FireSiteEvent("onNuke", releaseVirt, section, nuker);

        //        if (race is not null) FireSiteEvent("onRaceComplete", releaseVirt, section, nuker);

        //        await _s.WriteAsync(
        //            $"250 NUKE completed for {virt}. Reason: {reason}\r\n",
        //            ct);
        //    }
        //    catch (Exception ex)
        //    {
        //        _log.Log(
        //            FtpLogLevel.Error,
        //            $"SITE NUKE failed for {virt}: {ex.Message}");

        //        await _s.WriteAsync("550 NUKE failed.\r\n", ct);
        //    }
        //}

        //internal async Task SITE_WIPE(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE WIPE requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync(
        //            "501 Usage: SITE WIPE <path> [reason...]\r\n",
        //            ct);
        //        return;
        //    }

        //    var parts = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
        //    var pathArg = parts[0];
        //    var reason = parts.Length > 1 ? parts[1] : string.Empty;

        //    var virt = FtpPath.Normalize(_s.Cwd, pathArg);
        //    string phys;
        //    try
        //    {
        //        phys = _fs.MapToPhysical(virt);
        //    }
        //    catch
        //    {
        //        await _s.WriteAsync("550 Permission denied.\r\n", ct);
        //        return;
        //    }

        //    var isDir = Directory.Exists(phys);
        //    var isFile = File.Exists(phys);

        //    if (!isDir && !isFile)
        //    {
        //        await _s.WriteAsync("550 File or directory not found.\r\n", ct);
        //        return;
        //    }

        //    try
        //    {
        //        if (isDir)
        //        {
        //            Directory.Delete(phys, recursive: true);
        //        }
        //        else
        //        {
        //            File.Delete(phys);
        //        }

        //        _log.Log(
        //            FtpLogLevel.Info,
        //            $"SITE WIPE by {_s.Account?.UserName ?? "unknown"}: {virt} {(string.IsNullOrWhiteSpace(reason) ? "" : $"(Reason: {reason})")}");

        //        await _s.WriteAsync(
        //            $"250 WIPE completed for {virt}\r\n",
        //            ct);
        //    }
        //    catch (Exception ex)
        //    {
        //        _log.Log(
        //            FtpLogLevel.Error,
        //            $"SITE WIPE failed for {virt}: {ex.Message}");

        //        await _s.WriteAsync("550 WIPE failed.\r\n", ct);
        //    }
        //}

        //internal async Task SITE_MOVE(string arg, CancellationToken ct)
        //{
        //    if (_s.Account is not { IsAdmin: true })
        //    {
        //        await _s.WriteAsync("550 SITE MOVE requires admin privileges.\r\n", ct);
        //        return;
        //    }

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync(
        //            "501 Usage: SITE MOVE <src> <dst>\r\n",
        //            ct);
        //        return;
        //    }

        //    // NOTE: for now, paths cannot contain spaces.
        //    var parts = arg.Split(' ', 3, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        //    if (parts.Length < 2)
        //    {
        //        await _s.WriteAsync(
        //            "501 Usage: SITE MOVE <src> <dst>\r\n",
        //            ct);
        //        return;
        //    }

        //    var srcArg = parts[0];
        //    var dstArg = parts[1];

        //    var srcVirt = FtpPath.Normalize(_s.Cwd, srcArg);
        //    var dstVirt = FtpPath.Normalize(_s.Cwd, dstArg);

        //    string srcPhys;
        //    string dstPhys;
        //    try
        //    {
        //        srcPhys = _fs.MapToPhysical(srcVirt);
        //        dstPhys = _fs.MapToPhysical(dstVirt);
        //    }
        //    catch
        //    {
        //        await _s.WriteAsync("550 Permission denied.\r\n", ct);
        //        return;
        //    }

        //    var isDir = Directory.Exists(srcPhys);
        //    var isFile = File.Exists(srcPhys);

        //    if (!isDir && !isFile)
        //    {
        //        await _s.WriteAsync("550 Source file or directory not found.\r\n", ct);
        //        return;
        //    }

        //    // Basic safety: do not overwrite existing target.
        //    if (Directory.Exists(dstPhys) || File.Exists(dstPhys))
        //    {
        //        await _s.WriteAsync("550 Destination already exists.\r\n", ct);
        //        return;
        //    }

        //    try
        //    {
        //        var dstParent = Path.GetDirectoryName(dstPhys);
        //        if (!string.IsNullOrEmpty(dstParent) && !Directory.Exists(dstParent))
        //        {
        //            await _s.WriteAsync("550 Destination parent directory does not exist.\r\n", ct);
        //            return;
        //        }

        //        if (isDir)
        //        {
        //            Directory.Move(srcPhys, dstPhys);
        //        }
        //        else
        //        {
        //            File.Move(srcPhys, dstPhys);
        //        }

        //        _log.Log(
        //            FtpLogLevel.Info,
        //            $"SITE MOVE by {_s.Account?.UserName ?? "unknown"}: {srcVirt} -> {dstVirt}");

        //        await _s.WriteAsync(
        //            $"250 MOVE completed: {srcVirt} -> {dstVirt}\r\n",
        //            ct);
        //    }
        //    catch (Exception ex)
        //    {
        //        _log.Log(
        //            FtpLogLevel.Error,
        //            $"SITE MOVE failed {srcVirt} -> {dstVirt}: {ex.Message}");

        //        await _s.WriteAsync("550 MOVE failed.\r\n", ct);
        //    }
        //}

        //internal async Task SITE_RACE(string arg, CancellationToken ct)
        //{
        //    // RACE is read-only; usually visible to all logged-in users, no admin check.

        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync(
        //            "501 Usage: SITE RACE <path>\r\n",
        //            ct);
        //        return;
        //    }

        //    var releaseVirt = FtpPath.Normalize(_s.Cwd, arg);

        //    if (!_raceEngine.TryGetRace(releaseVirt, out var race))
        //    {
        //        await _s.WriteAsync("550 No race information for this path.\r\n", ct);
        //        return;
        //    }

        //    var totalBytes = race.TotalBytes <= 0 ? 1 : race.TotalBytes; // avoid div-by-zero
        //    var ordered = race.UserBytes
        //        .OrderByDescending(kv => kv.Value)
        //        .ToList();

        //    await _s.WriteAsync(
        //        $"211-RACE {race.ReleasePath} (section: {race.SectionName})\r\n",
        //        ct);
        //    await _s.WriteAsync("211-User             MB        %\r\n", ct);
        //    await _s.WriteAsync("211-------------------------------\r\n", ct);

        //    foreach (var kv in ordered)
        //    {
        //        var user = kv.Key;
        //        var bytes = kv.Value;
        //        var mb = bytes / (1024.0 * 1024.0);
        //        var pct = (double)bytes * 100.0 / totalBytes;

        //        await _s.WriteAsync(
        //            $"211- {user,-12} {mb,8:0.00} {pct,6:0.0}\r\n",
        //            ct);
        //    }

        //    await _s.WriteAsync("211 End\r\n", ct);
        //}

        //internal async Task SITE_RACESTATS(string arg, CancellationToken ct)
        //{
        //    if (string.IsNullOrWhiteSpace(arg))
        //    {
        //        await _s.WriteAsync(
        //            "501 Usage: SITE RACESTATS <path>\r\n",
        //            ct);
        //        return;
        //    }

        //    var releaseVirt = FtpPath.Normalize(_s.Cwd, arg);

        //    if (!_raceEngine.TryGetRace(releaseVirt, out var race))
        //    {
        //        await _s.WriteAsync("550 No race information for this path.\r\n", ct);
        //        return;
        //    }

        //    var totalBytes = race.TotalBytes;
        //    var totalMb = totalBytes / (1024.0 * 1024.0);
        //    var ordered = race.UserBytes
        //        .OrderByDescending(kv => kv.Value)
        //        .ToList();

        //    await _s.WriteAsync(
        //        $"211-RACESTATS {race.ReleasePath}\r\n",
        //        ct);
        //    await _s.WriteAsync($"211- Section:       {race.SectionName}\r\n", ct);
        //    await _s.WriteAsync($"211- Started:       {race.StartedAt:yyyy-MM-dd HH:mm:ss zzz}\r\n", ct);
        //    await _s.WriteAsync($"211- Last Update:   {race.LastUpdatedAt:yyyy-MM-dd HH:mm:ss zzz}\r\n", ct);
        //    await _s.WriteAsync($"211- Files:         {race.FileCount}\r\n", ct);
        //    await _s.WriteAsync($"211- Total Bytes:   {totalBytes}\r\n", ct);
        //    await _s.WriteAsync($"211- Total MB:      {totalMb:0.00}\r\n", ct);
        //    await _s.WriteAsync("211-\r\n", ct);
        //    await _s.WriteAsync("211-User             MB        %\r\n", ct);
        //    await _s.WriteAsync("211-------------------------------\r\n", ct);

        //    var denom = totalBytes <= 0 ? 1 : totalBytes;

        //    foreach (var kv in ordered)
        //    {
        //        var user = kv.Key;
        //        var bytes = kv.Value;
        //        var mb = bytes / (1024.0 * 1024.0);
        //        var pct = (double)bytes * 100.0 / denom;

        //        await _s.WriteAsync(
        //            $"211- {user,-12} {mb,8:0.00} {pct,6:0.0}\r\n",
        //            ct);
        //    }

        //    await _s.WriteAsync("211 End\r\n", ct);
        //}

        //internal async Task SITE_LASTRACES(string arg, CancellationToken ct)
        //{
        //    var max = 10;
        //    if (!string.IsNullOrWhiteSpace(arg) &&
        //        int.TryParse(arg.Trim(), out var parsed) &&
        //        parsed > 0 && parsed <= 50)
        //    {
        //        max = parsed;
        //    }

        //    var races = _raceEngine.GetRecentRaces(max);
        //    if (races.Count == 0)
        //    {
        //        await _s.WriteAsync("211 No races recorded yet.\r\n", ct);
        //        return;
        //    }

        //    await _s.WriteAsync("211-Last races:\r\n", ct);
        //    await _s.WriteAsync("211-#  Section  Files  MB      LastUpdate           Path\r\n", ct);
        //    await _s.WriteAsync("211--------------------------------------------------------------\r\n", ct);

        //    var idx = 1;
        //    foreach (var race in races)
        //    {
        //        var mb = race.TotalBytes / (1024.0 * 1024.0);
        //        await _s.WriteAsync(
        //            $"211- {idx,2} {race.SectionName,-8} {race.FileCount,5} {mb,7:0.00} {race.LastUpdatedAt:yyyy-MM-dd HH:mm} {race.ReleasePath}\r\n",
        //            ct);
        //        idx++;
        //    }

        //    await _s.WriteAsync("211 End\r\n", ct);
        //}

        //internal async Task SITE_RACELOG(string arg, CancellationToken ct)
        //{
        //    var max = 10;
        //    if (!string.IsNullOrWhiteSpace(arg) &&
        //        int.TryParse(arg.Trim(), out var parsed) &&
        //        parsed > 0 && parsed <= 50)
        //    {
        //        max = parsed;
        //    }

        //    var races = _raceEngine.GetRecentRaces(max);
        //    if (races.Count == 0)
        //    {
        //        await _s.WriteAsync("211 No races recorded yet.\r\n", ct);
        //        return;
        //    }

        //    await _s.WriteAsync("211-RACELOG (most recent first)\r\n", ct);

        //    foreach (var race in races)
        //    {
        //        var mb = race.TotalBytes / (1024.0 * 1024.0);

        //        await _s.WriteAsync(
        //            $"211- Path:    {race.ReleasePath}\r\n" +
        //            $"211- Section: {race.SectionName}\r\n" +
        //            $"211- Started: {race.StartedAt:yyyy-MM-dd HH:mm:ss zzz}\r\n" +
        //            $"211- Last:    {race.LastUpdatedAt:yyyy-MM-dd HH:mm:ss zzz}\r\n" +
        //            $"211- Files:   {race.FileCount}\r\n" +
        //            $"211- MB:      {mb:0.00}\r\n" +
        //            "211-\r\n",
        //            ct);
        //    }

        //    await _s.WriteAsync("211 End\r\n", ct);
        //}

        #endregion

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
