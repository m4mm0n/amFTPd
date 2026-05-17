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


using System;
using System.Net;
using System.Reflection;
using System.Text;
using amFTPd.Config.Ftpd;
using amFTPd.Core.Dupe;
using amFTPd.Core.Events;
using amFTPd.Core.Fxp;
using amFTPd.Core.Ident;
using amFTPd.Core.Messages;
using amFTPd.Core.Site;
using amFTPd.Core.Stats.Live;
using amFTPd.Core.Vfs;
using amFTPd.Core.Zipscript;
using amFTPd.Logging;
using amFTPd.Scripting;
using amFTPd.Security;
using amFTPd.Utils.Cryptography;
using RatioLoginContext = amFTPd.Core.RatioLoginContext;

namespace amFTPd.Core
{
    /// <summary>
    /// Partial class for handling FTP commands within the FtpCommandRouter.
    /// </summary>
    public sealed partial class FtpCommandRouter
    {
        #region AMScript Context Builders
        private static string FtpErrorReply(string? message, string fallback)
        {
            var reply = string.IsNullOrWhiteSpace(message) ? fallback : message.TrimEnd();
            if (reply.Length < 3 || !char.IsDigit(reply[0]) || !char.IsDigit(reply[1]) || !char.IsDigit(reply[2]))
            {
                reply = "550 " + reply.TrimStart();
            }

            return reply.EndsWith("\r\n", StringComparison.Ordinal) ? reply : reply + "\r\n";
        }

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
                EarnedUpload: kb,    // default earn = 1:1
                IsAdmin: account.IsAdmin || account.IsSiteop
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
                Event: command.ToUpperInvariant(),   // "PASV", "EPSV", "PORT", "EPRT"
                IsAdmin: account is not null && (account.IsAdmin || account.IsSiteop)
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
                Event: "ROUTE",
                IsAdmin: account.IsAdmin || account.IsSiteop
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
                Event: $"SITE {command.ToUpperInvariant()}",
                IsAdmin: acc.IsAdmin || acc.IsSiteop,
                Arg: args
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
                Event: evt,
                IsAdmin: acc.IsAdmin || acc.IsSiteop,
                IsSiteop: acc.IsSiteop,
                IsTls: _s.TlsActive,
                Arg: args ?? ""
            );
        }

        private async Task SendDenyAsync(AMScriptResult res, string command, CancellationToken ct)
        {
            var msg = res.DenyReason ?? $"550 {command} denied by policy.";
            if (msg.Length < 3 || !char.IsDigit(msg[0]) || !char.IsDigit(msg[1]) || !char.IsDigit(msg[2]))
            {
                msg = "550 " + msg;
            }
            await _s.WriteAsync(msg, ct);
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

            var builtInAuthOk = _s.Users.TryAuthenticate(username, arg, out var account, out var authDenyReason)
                                && account is not null;

            if (!builtInAuthOk)
            {
                var isConcurrentLimit = authDenyReason is not null &&
                    authDenyReason.Contains("Max connections", StringComparison.OrdinalIgnoreCase);

                // ── Plugin auth fallback ─────────────────────────────────────────
                // Only attempt if it's a real auth failure (wrong password / unknown
                // user), not a policy denial like the concurrent-connection limit.
                if (!isConcurrentLimit && _runtime.PluginHost is { } pluginHost)
                {
                    var remoteIp = _s.RemoteEndPoint?.Address?.ToString() ?? string.Empty;
                    var pluginResult = await pluginHost.TryAuthenticateAsync(
                        username, arg, remoteIp, ct).ConfigureAwait(false);

                    if (pluginResult is not null)
                    {
                        if (pluginResult.Outcome == amFTPd.Plugin.Abstractions.PluginAuthOutcome.Authenticated)
                        {
                            // Plugin validated the credentials. Look up the user account so that
                            // all subsequent login checks (disabled, ratio, ident, etc.) still run.
                            account = _s.Users.FindUser(username);

                            if (account is not null)
                            {
                                // Bypass further auth-failure handling; fall through to post-auth checks.
                                goto PostAuth;
                            }

                            // Plugin authenticated but user doesn't exist in the store — deny.
                            _log.Log(FtpLogLevel.Warn,
                                $"[Plugin] Auth plugin authenticated '{username}' but user is not in the user store.");
                            await _s.WriteAsync("530 Login incorrect.\r\n", ct);
                            return;
                        }

                        if (pluginResult.Outcome == amFTPd.Plugin.Abstractions.PluginAuthOutcome.Rejected)
                        {
                            _s.NotifyLoginFailed();
                            if (_s.RemoteEndPoint?.Address is IPAddress rejIp)
                                _server.NotifyFailedLogin(rejIp);

                            await _s.WriteAsync(
                                pluginResult.RejectReason ?? "530 Login rejected.\r\n", ct);
                            return;
                        }
                        // Passthrough — fall through to built-in deny below.
                    }
                }
                // ── End plugin auth fallback ─────────────────────────────────────

                // Only count against the hammer/IP-reputation for actual bad passwords,
                // not for exceeding the concurrent login limit.
                if (!isConcurrentLimit)
                {
                    _s.NotifyLoginFailed();

                    if (_s.RemoteEndPoint?.Address is IPAddress ip)
                    {
                        _server.NotifyFailedLogin(ip);
                    }
                }

                await _s.WriteAsync(authDenyReason ?? "530 Login incorrect.\r\n", ct);
                return;
            }

        PostAuth:
            // Reachable via fall-through (built-in auth) or goto (plugin auth).
            // In both cases account is non-null, but add a guard for the compiler.
            if (account is null)
            {
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
                    Event: "LOGIN",
                    IsAdmin: account.IsAdmin || account.IsSiteop
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
                    Event: "LOGIN",
                    IsAdmin: account.IsAdmin || account.IsSiteop
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

            // ------------------------------------------------------------------
            // MOTD — send after successful login if a motd.txt exists.
            // ------------------------------------------------------------------
            var motdEngine = _runtime.Messages;
            if (motdEngine is not null)
            {
                var activeSessions = FtpSession.GetActiveSessions().Count;
                var motd = motdEngine.RenderMotd(account, activeSessions);
                if (!string.IsNullOrWhiteSpace(motd))
                {
                    var motdMsg = MessageEngine.FormatAsMultiLine("230", motd);
                    await _s.WriteAsync(motdMsg, ct);
                }
            }
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
                _log.Log(FtpLogLevel.Debug, $"[AMScript] Evaluating CWD for {ctx.UserName}");
                var res = _userScript.EvaluateUser(ctx);
                _log.Log(FtpLogLevel.Debug, $"[AMScript] CWD result: {res.Action} {res.DenyReason}");
                if (res.Action == AMRuleAction.Deny)
                {
                    await SendDenyAsync(res, "CWD", ct);
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

                // Per-directory .message file
                var dirMsgEngine = _runtime.Messages;
                if (dirMsgEngine is not null)
                {
                    var activeSessions = FtpSession.GetActiveSessions().Count;
                    var dirMsg = dirMsgEngine.RenderDirMessage(phys, _s.Account, activeSessions);
                    if (!string.IsNullOrWhiteSpace(dirMsg))
                    {
                        var formatted = MessageEngine.FormatAsMultiLine("250", dirMsg);
                        await _s.WriteAsync(formatted, ct);
                    }
                }
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

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var parts = arg.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 6)
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            if (!int.TryParse(parts[0], out var h1) || h1 is < 0 or > 255 ||
                !int.TryParse(parts[1], out var h2) || h2 is < 0 or > 255 ||
                !int.TryParse(parts[2], out var h3) || h3 is < 0 or > 255 ||
                !int.TryParse(parts[3], out var h4) || h4 is < 0 or > 255)
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            if (!int.TryParse(parts[4], out var portHi) || portHi is < 0 or > 255 ||
                !int.TryParse(parts[5], out var portLo) || portLo is < 0 or > 255)
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var port = (portHi << 8) + portLo;
            if (port is < 1 or > 65535)
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var requestedIp = new IPAddress(new[] { (byte)h1, (byte)h2, (byte)h3, (byte)h4 });
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

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            if (arg.Length < 5 || arg[0] != '|')
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var eprtParts = arg.Split('|');
            if (eprtParts.Length != 5 ||
                eprtParts[0].Length != 0 ||
                eprtParts[4].Length != 0)
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            if (!int.TryParse(eprtParts[1], out var proto) || proto < 1 || proto > 2)
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var addrText = eprtParts[2];
            if (!IPAddress.TryParse(addrText, out var ip))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            if (proto == 1 && ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
                proto == 2 && ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            if (!int.TryParse(eprtParts[3], out var port) ||
                port is < 1 or > 65535)
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

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

            if (_userScript is not null)
            {
                var ctx = BuildUserContext("LIST", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    await SendDenyAsync(res, "LIST", ct);
                    return;
                }
            }

            var target = string.IsNullOrWhiteSpace(arg) ? "." : arg;
            var absTarget = FtpPath.Normalize(_s.Cwd, target);

            var vfsResult = _s.VfsManager?.Resolve(absTarget, _s.Account);
            if (vfsResult is not { Success: true, Node: not null })
            {
                await _s.WriteAsync(FtpErrorReply(vfsResult?.ErrorMessage, "550 Not found.\r\n"), ct);
                return;
            }

            var node = vfsResult.Node;

            if (!_directoryAccess.Evaluate(node.VirtualPath).CanList)
            {
                await _s.WriteAsync("550 Listing not allowed in this directory.\r\n", ct);
                return;
            }

            await _s.WriteAsync(FtpResponses.FileOk, ct);

            var transferOk = await _s.WithDataAsync(async stream =>
            {
                await using var wr = new StreamWriter(
                    stream,
                    new UTF8Encoding(false),
                    64 * 1024,
                    leaveOpen: true);

                static string VirtualDirLine(string virtualPath)
                {
                    var name = Path.GetFileName(virtualPath.TrimEnd('/'));
                    if (string.IsNullOrEmpty(name)) name = "/";
                    return $"drwxr-xr-x 1 owner group 0 Jan 01 00:00 {name}";
                }

                static string VirtualFileLine(string virtualPath, string? content)
                {
                    var name = Path.GetFileName(virtualPath);
                    var size = content is null ? 0 : Encoding.UTF8.GetByteCount(content);
                    return $"-rw-r--r-- 1 owner group {size} Jan 01 00:00 {name}";
                }

                switch (node.Type)
                {
                    case VfsNodeType.PhysicalDirectory:
                        foreach (var dir in Directory.EnumerateDirectories(node.PhysicalPath!))
                            await wr.WriteLineAsync(_fs.ToUnixListLine(new DirectoryInfo(dir)));

                        foreach (var file in Directory.EnumerateFiles(node.PhysicalPath!))
                            await wr.WriteLineAsync(_fs.ToUnixListLine(new FileInfo(file)));
                        break;

                    case VfsNodeType.PhysicalFile:
                        await wr.WriteLineAsync(
                            _fs.ToUnixListLine((FileInfo)node.FileSystemInfo!));
                        break;

                    case VfsNodeType.VirtualDirectory:
                        {
                            // If the provider supports enumeration, list children; otherwise show a single entry.
                            var children = _s.VfsManager?.Enumerate(node.VirtualPath, _s.Account)
                                ?.ToList() ?? [];

                            if (children.Count == 0)
                            {
                                await wr.WriteLineAsync(VirtualDirLine(node.VirtualPath));
                                break;
                            }

                            foreach (var child in children)
                            {
                                switch (child.Type)
                                {
                                    case VfsNodeType.PhysicalDirectory:
                                    case VfsNodeType.PhysicalFile:
                                        if (child.FileSystemInfo is not null)
                                            await wr.WriteLineAsync(_fs.ToUnixListLine(child.FileSystemInfo));
                                        break;

                                    case VfsNodeType.VirtualDirectory:
                                        await wr.WriteLineAsync(VirtualDirLine(child.VirtualPath));
                                        break;

                                    case VfsNodeType.VirtualFile:
                                        await wr.WriteLineAsync(VirtualFileLine(child.VirtualPath, child.VirtualContent));
                                        break;
                                }
                            }
                            break;
                        }

                    case VfsNodeType.VirtualFile:
                        await wr.WriteLineAsync(VirtualFileLine(node.VirtualPath, node.VirtualContent));
                        break;
                }

                // REQUIRED
                return 0L;
            }, isUpload: false, countBandwidth: false, ct);

            if (transferOk)
                await _s.WriteAsync(FtpResponses.ClosingData, ct);
        }

        private async Task NLST(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            if (_userScript is not null)
            {
                var ctx = BuildUserContext("NLST", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    await SendDenyAsync(res, "NLST", ct);
                    return;
                }
            }

            var target = string.IsNullOrWhiteSpace(arg) ? "." : arg;
            var absTarget = FtpPath.Normalize(_s.Cwd, target);

            var vfsResult = _s.VfsManager?.Resolve(absTarget, _s.Account);
            if (vfsResult is not { Success: true, Node: not null })
            {
                await _s.WriteAsync(FtpErrorReply(vfsResult?.ErrorMessage, "550 Not found.\r\n"), ct);
                return;
            }

            var node = vfsResult.Node;

            if (!_directoryAccess.Evaluate(node.VirtualPath).CanList)
            {
                await _s.WriteAsync("550 Listing not allowed in this directory.\r\n", ct);
                return;
            }

            await _s.WriteAsync(FtpResponses.FileOk, ct);

            var transferOk = await _s.WithDataAsync(async stream =>
            {
                await using var wr = new StreamWriter(
                    stream,
                    new UTF8Encoding(false),
                    64 * 1024,
                    leaveOpen: true);

                switch (node.Type)
                {
                    case VfsNodeType.PhysicalDirectory:
                        foreach (var dir in Directory.EnumerateDirectories(node.PhysicalPath!))
                            await wr.WriteLineAsync(Path.GetFileName(dir));

                        foreach (var file in Directory.EnumerateFiles(node.PhysicalPath!))
                            await wr.WriteLineAsync(Path.GetFileName(file));
                        break;

                    case VfsNodeType.VirtualDirectory:
                        {
                            var children = _s.VfsManager?.Enumerate(node.VirtualPath, _s.Account)
                                ?.ToList() ?? [];

                            if (children.Count == 0)
                                break;

                            foreach (var child in children)
                            {
                                var name = Path.GetFileName(child.VirtualPath.TrimEnd('/'));
                                if (!string.IsNullOrWhiteSpace(name))
                                    await wr.WriteLineAsync(name);
                            }

                            break;
                        }

                    default:
                        // Single file
                        var singleName = Path.GetFileName(node.VirtualPath.TrimEnd('/'));
                        if (string.IsNullOrEmpty(singleName)) singleName = "/";
                        await wr.WriteLineAsync(singleName);
                        break;
                }

                return 0L;
            }, isUpload: false, countBandwidth: false, ct);

            if (transferOk)
                await _s.WriteAsync(FtpResponses.ClosingData, ct);
        }

        private async Task MLSD(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            if (_userScript is not null)
            {
                var ctx = BuildUserContext("MLSD", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    await SendDenyAsync(res, "MLSD", ct);
                    return;
                }
            }

            var target = string.IsNullOrWhiteSpace(arg) ? "." : arg;
            var absTarget = FtpPath.Normalize(_s.Cwd, target);

            var vfsResult = _s.VfsManager?.Resolve(absTarget, _s.Account);
            if (vfsResult is not { Success: true, Node: not null })
            {
                await _s.WriteAsync(FtpErrorReply(vfsResult?.ErrorMessage, "550 MLSD failed.\r\n"), ct);
                return;
            }

            var node = vfsResult.Node;

            if (!_directoryAccess.Evaluate(node.VirtualPath).CanList)
            {
                await _s.WriteAsync("550 Listing not allowed in this directory.\r\n", ct);
                return;
            }

            await _s.WriteAsync(FtpResponses.FileOk, ct);

            var transferOk = await _s.WithDataAsync(async stream =>
            {
                await using var wr = new StreamWriter(
                    stream,
                    new UTF8Encoding(false),
                    64 * 1024,
                    leaveOpen: true);

                switch (node.Type)
                {
                    case VfsNodeType.PhysicalDirectory:
                        foreach (var dir in Directory.EnumerateDirectories(node.PhysicalPath!))
                            await wr.WriteLineAsync(_fs.ToMlsdLine(new DirectoryInfo(dir)));

                        foreach (var file in Directory.EnumerateFiles(node.PhysicalPath!))
                            await wr.WriteLineAsync(_fs.ToMlsdLine(new FileInfo(file)));
                        break;

                    case VfsNodeType.PhysicalFile:
                        await wr.WriteLineAsync(
                            _fs.ToMlsdLine((FileInfo)node.FileSystemInfo!));
                        break;

                    case VfsNodeType.VirtualDirectory:
                        {
                            var children = _s.VfsManager?.Enumerate(node.VirtualPath, _s.Account)
                                ?.ToList() ?? [];

                            foreach (var child in children)
                            {
                                var name = Path.GetFileName(child.VirtualPath.TrimEnd('/'));
                                if (string.IsNullOrEmpty(name))
                                    continue;

                                switch (child.Type)
                                {
                                    case VfsNodeType.PhysicalDirectory:
                                    case VfsNodeType.PhysicalFile:
                                        if (child.FileSystemInfo is not null)
                                            await wr.WriteLineAsync(_fs.ToMlsdLine(child.FileSystemInfo));
                                        break;

                                    case VfsNodeType.VirtualDirectory:
                                        await wr.WriteLineAsync($"type=dir;perm=el; {name}");
                                        break;

                                    case VfsNodeType.VirtualFile:
                                        var size = child.VirtualContent is null
                                            ? 0
                                            : Encoding.UTF8.GetByteCount(child.VirtualContent);
                                        await wr.WriteLineAsync($"type=file;size={size};perm=rl; {name}");
                                        break;
                                }
                            }
                            break;
                        }

                    case VfsNodeType.VirtualFile:
                        {
                            var name = Path.GetFileName(node.VirtualPath);
                            var size = node.VirtualContent is null
                                ? 0
                                : Encoding.UTF8.GetByteCount(node.VirtualContent);
                            await wr.WriteLineAsync($"type=file;size={size};perm=rl; {name}");
                            break;
                        }
                }

                return 0L;
            }, isUpload: false, countBandwidth: false, ct);

            if (transferOk)
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
                    await SendDenyAsync(res, "LIST", ct);
                    return;
                }
            }

            var target = string.IsNullOrWhiteSpace(arg) ? "." : arg;

            var vfsResult = _s.VfsManager?.Resolve(target, _s.Account);
            if (vfsResult != null && (!vfsResult.Success || vfsResult.Node is null))
            {
                await _s.WriteAsync(FtpErrorReply(vfsResult.ErrorMessage, "550 MLST failed.\r\n"), ct);
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

            if (_userScript is not null)
            {
                var ctx = BuildUserContext("RETR", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    await SendDenyAsync(res, "LIST", ct);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var vfsResult = _s.VfsManager?.Resolve(arg, _s.Account);
            if (vfsResult is not { Success: true, Node: not null })
            {
                await _s.WriteAsync(FtpErrorReply(vfsResult?.ErrorMessage, "550 File not found.\r\n"), ct);
                return;
            }

            var node = vfsResult.Node;
            if (node.Type is not (VfsNodeType.PhysicalFile or VfsNodeType.VirtualFile))
            {
                await _s.WriteAsync("550 File not found.\r\n", ct);
                return;
            }

            var virtTarget = node.VirtualPath;
            if (!_directoryAccess.Evaluate(virtTarget).CanDownload)
            {
                await _s.WriteAsync("550 Download not allowed in this directory.\r\n", ct);
                return;
            }

            Stream sourceStream;
            long length;

            try
            {
                if (node.Type == VfsNodeType.PhysicalFile)
                {
                    var fi = new FileInfo(node.PhysicalPath!);
                    length = fi.Length;
                    sourceStream = fi.OpenRead();
                }
                else
                {
                    var buf = Encoding.UTF8.GetBytes(node.VirtualContent ?? "");
                    length = buf.LongLength;
                    sourceStream = new MemoryStream(buf, false);
                }
            }
            catch
            {
                await _s.WriteAsync("550 File not accessible.\r\n", ct);
                return;
            }

            var rest = _s.RestOffset;
            if (rest is > 0 && rest < length)
                length -= rest.Value;

            var section = GetSectionForVirtual(virtTarget);
            if (Server!.IsSectionBlocked(section.Name, out var reason))
            {
                await _s.WriteAsync(
                    $"550 Section temporarily unavailable: {reason}\r\n",
                    ct);
                await sourceStream.DisposeAsync();
                return;
            }

            if (_runtime.CreditEngine is not null &&
                !await CheckDownloadCreditsAsync(virtTarget, section, length, ct))
            {
                await sourceStream.DisposeAsync();
                return;
            }

            await _s.WriteAsync(FtpResponses.FileOk, ct);
            _s.ClearRestOffset();

            _s.BeginTransfer(Path.GetFileName(node.PhysicalPath ?? virtTarget), isUpload: false, length);
            try
            {
                var transferOk = await _s.WithDataAsync(async data =>
                {
                    await using (sourceStream)
                    {
                        if (rest is > 0 && sourceStream.CanSeek)
                            sourceStream.Seek(rest.Value, SeekOrigin.Begin);

                        var transferred = await CopyWithThrottleAsync(
                            sourceStream,
                            data,
                            ResolveEffectiveSpeedKbps(isDownload: true, section, virtTarget),
                            isDownload: true,
                            ct,
                            _s.AddTransferBytes);

                        if (transferred <= 0)
                            return 0L;

                        if (section is not null)
                        {
                            Server.NotifySectionBandwidth(
                                section.Name,
                                transferred,
                                isUpload: false);
                        }

                        // ---- Credits -------------------------------------------------
                        _runtime.CreditEngine?.TryConsumeCredits(
                            _s.Account,
                            section?.Name ?? "",
                            transferred,
                            out _);

                        // ---- Rolling stats ------------------------------------------
                        var rs = _runtime.RollingStats;
                        rs.DownloadBytes5s.Add(transferred);
                        rs.DownloadBytes1m.Add(transferred);
                        rs.DownloadBytes5m.Add(transferred);
                        rs.Transfers5s.Add(1);
                        rs.Transfers1m.Add(1);
                        rs.Transfers5m.Add(1);

                        // ---- Live user stats ----------------------------------------
                        var live = _runtime.LiveStats;

                        var user = live.Users.GetOrAdd(
                            _s.Account.UserName,
                            _ => new UserLiveStats { UserName = _s.Account.UserName });

                        Interlocked.Increment(ref user.Downloads);
                        Interlocked.Add(ref user.BytesDownloaded, transferred);

                        // ---- Live section stats -------------------------------------
                        if (section is not null)
                        {
                            var sec = live.Sections.GetOrAdd(
                                section.Name,
                                _ => new SectionLiveStats { SectionName = section.Name });

                            Interlocked.Increment(ref sec.Downloads);
                            Interlocked.Add(ref sec.BytesDownloaded, transferred);

                            lock (_sectionsTouched)
                            {
                                if (_sectionsTouched.Add(section.Name))
                                    Interlocked.Increment(ref sec.ActiveUsers);
                            }
                        }

                        FireSiteEvent("onDownload", virtTarget, section, _s.Account.UserName);

                        // Publish Download event to EventBus (for session log, xferlog, IRC, etc.)
                        _runtime.EventBus?.Publish(new FtpEvent
                        {
                            Type = FtpEventType.Download,
                            Timestamp = DateTimeOffset.UtcNow,
                            SessionId = _s.SessionId,
                            User = _s.Account.UserName,
                            Group = _s.Account.GroupName,
                            Section = section?.Name,
                            VirtualPath = virtTarget,
                            Bytes = transferred,
                            RemoteHost = _s.RemoteEndPoint?.Address.ToString()
                        });

                        return transferred;
                    }
                }, isUpload: false, countBandwidth: true, ct);

                if (transferOk)
                    await _s.WriteAsync(FtpResponses.ClosingData, ct);
            }
            finally { _s.EndTransfer(); }
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

            if (_userScript is not null)
            {
                var ctx = BuildUserContext("STOR", arg);
                var res = _userScript.EvaluateUser(ctx);
                if (res.Action == AMRuleAction.Deny)
                {
                    await SendDenyAsync(res, "LIST", ct);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var virtTarget = FtpPath.Normalize(_s.Cwd, arg);
            var dirVirt = Path.GetDirectoryName(virtTarget)?.Replace('\\', '/') ?? "/";
            var fileName = Path.GetFileName(virtTarget);

            if (!_directoryAccess.Evaluate(dirVirt).CanUpload)
            {
                await _s.WriteAsync("550 Upload not allowed in this directory.\r\n", ct);
                return;
            }

            string physDir;
            try
            {
                var dirResult = _s.VfsManager?.Resolve(dirVirt, _s.Account);
                physDir = dirResult?.Node?.PhysicalPath
                          ?? Path.GetDirectoryName(_fs.MapToPhysical(virtTarget))!;
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            var phys = Path.Combine(physDir, fileName);
            Directory.CreateDirectory(physDir);

            var section = GetSectionForVirtual(virtTarget);
            if (Server.IsSectionBlocked(section.Name, out var reason))
            {
                await _s.WriteAsync(
                    $"550 Section temporarily unavailable: {reason}\r\n",
                    ct);
                return;
            }

            // ------------------------------------------------------------------
            // Upload quota check
            // ------------------------------------------------------------------
            if (_runtime.UploadQuota is { } quotaStore && _s.Account is not null)
            {
                var quotaResult = quotaStore.Check(_s.Account, _runtime.GetGroupsSnapshot());
                if (!quotaResult.Allowed)
                {
                    await _s.WriteAsync(
                        $"553 Upload denied: {quotaResult.DeniedReason}\r\n",
                        ct);
                    return;
                }
            }

            // ------------------------------------------------------------------
            // SFV-FIRST enforcement: reject non-.sfv uploads until an .sfv
            // is present in the release directory.
            // ------------------------------------------------------------------
            if (section.RequireSfvFirst &&
                !fileName.EndsWith(".sfv", StringComparison.OrdinalIgnoreCase))
            {
                var hasSfv = Directory.Exists(physDir) &&
                    Directory.EnumerateFiles(physDir, "*.sfv", SearchOption.TopDirectoryOnly).Any();
                if (!hasSfv)
                {
                    await _s.WriteAsync(
                        $"550 SFV-first enforced: upload the .sfv file before uploading '{fileName}'.\r\n",
                        ct);
                    return;
                }
            }

            // Read REST offset early so we can validate before opening the data connection.
            var rest = _s.RestOffset;
            _s.ClearRestOffset();

            // ------------------------------------------------------------------
            // RESUME INTEGRITY CHECK: if a REST offset is given and the section
            // requires it, verify the partial file on disk is exactly that many
            // bytes before accepting the resume.  A size mismatch means either
            // the partial file is corrupt or the client has the wrong offset.
            // ------------------------------------------------------------------
            if (rest is > 0 && section is not null && section.RequireResumeIntegrity)
            {
                if (!File.Exists(phys))
                {
                    await _s.WriteAsync(
                        $"550 Resume rejected: no partial file found for '{fileName}' " +
                        $"at REST offset {rest.Value}. Upload the complete file.\r\n",
                        ct);
                    return;
                }

                var existingSize = new FileInfo(phys).Length;
                if (existingSize != rest.Value)
                {
                    await _s.WriteAsync(
                        $"550 Resume integrity check failed: partial file is {existingSize} bytes " +
                        $"but REST offset is {rest.Value}. Delete and re-upload.\r\n",
                        ct);
                    return;
                }

                // If an SFV is present, verify the partial file's CRC32 against a
                // cached .partcrc sidecar if one exists from the previous upload attempt.
                var partCrcFile = phys + ".partcrc";
                if (File.Exists(partCrcFile))
                {
                    try
                    {
                        var expectedHex = (await File.ReadAllTextAsync(partCrcFile, ct)).Trim();
                        var actualCrc = amFTPd.Utils.Cryptography.Crc32.ComputeFile(phys);
                        var actualHex = amFTPd.Utils.Cryptography.Crc32.ToHex(actualCrc);

                        if (!string.Equals(expectedHex, actualHex, StringComparison.OrdinalIgnoreCase))
                        {
                            await _s.WriteAsync(
                                $"550 Resume integrity check failed: partial CRC mismatch " +
                                $"(expected {expectedHex}, got {actualHex}). Delete and re-upload.\r\n",
                                ct);
                            return;
                        }
                    }
                    catch
                    {
                        // Sidecar unreadable — skip CRC check but still allow resume
                    }
                }
            }

            await _s.WriteAsync(FtpResponses.FileOk, ct);

            _s.BeginTransfer(fileName, isUpload: true);
            try
            {
                var transferOk = await _s.WithDataAsync(async data =>
                {
                    var mode = rest is > 0 ? FileMode.OpenOrCreate : FileMode.Create;
                    var preTransferSize = rest is > 0 && File.Exists(phys) ? new FileInfo(phys).Length : 0L;

                    await using var fs = new FileStream(
                        phys,
                        mode,
                        FileAccess.Write,
                        FileShare.Read);

                    // ============================================================
                    // CRC32 STREAM WRAPPER (ADDED)
                    // ============================================================
                    await using var crcStream = new Crc32WriteStream(fs);

                    if (rest is > 0)
                        fs.Seek(rest.Value, SeekOrigin.Begin);

                    var transferred = await CopyWithThrottleAsync(
                        data,
                        crcStream,
                        ResolveEffectiveSpeedKbps(isDownload: false, section, virtTarget),
                        isDownload: false,
                        ct,
                        _s.AddTransferBytes);

                    if (transferred <= 0)
                        return 0L;

                    if (section is not null)
                    {
                        Server.NotifySectionBandwidth(
                            section.Name,
                            transferred,
                            isUpload: true);
                    }

                    // ---- Credits ---------------------------------------------------
                    if (section is not null)
                        ApplyUploadCredits(virtTarget, section, transferred);

                    // ---- Rolling stats ---------------------------------------------
                    var rs = _runtime.RollingStats;
                    rs.UploadBytes5s.Add(transferred);
                    rs.UploadBytes1m.Add(transferred);
                    rs.UploadBytes5m.Add(transferred);
                    rs.Transfers5s.Add(1);
                    rs.Transfers1m.Add(1);
                    rs.Transfers5m.Add(1);

                    // ---- Live user stats -------------------------------------------
                    var live = _runtime.LiveStats;

                    if (_s.Account is not null)
                    {
                        var user = live.Users.GetOrAdd(
                            _s.Account.UserName,
                            _ => new UserLiveStats { UserName = _s.Account.UserName });

                        Interlocked.Increment(ref user.Uploads);
                        Interlocked.Add(ref user.BytesUploaded, transferred);
                    }

                    // ---- Live section stats ----------------------------------------
                    if (section is not null)
                    {
                        var sec = live.Sections.GetOrAdd(
                            section.Name,
                            _ => new SectionLiveStats { SectionName = section.Name });

                        Interlocked.Increment(ref sec.Uploads);
                        Interlocked.Add(ref sec.BytesUploaded, transferred);

                        lock (_sectionsTouched)
                        {
                            if (_sectionsTouched.Add(section.Name))
                                Interlocked.Increment(ref sec.ActiveUsers);
                        }
                    }

                    // ============================================================
                    // BINARY DUPE STORE UPDATE (ADDED)
                    // ============================================================
                    if (_runtime.DupeStore is BinaryDupeStore dupe && section is not null)
                    {
                        var release = Path.GetFileName(dirVirt.TrimEnd('/'));

                        // Replace existing dupe state for this file so size/CRC are
                        // calculated from the final on-disk file after resume/overwrite.
                        dupe.RemoveFile(section.Name, release, fileName);

                        await crcStream.FlushAsync(ct);

                        uint finalCrc;
                        try
                        {
                            finalCrc = rest is > 0
                                ? amFTPd.Utils.Cryptography.Crc32.ComputeFile(phys)
                                : crcStream.Hash;
                        }
                        catch
                        {
                            finalCrc = crcStream.Hash;
                        }

                        var finalSize = preTransferSize + transferred;

                        dupe.AddOrUpdateFile(
                            section.Name,
                            release,
                            fileName,
                            finalCrc,
                            finalSize,
                            _s.Account!.UserName,
                            _s.Account.GroupName);
                    }

                    // ---- Zipscript -------------------------------------------------
                    if (_runtime.Zipscript is not null)
                    {
                        var ctx = new ZipscriptUploadContext(
                            section?.Name,
                            virtTarget,
                            phys,
                            transferred,
                            _s.Account?.UserName,
                            DateTimeOffset.UtcNow,
                            Crc32: crcStream.Hash);

                        _runtime.Zipscript.OnUploadComplete(ctx);
                    }

                    if (_s.Account is { } acc && section is not null)
                    {
                        _raceEngine.RegisterUpload(
                            acc.UserName,
                            dirVirt,
                            section.Name,
                            transferred);
                    }

                    // ---- Resume integrity sidecar (best-effort) -------------------
                    // If RequireResumeIntegrity is set, write (or refresh) a .partcrc
                    // sidecar alongside the uploaded file.  A subsequent resume will
                    // verify this CRC before accepting the REST offset.
                    if (section is { RequireResumeIntegrity: true })
                    {
                        try
                        {
                            var partCrcPath = phys + ".partcrc";
                            var fileCrc = amFTPd.Utils.Cryptography.Crc32.ComputeFile(phys);
                            await File.WriteAllTextAsync(
                                partCrcPath,
                                amFTPd.Utils.Cryptography.Crc32.ToHex(fileCrc),
                                ct);
                        }
                        catch { /* best-effort; never block the upload response */ }
                    }

                    FireSiteEvent("onUpload", virtTarget, section, _s.Account?.UserName);

                    // Publish Upload event to EventBus (for session log, xferlog, IRC, etc.)
                    _runtime.EventBus?.Publish(new FtpEvent
                    {
                        Type = FtpEventType.Upload,
                        Timestamp = DateTimeOffset.UtcNow,
                        SessionId = _s.SessionId,
                        User = _s.Account?.UserName,
                        Group = _s.Account?.GroupName,
                        Section = section?.Name,
                        VirtualPath = virtTarget,
                        Bytes = transferred,
                        RemoteHost = _s.RemoteEndPoint?.Address.ToString()
                    });

                    return transferred;
                }, isUpload: true, countBandwidth: true, ct);

                if (transferOk)
                    await _s.WriteAsync(FtpResponses.ClosingData, ct);
            }
            finally { _s.EndTransfer(); }
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
                    await SendDenyAsync(res, "LIST", ct);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var virtTarget = FtpPath.Normalize(_s.Cwd, arg);

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
                if (dirResult != null &&
                    dirResult.Success &&
                    dirResult.Node is { Type: VfsNodeType.PhysicalDirectory } node)
                {
                    physDir = node.PhysicalPath!;
                }
                else
                {
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

            // APPE ignores REST
            _s.ClearRestOffset();

            long transferredTotal = 0;
            long preTransferSize = 0;

            var transferOk = await _s.WithDataAsync(async s =>
            {
                preTransferSize = new FileInfo(phys).Length;

                await using var fs = new FileStream(
                    phys,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read);

                var transferred = await CopyWithThrottleAsync(
                    s,
                    fs,
                    ResolveEffectiveSpeedKbps(isDownload: false, section, virtTarget),
                    isDownload: false,
                    ct);

                if (transferred <= 0)
                    return 0L;

                transferredTotal = transferred;

                if (section is not null)
                {
                    Server.NotifySectionBandwidth(
                        section.Name,
                        transferred,
                        isUpload: true);
                }

                if (section is not null)
                    ApplyUploadCredits(virtTarget, section, transferred);

                FireSiteEvent("onUpload", virtTarget, section, _s.Account?.UserName);

                if (_runtime.Zipscript is not null && section is not null)
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

                if (_s.Account is { } acc && section is not null)
                {
                    _raceEngine.RegisterUpload(
                        acc.UserName,
                        dirVirt,
                        section.Name,
                        transferred);
                }

                return transferred;
            }, isUpload: true, countBandwidth: true, ct);

            // ============================================================
            // RECOMPUTE CRC FOR FULL FILE (CORRECT FOR APPE)
            // ============================================================
            if (transferredTotal > 0 &&
                _runtime.DupeStore is BinaryDupeStore dupe &&
                section is not null &&
                _s.Account is { } account)
            {
                uint crc;
                try
                {
                    crc = Crc32.ComputeFile(phys);
                }
                catch
                {
                    // CRC failure should not abort APPE
                    crc = 0;
                }

                var release = Path.GetFileName(dirVirt.TrimEnd('/'));
                var fileSize = preTransferSize + transferredTotal;

                dupe.RemoveFile(section.Name, release, fileName);

                dupe.AddOrUpdateFile(
                    section.Name,
                    release,
                    fileName,
                    crc,
                    fileSize,
                    account.UserName,
                    account.GroupName);
            }

            if (transferOk)
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
                    await SendDenyAsync(res, "LIST", ct);
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
                    await SendDenyAsync(res, "LIST", ct);
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
                await _s.WriteAsync(FtpErrorReply(vfsResult.ErrorMessage, "550 File not found.\r\n"), ct);
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
                    _s.VfsManager?.ClearCache();

                    // Clean up .partcrc sidecar if present
                    try
                    {
                        var partCrc = phys + ".partcrc";
                        if (File.Exists(partCrc)) File.Delete(partCrc);
                    }
                    catch { /* best-effort */ }

                    // ============================================================
                    // BINARY DUPE STORE UPDATE (ADDED)
                    // ============================================================
                    if (_runtime.DupeStore is BinaryDupeStore dupe &&
                        node?.VirtualPath is not null)
                    {
                        var dirVirt = Path.GetDirectoryName(node.VirtualPath)
                                      ?.Replace('\\', '/') ?? "/";

                        var release = Path.GetFileName(dirVirt.TrimEnd('/'));
                        var fileName = Path.GetFileName(node.VirtualPath);

                        dupe.RemoveFile(
                            section.Name,
                            release,
                            fileName);
                    }

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
                _log.Log(FtpLogLevel.Debug, $"[AMScript] Evaluating MKD for {ctx.UserName}");
                var res = _userScript.EvaluateUser(ctx);
                _log.Log(FtpLogLevel.Debug, $"[AMScript] MKD result: {res.Action} {res.DenyReason}");
                if (res.Action == AMRuleAction.Deny)
                {
                    await SendDenyAsync(res, "MKD", ct);
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
                _s.VfsManager?.ClearCache();
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
                    await SendDenyAsync(res, "LIST", ct);
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
                await _s.WriteAsync(FtpErrorReply(vfsResult.ErrorMessage, "550 Directory not found.\r\n"), ct);
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
                    await SendDenyAsync(res, "LIST", ct);
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
                await _s.WriteAsync(FtpErrorReply(fromResult.ErrorMessage, "550 Not found.\r\n"), ct);
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
                if (toDirResult != null && toDirResult.Success &&
                    toDirResult.Node is { Type: VfsNodeType.PhysicalDirectory } toDirNode)
                {
                    var physDir = toDirNode.PhysicalPath!;
                    toPhys = Path.Combine(physDir, toName);
                }
                else
                {
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
                    _s.VfsManager?.ClearCache();
                }
                else if (isDir)
                {
                    Directory.Move(fromPhys, toPhys);
                    _s.VfsManager?.ClearCache();
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

            // ============================================================
            // BINARY DUPE STORE UPDATE (FILES ONLY)  <-- ADDED
            // ============================================================
            if (isFile &&
                _runtime.DupeStore is BinaryDupeStore dupe &&
                _s.Account is { } acc)
            {
                var section = GetSectionForVirtual(toVirt);

                var fromDir = Path.GetDirectoryName(fromVirt)
                              ?.Replace('\\', '/') ?? "/";

                var toDir = Path.GetDirectoryName(toVirt)
                            ?.Replace('\\', '/') ?? "/";

                var fromRelease = Path.GetFileName(fromDir.TrimEnd('/'));
                var toRelease = Path.GetFileName(toDir.TrimEnd('/'));

                var oldName = Path.GetFileName(fromVirt);
                var newName = Path.GetFileName(toVirt);

                // Remove old entry
                dupe.RemoveFile(section.Name, fromRelease, oldName);

                // Re-add under new location/name
                uint crc;
                try { crc = Crc32.Compute(toPhys); }
                catch { crc = 0; }

                long size = 0;
                try { size = new FileInfo(toPhys).Length; }
                catch { }

                dupe.AddOrUpdateFile(
                    section.Name,
                    toRelease,
                    newName,
                    crc,
                    size,
                    acc.UserName,
                    acc.GroupName);
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
                        var delCtx = new ZipscriptDeleteContext(
                            section.Name,
                            fromVirt,
                            fromPhys,
                            IsDirectory: false,
                            user,
                            now);

                        _runtime.Zipscript.OnDelete(delCtx);

                        long size = 0;
                        try { size = new FileInfo(toPhys).Length; } catch { }

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
                // zipscript is best-effort
            }

            _s.RenameFrom = null;
            await _s.WriteAsync(FtpResponses.ActionOk, ct);
        }

        private async Task SIZE(string arg, CancellationToken ct)
        {
            _log.Log(FtpLogLevel.Debug, $"[FTP] SIZE {arg}");
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
                    await SendDenyAsync(res, "SIZE", ct);
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
            if (vfsResult == null || !vfsResult.Success || vfsResult.Node is null)
            {
                await _s.WriteAsync(FtpErrorReply(vfsResult?.ErrorMessage, "550 File not found.\r\n"), ct);
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
                    await SendDenyAsync(res, "MDTM", ct);
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
                await _s.WriteAsync(FtpErrorReply(vfsResult.ErrorMessage, "550 File not found.\r\n"), ct);
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
                    await SendDenyAsync(res, "LIST", ct);
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
                await _s.WriteAsync(FtpErrorReply(vfsResult.ErrorMessage, "550 File not found.\r\n"), ct);
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
            // ------------------------------------------------------------------
            // Centralized SITE hardening (router-level, post-script)
            // ------------------------------------------------------------------

            // Must be logged in
            if (_s.Account is null)
            {
                await _s.WriteAsync("530 Please login first.\r\n", ct);
                return;
            }

            // Reputation gate: SITE is privileged and sensitive
            if (_s.Reputation != FtpSessionReputation.Good)
            {
                await _s.WriteAsync("550 SITE disabled for this session.\r\n", ct);
                return;
            }

            // Context for authorization: has verb, args, router, session, etc.
            var authCtx = new SiteCommandContext(this, sub, rest);

            // --------------------------------------------------------------------------------------------------
            // TCL SCRIPTING: Check for glFTPd-style custom TCL commands
            // ------------------------------------------------------------------
            if (_runtime.TclRunner is not null && _runtime.Tcl is { SiteCommands: { Count: > 0 } tclMap })
            {
                if (tclMap.TryGetValue(sub, out var tclScriptPath))
                {
                    var tclArgs = string.IsNullOrWhiteSpace(rest) ? [] : rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    _log.Log(FtpLogLevel.Debug, $"[TCL] Executing {tclScriptPath} for user {account?.UserName}");
                    var tclResult = await _runtime.TclRunner.ExecuteSiteCommandAsync(tclScriptPath, account!, tclArgs, _s);
                    _log.Log(FtpLogLevel.Debug, $"[TCL] Result: Success={tclResult.Success}, OutputLength={tclResult.Result?.Length ?? 0}");

                    // glFTPd scripts output to stdout, which we capture as tclResult.Result.
                    if (!string.IsNullOrEmpty(tclResult.Result))
                    {
                        var lines = tclResult.Result.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            // If script already provides code (e.g. 200-), don't prefix.
                            if (line.Length >= 4 && char.IsDigit(line[0]) && (line[3] == '-' || line[3] == ' '))
                            {
                                await _s.WriteAsync(line, ct);
                            }
                            else
                            {
                                await _s.WriteAsync($"200- {line}", ct);
                            }
                        }
                        await _s.WriteAsync("200 OK", ct);
                    }
                    else if (tclResult.Success)
                    {
                        await _s.WriteAsync("200 Command successful.", ct);
                    }
                    else
                    {
                        await _s.WriteAsync("550 Script failed or returned error.", ct);
                    }
                    return;
                }
            }

            if (!_siteCommands.TryGetValue(sub, out var cmd))
            {
                if (!FtpAuthorization.CanUseSiteCommand(account, sub, authCtx))
                {
                    await _s.WriteAsync("550 Permission denied.\r\n", ct);
                    _log.Log(FtpLogLevel.Warn,
                        $"SITE {sub} rejected for user {account?.UserName} from {_s.RemoteEndPoint}");
                    return;
                }

                // Delegate to any plugin that registered this verb before giving up.
                if (_runtime.PluginHost is { } host)
                {
                    var handled = await host.TryHandleSiteCommandAsync(
                        verb: sub,
                        argument: rest,
                        username: account!.UserName,
                        isAdmin: account.IsAdmin,
                        isSiteop: account.IsSiteop,
                        remoteIp: _s.RemoteEndPoint?.Address?.ToString() ?? string.Empty,
                        currentPath: _s.Cwd,
                        write: (msg, token) => _s.WriteAsync(msg, token),
                        ct: ct).ConfigureAwait(false);

                    if (handled) return;
                }

                // Show the original verb in the error so scripts see what they typed
                await _s.WriteAsync($"502 Unknown SITE command '{rawSub}'.\r\n", ct);
                return;
            }

            if ((cmd.RequiresAdmin && account?.IsAdmin != true) ||
                (cmd.RequiresSiteop && account?.IsAdmin != true && account?.IsSiteop != true))
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                _log.Log(FtpLogLevel.Warn,
                    $"SITE {sub} rejected for user {account?.UserName} from {_s.RemoteEndPoint}");
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

        private static string ExtractListPathArg(string rawArg)
        {
            // FTP clients sometimes send LIST/NLST/MLSD options like "-la" or "-a /path".
            // We treat the last non-option token as a path; if none, we default to ".".
            if (string.IsNullOrWhiteSpace(rawArg))
                return ".";

            var s = rawArg.Trim();

            if (!s.StartsWith("-", StringComparison.Ordinal))
                return s;

            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                var p = parts[i];
                if (p.Length == 0)
                    continue;

                if (!p.StartsWith("-", StringComparison.Ordinal))
                    return p;
            }

            return ".";
        }

        private static string ExtractListTarget(string? arg)
        {
            if (string.IsNullOrWhiteSpace(arg))
                return ".";

            // Common FTP clients send options like: "-la". We treat the last non-option token as a path.
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                var p = parts[i];
                if (p.Length == 0)
                    continue;
                if (p[0] == '-')
                    continue;
                return p;
            }

            return ".";
        }

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
                Event: eventName,
                IsAdmin: acc is not null && (acc.IsAdmin || acc.IsSiteop)
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
