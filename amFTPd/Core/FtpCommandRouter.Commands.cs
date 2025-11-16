using amFTPd.Config.Ftpd;
using amFTPd.Scripting;
using amFTPd.Security;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace amFTPd.Core
{
    internal sealed partial class FtpCommandRouter
    {
        private AMScriptContext BuildCreditContext(FtpSection section, long bytes)
        {
            var account = _s.Account!;
            long kb = Math.Max(1L, bytes / 1024L);

            return new AMScriptContext(
                IsFxp: _isFxp,
                Section: section.Name,
                FreeLeech: section.FreeLeech,
                UserName: account.UserName,
                UserGroup: account.GroupName ?? string.Empty,
                Bytes: bytes,
                Kb: kb,
                CostDownload: kb,  // default: 1:1
                EarnedUpload: kb   // default: 1:1
            );
        }

        private AMScriptContext BuildSimpleContextForFxpAndActive()
        {
            var account = _s.Account;
            var userName = account?.UserName ?? string.Empty;
            var userGroup = account?.GroupName ?? string.Empty;

            return new AMScriptContext(
                IsFxp: _isFxp,
                Section: string.Empty,
                FreeLeech: false,
                UserName: userName,
                UserGroup: userGroup,
                Bytes: 0,
                Kb: 0,
                CostDownload: 0,
                EarnedUpload: 0
            );
        }


        // --- Auth / TLS ---

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
            // USER must be sent first
            if (_s.PendingUser is null)
            {
                await _s.WriteAsync(FtpResponses.BadSeq, ct); // 503
                return;
            }

            var pending = _s.PendingUser;

            // Anonymous login (if allowed)
            if (_cfg.AllowAnonymous && pending.Equals("anonymous", StringComparison.OrdinalIgnoreCase))
            {
                // We ignore the password, but clients still send something (usually email).
                var anon = new FtpUser(
                    UserName: "anonymous",
                    PasswordHash: string.Empty,  // not used
                    HomeDir: "/",
                    IsAdmin: false,
                    AllowFxp: false,
                    AllowUpload: false,          // typically no uploads for anon
                    AllowDownload: true,
                    AllowActiveMode: true,
                    MaxConcurrentLogins: 100,
                    IdleTimeout: TimeSpan.FromMinutes(15),
                    MaxUploadKbps: 0,
                    MaxDownloadKbps: 0,
                    GroupName: "anonymous",
                    CreditsKb: 0,
                    AllowedIpMask: null,         // no IP restriction
                    RequireIdentMatch: false,
                    RequiredIdent: null
                );

                _s.Login(anon);
                _s.PendingUser = null;
                await _s.WriteAsync(FtpResponses.AuthOk, ct); // 230
                return;
            }

            // Normal authenticated user
            if (!_s.Users.TryAuthenticate(pending, arg, out var account) || account is null)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct); // 530
                return;
            }

            // ---- IP restriction ----------------------------------------------------
            if (!string.IsNullOrWhiteSpace(account.AllowedIpMask))
            {
                var remoteEp = (IPEndPoint)_s.Control.Client.RemoteEndPoint!;
                if (!IsIpAllowed(remoteEp.Address, account.AllowedIpMask))
                {
                    await _s.WriteAsync("530 Login not allowed from this IP.\r\n", ct);
                    return;
                }
            }

            // ---- IDENT check -------------------------------------------------------
            if (account.RequireIdentMatch && !string.IsNullOrWhiteSpace(account.RequiredIdent))
            {
                var remoteEp = (IPEndPoint)_s.Control.Client.RemoteEndPoint!;
                var localEp = (IPEndPoint)_s.Control.Client.LocalEndPoint!;

                var ident = await QueryIdentAsync(localEp, remoteEp, ct);

                if (ident is null ||
                    !string.Equals(ident, account.RequiredIdent, StringComparison.OrdinalIgnoreCase))
                {
                    await _s.WriteAsync("530 Ident mismatch.\r\n", ct);
                    return;
                }
            }

            // All good → log in
            _s.Login(account);
            _s.PendingUser = null;
            await _s.WriteAsync(FtpResponses.AuthOk, ct); // 230
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

            await _s.WriteAsync(FtpResponses.TlsReady, ct);
            await _s.UpgradeToTlsAsync(_tls);
        }

        private async Task PROT(string arg, CancellationToken ct)
        {
            var v = arg.Trim().ToUpperInvariant();
            if (v is "C" or "P")
            {
                _s.Protection = v;
                await _s.WriteAsync(FtpResponses.ProtOk, ct);
            }
            else
            {
                await _s.WriteAsync("536 Only C or P supported.\r\n", ct);
            }
        }

        private Task FEAT(CancellationToken ct)
            => _s.WriteAsync(
                "211-Features:\r\n" +
                " UTF8\r\n EPSV\r\n EPRT\r\n PASV\r\n PBSZ\r\n PROT\r\n AUTH TLS\r\n" +
                " SIZE\r\n MDTM\r\n REST STREAM\r\n" +
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
            sb.AppendLine(" USER PASS AUTH PBSZ PROT");
            sb.AppendLine(" FEAT SYST OPTS NOOP QUIT HELP STAT ALLO MODE STRU ABOR");
            sb.AppendLine(" PWD CWD CDUP TYPE");
            sb.AppendLine(" PASV EPSV PORT EPRT");
            sb.AppendLine(" LIST NLST RETR STOR APPE REST");
            sb.AppendLine(" DELE MKD RMD RNFR RNTO");
            sb.AppendLine(" SIZE MDTM SITE");
            sb.AppendLine("214 Help OK.");

            await _s.WriteAsync(sb.ToString().Replace("\n", "\r\n"), ct);
        }

        private async Task STAT(string arg, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync("502 STAT with path not implemented.\r\n", ct);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("211-FTP server status:");
            sb.AppendLine($" Connected: {(_s.Control.Connected ? "Yes" : "No")}");
            sb.AppendLine($" Logged in: {(_s.LoggedIn ? "Yes" : "No")}");
            sb.AppendLine($" User: {_s.UserName ?? "(none)"}");
            sb.AppendLine($" CWD: {_s.Cwd}");
            sb.AppendLine("211 End of status.");

            await _s.WriteAsync(sb.ToString().Replace("\n", "\r\n"), ct);
        }

        // --- Navigation ---

        private async Task CWD(string arg, CancellationToken ct)
        {
            if (!_s.LoggedIn)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            var newV = FtpPath.Normalize(_s.Cwd, arg);
            string phys;
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
            if (!_s.LoggedIn)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            var newV = FtpPath.Normalize(_s.Cwd, "..");
            string phys;
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

        // --- Transfer parameters ---

        private async Task TYPE(string arg, CancellationToken ct)
        {
            if (arg.StartsWith("I", StringComparison.OrdinalIgnoreCase))
                await _s.WriteAsync(FtpResponses.TypeSetBinary, ct);
            else
                await _s.WriteAsync(FtpResponses.TypeSetAscii, ct);
        }

        private async Task PASV(CancellationToken ct)
        {
            int port = await _s.OpenPassiveAsync(ct);

            var ip = _cfg.BindAddress;
            var remote = (IPEndPoint)_s.Control.Client.RemoteEndPoint!;

            _isFxp = !ip.Equals(remote.Address);

            // FXP script hook
            if (_fxpScript is not null && _isFxp)
            {
                var ctx = BuildSimpleContextForFxpAndActive();
                var result = _fxpScript.EvaluateDownload(ctx);
                if (result.Action == AMRuleAction.Deny)
                {
                    await _s.WriteAsync("504 FXP not allowed in PASV by rule.\r\n", ct);
                    return;
                }
            }

            var allowFxp = _s.Account?.AllowFxp ?? _cfg.AllowFxp;
            if (!allowFxp && _isFxp)
            {
                await _s.WriteAsync("504 FXP not allowed in PASV.\r\n", ct);
                return;
            }

            var b = ip.GetAddressBytes();
            await _s.WriteAsync(
                $"227 Entering Passive Mode ({b[0]},{b[1]},{b[2]},{b[3]},{port >> 8},{port & 255})\r\n",
                ct
            );
        }

        private Task EPSV(CancellationToken ct)
            => EPSV(null, ct);

        private async Task EPSV(string? arg, CancellationToken ct)
        {
            // EPSV ALL
            if (!string.IsNullOrEmpty(arg) &&
                arg.Trim().Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                await _s.WriteAsync("200 EPSV ALL command successful.\r\n", ct);
                return;
            }

            // family checks kept simple or removed if you prefer

            int port = await _s.OpenPassiveAsync(ct);

            // In EPSV, FXP detection is a bit looser; you can treat EPSV as local-only
            var ip = _cfg.BindAddress;
            var remote = (IPEndPoint)_s.Control.Client.RemoteEndPoint!;
            _isFxp = !ip.Equals(remote.Address);

            if (_fxpScript is not null && _isFxp)
            {
                var ctx = BuildSimpleContextForFxpAndActive();
                var result = _fxpScript.EvaluateDownload(ctx);
                if (result.Action == AMRuleAction.Deny)
                {
                    await _s.WriteAsync("504 FXP not allowed in EPSV by rule.\r\n", ct);
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

            _isFxp = !requestedIp.Equals(remote.Address);

            // Active-mode script hook
            if (_activeScript is not null)
            {
                var ctx = BuildSimpleContextForFxpAndActive();
                var result = _activeScript.EvaluateDownload(ctx);
                if (result.Action == AMRuleAction.Deny)
                {
                    await _s.WriteAsync("504 Active mode denied by rule.\r\n", ct);
                    return;
                }
            }

            // FXP script hook
            if (_fxpScript is not null && _isFxp)
            {
                var ctx = BuildSimpleContextForFxpAndActive();
                var result = _fxpScript.EvaluateDownload(ctx);
                if (result.Action == AMRuleAction.Deny)
                {
                    await _s.WriteAsync("504 FXP not allowed by rule.\r\n", ct);
                    return;
                }
            }

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

            // Format: |<af>|<host>|<port>|
            // Example: |1|192.168.1.5|52344|
            //          |2|2001:db8::1|50200|
            if (!arg.StartsWith('|') || arg.Count(c => c == '|') < 3)
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var parts = arg.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            // parts[0] = address family (1 = IPv4, 2 = IPv6)
            // parts[1] = IP
            // parts[2] = port
            var af = parts[0];
            var host = parts[1];
            var portString = parts[2];

            if (!int.TryParse(portString, out int port))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            IPAddress requestedIp;
            if (!IPAddress.TryParse(host, out requestedIp))
            {
                await _s.WriteAsync("500 Invalid address.\r\n", ct);
                return;
            }

            var remote = (IPEndPoint)_s.Control.Client.RemoteEndPoint!;

            // --- FXP DETECTION (same rule as PORT) ---
            _isFxp = !requestedIp.Equals(remote.Address);

            // Active-mode script hook
            if (_activeScript is not null)
            {
                var ctx = BuildSimpleContextForFxpAndActive();
                var result = _activeScript.EvaluateDownload(ctx);
                if (result.Action == AMRuleAction.Deny)
                {
                    await _s.WriteAsync("504 Active mode denied by rule.\r\n", ct);
                    return;
                }
            }

            // FXP script hook
            if (_fxpScript is not null && _isFxp)
            {
                var ctx = BuildSimpleContextForFxpAndActive();
                var result = _fxpScript.EvaluateDownload(ctx);
                if (result.Action == AMRuleAction.Deny)
                {
                    await _s.WriteAsync("504 FXP not allowed by rule.\r\n", ct);
                    return;
                }
            }

            var allowFxp = _s.Account?.AllowFxp ?? _cfg.AllowFxp;
            if (!allowFxp && _isFxp)
            {
                await _s.WriteAsync("504 FXP not allowed: IP mismatch.\r\n", ct);
                return;
            }

            await _s.OpenActiveAsync(requestedIp, port, ct);
            await _s.WriteAsync(FtpResponses.CmdOkay, ct);
        }

        // --- Listing / transfer ---

        private async Task LIST(string arg, CancellationToken ct)
        {
            if (!_s.LoggedIn)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            var target = FtpPath.Normalize(_s.Cwd, string.IsNullOrWhiteSpace(arg) ? "." : arg);
            string phys;
            try
            {
                phys = _fs.MapToPhysical(target);
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            if (!Directory.Exists(phys) && !File.Exists(phys))
            {
                await _s.WriteAsync("550 Not found.\r\n", ct);
                return;
            }

            await _s.WriteAsync(FtpResponses.FileOk, ct);

            await _s.WithDataAsync(async stream =>
            {
                using var wr = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                if (Directory.Exists(phys))
                {
                    foreach (var dir in Directory.EnumerateDirectories(phys))
                        await wr.WriteLineAsync(_fs.ToUnixListLine(new DirectoryInfo(dir)));

                    foreach (var file in Directory.EnumerateFiles(phys))
                        await wr.WriteLineAsync(_fs.ToUnixListLine(new FileInfo(file)));
                }
                else
                {
                    await wr.WriteLineAsync(_fs.ToUnixListLine(new FileInfo(phys)));
                }
            }, ct);

            await _s.WriteAsync(FtpResponses.ClosingData, ct);
        }

        private async Task NLST(string arg, CancellationToken ct)
        {
            if (!_s.LoggedIn)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            var target = FtpPath.Normalize(_s.Cwd, string.IsNullOrWhiteSpace(arg) ? "." : arg);
            string phys;
            try
            {
                phys = _fs.MapToPhysical(target);
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            if (!Directory.Exists(phys) && !File.Exists(phys))
            {
                await _s.WriteAsync("550 Not found.\r\n", ct);
                return;
            }

            await _s.WriteAsync(FtpResponses.FileOk, ct);

            await _s.WithDataAsync(async stream =>
            {
                using var wr = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                if (Directory.Exists(phys))
                {
                    foreach (var dir in Directory.EnumerateDirectories(phys))
                        await wr.WriteLineAsync(Path.GetFileName(dir));

                    foreach (var file in Directory.EnumerateFiles(phys))
                        await wr.WriteLineAsync(Path.GetFileName(file));
                }
                else
                {
                    await wr.WriteLineAsync(Path.GetFileName(phys));
                }
            }, ct);

            await _s.WriteAsync(FtpResponses.ClosingData, ct);
        }

        private async Task RETR(string arg, CancellationToken ct)
        {
            if (!_s.LoggedIn)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            if (_s.Account is { AllowDownload: false })
            {
                await _s.WriteAsync("550 Download not allowed for this user.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var virtTarget = FtpPath.Normalize(_s.Cwd, arg);
            string phys;
            try
            {
                phys = _fs.MapToPhysical(virtTarget);
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            if (!File.Exists(phys))
            {
                await _s.WriteAsync("550 File not found.\r\n", ct);
                return;
            }

            var fi = new FileInfo(phys);
            long length = fi.Length;
            var rest = _s.RestOffset;
            if (rest.HasValue && rest.Value > 0 && rest.Value < length)
                length -= rest.Value;

            var section = GetSectionForVirtual(virtTarget);

            if (!await CheckDownloadCreditsAsync(section, length, ct))
                return;

            await _s.WriteAsync(FtpResponses.FileOk, ct);

            var offset = rest;
            _s.ClearRestOffset();

            await _s.WithDataAsync(async s =>
            {
                await using var fs = new FileStream(phys, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (offset.HasValue && offset.Value > 0)
                    fs.Seek(offset.Value, SeekOrigin.Begin);

                var maxKbps = _s.Account?.MaxDownloadKbps ?? 0;
                var transferred = await CopyWithThrottleAsync(fs, s, maxKbps, ct);
                ApplyDownloadCredits(section, transferred);
            }, ct);

            await _s.WriteAsync(FtpResponses.ClosingData, ct);
        }

        private async Task STOR(string arg, CancellationToken ct)
        {
            if (!_s.LoggedIn)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            if (_s.Account is { AllowUpload: false })
            {
                await _s.WriteAsync("550 Upload not allowed for this user.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var virtTarget = FtpPath.Normalize(_s.Cwd, arg);
            string phys;
            try
            {
                phys = _fs.MapToPhysical(virtTarget);
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

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
                ApplyUploadCredits(section, transferred);
            }, ct);

            await _s.WriteAsync(FtpResponses.ClosingData, ct);
        }

        private async Task APPE(string arg, CancellationToken ct)
        {
            if (!_s.LoggedIn)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            if (_s.Account is { AllowUpload: false })
            {
                await _s.WriteAsync("550 Upload not allowed for this user.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var virtTarget = FtpPath.Normalize(_s.Cwd, arg);
            string phys;
            try
            {
                phys = _fs.MapToPhysical(virtTarget);
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

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
                ApplyUploadCredits(section, transferred);
            }, ct);

            await _s.WriteAsync(FtpResponses.ClosingData, ct);
        }

        private async Task REST(string arg, CancellationToken ct)
        {
            if (!_s.LoggedIn)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg) || !long.TryParse(arg, out var offset) || offset < 0)
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            _s.RestOffset = offset;
            await _s.WriteAsync($"350 Restarting at {offset}. Send STORE or RETRIEVE.\r\n", ct);
        }

        // --- File system ops ---

        private async Task DELE(string arg, CancellationToken ct)
        {
            var target = FtpPath.Normalize(_s.Cwd, arg);
            string phys;
            try
            {
                phys = _fs.MapToPhysical(target);
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            if (File.Exists(phys))
            {
                File.Delete(phys);
                await _s.WriteAsync(FtpResponses.ActionOk, ct);
            }
            else
            {
                await _s.WriteAsync("550 File not found.\r\n", ct);
            }
        }

        private async Task MKD(string arg, CancellationToken ct)
        {
            var target = FtpPath.Normalize(_s.Cwd, arg);
            string phys;
            try
            {
                phys = _fs.MapToPhysical(target);
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            Directory.CreateDirectory(phys);
            await _s.WriteAsync(FtpResponses.PathCreated, ct);
        }

        private async Task RMD(string arg, CancellationToken ct)
        {
            var target = FtpPath.Normalize(_s.Cwd, arg);
            string phys;
            try
            {
                phys = _fs.MapToPhysical(target);
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            if (Directory.Exists(phys))
            {
                Directory.Delete(phys, true);
                await _s.WriteAsync(FtpResponses.ActionOk, ct);
            }
            else
            {
                await _s.WriteAsync("550 Directory not found.\r\n", ct);
            }
        }

        private async Task RNTO(string arg, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_s.RenameFrom))
            {
                await _s.WriteAsync(FtpResponses.BadSeq, ct);
                return;
            }

            var fromVirt = FtpPath.Normalize(_s.Cwd, _s.RenameFrom);
            var toVirt = FtpPath.Normalize(_s.Cwd, arg);

            string from, to;
            try
            {
                from = _fs.MapToPhysical(fromVirt);
                to = _fs.MapToPhysical(toVirt);
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(to)!);

            if (File.Exists(from))
            {
                File.Move(from, to, overwrite: true);
            }
            else if (Directory.Exists(from))
            {
                Directory.Move(from, to);
            }
            else
            {
                await _s.WriteAsync("550 Not found.\r\n", ct);
                return;
            }

            _s.RenameFrom = null;
            await _s.WriteAsync(FtpResponses.ActionOk, ct);
        }

        private async Task SIZE(string arg, CancellationToken ct)
        {
            if (!_s.LoggedIn)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var target = FtpPath.Normalize(_s.Cwd, arg);
            string phys;
            try
            {
                phys = _fs.MapToPhysical(target);
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

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
            if (!_s.LoggedIn)
            {
                await _s.WriteAsync(FtpResponses.NotLoggedIn, ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync(FtpResponses.SyntaxErr, ct);
                return;
            }

            var target = FtpPath.Normalize(_s.Cwd, arg);
            string phys;
            try
            {
                phys = _fs.MapToPhysical(target);
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            if (!File.Exists(phys))
            {
                await _s.WriteAsync("550 File not found.\r\n", ct);
                return;
            }

            var utc = File.GetLastWriteTimeUtc(phys);
            var stamp = utc.ToString("yyyyMMddHHmmss");
            await _s.WriteAsync($"213 {stamp}\r\n", ct);
        }

        // --- SITE stub ---

        private async Task SITE(string arg, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync("500 SITE requires a subcommand.\r\n", ct);
                return;
            }

            var parts = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
            var sub = parts[0].ToUpperInvariant();
            var rest = parts.Length > 1 ? parts[1] : string.Empty;

            switch (sub)
            {
                case "HELP":
                    await _s.WriteAsync(
                        "214-SITE commands:\r\n" +
                        " SITE HELP\r\n" +
                        " SITE WHO\r\n" +
                        " SITE USERS\r\n" +
                        " SITE GROUPS\r\n" +
                        " SITE KILL <id>\r\n" +
                        " SITE CHMOD <mode> <path>\r\n" +
                        " SITE ADDUSER <user> <password> <homedir> [group]\r\n" +
                        " SITE GADDUSER <group> <user>\r\n" +
                        " SITE CHPASS <user> <newpassword>\r\n" +
                        " SITE SETLIMITS <user> <upKbps> <downKbps>\r\n" +
                        " SITE SETFLAGS <user> <flags>\r\n" +
                        " SITE ADDIP <user> <mask>\r\n" +
                        " SITE DELIP <user>\r\n" +
                        " SITE IDENT <user> <ident>\r\n" +
                        " SITE REQIDENT <user> <on|off>\r\n" +
                        " SITE SHOWUSER <user>\r\n" +
                        "   Flags: +admin,-admin,+fxp,-fxp,+upload,-upload,+download,-download,+active,-active\r\n" +
                        "214 End\r\n", ct);
                    break;

                case "WHO":
                    await SITE_WHO(ct);
                    break;

                case "USERS":
                    await SITE_USERS(ct);
                    break;

                case "GROUPS":
                    await SITE_GROUPS(ct);
                    break;

                case "KILL":
                    await SITE_KILL(rest, ct);
                    break;

                case "CHMOD":
                    await SITE_CHMOD(rest, ct);
                    break;

                case "ADDUSER":
                    await SITE_ADDUSER(rest, ct);
                    break;

                case "GADDUSER":
                    await SITE_GADDUSER(rest, ct);
                    break;

                case "CHPASS":
                    await SITE_CHPASS(rest, ct);
                    break;

                case "SETLIMITS":
                    await SITE_SETLIMITS(rest, ct);
                    break;
                case "ADDIP":
                    await SITE_ADDIP(rest, ct);
                    break;

                case "DELIP":
                    await SITE_DELIP(rest, ct);
                    break;

                case "IDENT":
                    await SITE_IDENT(rest, ct);
                    break;
                case "SHOWUSER":
                    await SITE_SHOWUSER(rest, ct);
                    break;

                case "REQIDENT":
                    await SITE_REQIDENT(rest, ct);
                    break;

                case "SETFLAGS":
                    await SITE_SETFLAGS(rest, ct);
                    break;
                case "CREDITS":
                    await SITE_CREDITS(rest, ct);
                    break;
                case "GIVECRED":
                    await SITE_GIVECRED(rest, ct);
                    break;
                case "TAKECRED":
                    await SITE_TAKECRED(rest, ct);
                    break;
                case "SECTIONS":
                    await SITE_SECTIONS(ct);
                    break;
                default:
                    await _s.WriteAsync("502 SITE subcommand not implemented.\r\n", ct);
                    break;
            }
        }

        private async Task SITE_SHOWUSER(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync("550 Not logged in.\r\n", ct);
                return;
            }

            // If no argument: show current user
            string targetName;
            if (string.IsNullOrWhiteSpace(arg))
            {
                targetName = _s.Account.UserName;
            }
            else
            {
                targetName = arg.Trim();

                // Non-admins can only see themselves
                if (!_s.Account.IsAdmin &&
                    !targetName.Equals(_s.Account.UserName, StringComparison.OrdinalIgnoreCase))
                {
                    await _s.WriteAsync("550 Only admin can inspect other users.\r\n", ct);
                    return;
                }
            }

            var user = _s.Users.FindUser(targetName);
            if (user is null)
            {
                await _s.WriteAsync("550 User not found.\r\n", ct);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("211-User information:");
            sb.AppendLine($" USER       : {user.UserName}");
            sb.AppendLine($" GROUP      : {user.GroupName ?? "-"}");
            sb.AppendLine($" HOME       : {user.HomeDir}");
            sb.AppendLine($" ADMIN      : {(user.IsAdmin ? "YES" : "NO")}");
            sb.AppendLine($" FXP        : {(user.AllowFxp ? "YES" : "NO")}");
            sb.AppendLine($" UPLOAD     : {(user.AllowUpload ? "YES" : "NO")}");
            sb.AppendLine($" DOWNLOAD   : {(user.AllowDownload ? "YES" : "NO")}");
            sb.AppendLine($" ACTIVE MODE: {(user.AllowActiveMode ? "YES" : "NO")}");
            sb.AppendLine($" MAX LOGINS : {user.MaxConcurrentLogins}");
            sb.AppendLine($" IDLE TIME  : {(int)user.IdleTimeout.TotalSeconds} seconds");
            sb.AppendLine($" UL LIMIT   : {user.MaxUploadKbps} KB/s");
            sb.AppendLine($" DL LIMIT   : {user.MaxDownloadKbps} KB/s");
            sb.AppendLine($" CREDITS    : {user.CreditsKb} KB");
            sb.AppendLine($" IP MASK    : {user.AllowedIpMask ?? "-"}");
            sb.AppendLine($" REQ IDENT  : {(user.RequireIdentMatch ? "YES" : "NO")}");
            sb.AppendLine($" IDENT NAME : {user.RequiredIdent ?? "-"}");
            sb.AppendLine("211 End");

            await _s.WriteAsync(sb.ToString().Replace("\n", "\r\n"), ct);
        }

        private async Task SITE_ADDIP(string arg, CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE ADDIP requires admin privileges.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync("501 Usage: SITE ADDIP <user> <mask>\r\n", ct);
                return;
            }

            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await _s.WriteAsync("501 Usage: SITE ADDIP <user> <mask>\r\n", ct);
                return;
            }

            var userName = parts[0];
            var mask = parts[1];

            var user = _s.Users.FindUser(userName);
            if (user is null)
            {
                await _s.WriteAsync("550 User not found.\r\n", ct);
                return;
            }

            var updated = user with { AllowedIpMask = mask };

            if (_s.Users.TryUpdateUser(updated, out var error))
            {
                await _s.WriteAsync($"200 IP mask set to '{mask}'.\r\n", ct);
            }
            else
            {
                await _s.WriteAsync($"550 Failed to update user: {error}\r\n", ct);
            }
        }

        private async Task SITE_DELIP(string arg, CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE DELIP requires admin privileges.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync("501 Usage: SITE DELIP <user>\r\n", ct);
                return;
            }

            var userName = arg.Trim();

            var user = _s.Users.FindUser(userName);
            if (user is null)
            {
                await _s.WriteAsync("550 User not found.\r\n", ct);
                return;
            }

            var updated = user with { AllowedIpMask = null };

            if (_s.Users.TryUpdateUser(updated, out var error))
            {
                await _s.WriteAsync("200 IP mask cleared.\r\n", ct);
            }
            else
            {
                await _s.WriteAsync($"550 Failed to update user: {error}\r\n", ct);
            }
        }

        private async Task SITE_IDENT(string arg, CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE IDENT requires admin privileges.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync("501 Usage: SITE IDENT <user> <ident>\r\n", ct);
                return;
            }

            var parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await _s.WriteAsync("501 Usage: SITE IDENT <user> <ident>\r\n", ct);
                return;
            }

            var userName = parts[0];
            var ident = parts[1];

            var user = _s.Users.FindUser(userName);
            if (user is null)
            {
                await _s.WriteAsync("550 User not found.\r\n", ct);
                return;
            }

            var updated = user with { RequiredIdent = ident };

            if (_s.Users.TryUpdateUser(updated, out var error))
            {
                await _s.WriteAsync($"200 Required ident set to '{ident}'.\r\n", ct);
            }
            else
            {
                await _s.WriteAsync($"550 Failed to update user: {error}\r\n", ct);
            }
        }

        private async Task SITE_REQIDENT(string arg, CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE REQIDENT requires admin privileges.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync("501 Usage: SITE REQIDENT <user> <on|off>\r\n", ct);
                return;
            }

            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await _s.WriteAsync("501 Usage: SITE REQIDENT <user> <on|off>\r\n", ct);
                return;
            }

            var userName = parts[0];
            var modeStr = parts[1].ToLowerInvariant();

            bool value;
            if (modeStr is "on" or "1" or "true" or "yes")
                value = true;
            else if (modeStr is "off" or "0" or "false" or "no")
                value = false;
            else
            {
                await _s.WriteAsync("501 Mode must be on|off.\r\n", ct);
                return;
            }

            var user = _s.Users.FindUser(userName);
            if (user is null)
            {
                await _s.WriteAsync("550 User not found.\r\n", ct);
                return;
            }

            var updated = user with { RequireIdentMatch = value };

            if (_s.Users.TryUpdateUser(updated, out var error))
            {
                await _s.WriteAsync($"200 Require ident set to {(value ? "ON" : "OFF")}.\r\n", ct);
            }
            else
            {
                await _s.WriteAsync($"550 Failed to update user: {error}\r\n", ct);
            }
        }

        private async Task SITE_CREDITS(string arg, CancellationToken ct)
        {
            if (_s.Account is null)
            {
                await _s.WriteAsync("550 Not logged in.\r\n", ct);
                return;
            }

            FtpUser? target;
            string who;

            if (string.IsNullOrWhiteSpace(arg))
            {
                target = _s.Account;
                who = target.UserName;
            }
            else
            {
                if (_s.Account is not { IsAdmin: true })
                {
                    await _s.WriteAsync("550 Only admin can query other users' credits.\r\n", ct);
                    return;
                }

                target = _s.Users.FindUser(arg);
                if (target is null)
                {
                    await _s.WriteAsync("550 User not found.\r\n", ct);
                    return;
                }
                who = target.UserName;
            }

            await _s.WriteAsync($"200 CREDITS {who}: {target.CreditsKb} KB\r\n", ct);
        }
        private async Task SITE_GIVECRED(string arg, CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE GIVECRED requires admin privileges.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync("501 Usage: SITE GIVECRED <user> <MB>\r\n", ct);
                return;
            }

            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !long.TryParse(parts[1], out var mb) || mb <= 0)
            {
                await _s.WriteAsync("501 Usage: SITE GIVECRED <user> <MB>\r\n", ct);
                return;
            }

            var userName = parts[0];
            var user = _s.Users.FindUser(userName);
            if (user is null)
            {
                await _s.WriteAsync("550 User not found.\r\n", ct);
                return;
            }

            long deltaKb = mb * 1024;
            var updated = user with { CreditsKb = user.CreditsKb + deltaKb };

            if (_s.Users.TryUpdateUser(updated, out var error))
            {
                await _s.WriteAsync($"200 Credits added: {mb} MB (new {updated.CreditsKb} KB).\r\n", ct);
            }
            else
            {
                await _s.WriteAsync($"550 Failed to update credits: {error}\r\n", ct);
            }
        }

        private async Task SITE_TAKECRED(string arg, CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE TAKECRED requires admin privileges.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync("501 Usage: SITE TAKECRED <user> <MB>\r\n", ct);
                return;
            }

            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !long.TryParse(parts[1], out var mb) || mb <= 0)
            {
                await _s.WriteAsync("501 Usage: SITE TAKECRED <user> <MB>\r\n", ct);
                return;
            }

            var userName = parts[0];
            var user = _s.Users.FindUser(userName);
            if (user is null)
            {
                await _s.WriteAsync("550 User not found.\r\n", ct);
                return;
            }

            long deltaKb = mb * 1024;
            var updated = user with { CreditsKb = Math.Max(0, user.CreditsKb - deltaKb) };

            if (_s.Users.TryUpdateUser(updated, out var error))
            {
                await _s.WriteAsync($"200 Credits removed: {mb} MB (new {updated.CreditsKb} KB).\r\n", ct);
            }
            else
            {
                await _s.WriteAsync($"550 Failed to update credits: {error}\r\n", ct);
            }
        }
        private async Task SITE_SECTIONS(CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE SECTIONS requires admin privileges.\r\n", ct);
                return;
            }

            var secs = _sections.GetSections();

            var sb = new StringBuilder();
            sb.AppendLine("211-Sections:");
            foreach (var s in secs)
            {
                sb.Append(" NAME=").Append(s.Name);
                sb.Append(" ROOT=").Append(s.VirtualRoot);
                sb.Append(" FREE=").Append(s.FreeLeech ? "Y" : "N");
                sb.Append(" RATIO=").Append(s.RatioUploadUnit).Append(':').Append(s.RatioDownloadUnit);
                sb.AppendLine();
            }
            sb.AppendLine("211 End");

            await _s.WriteAsync(sb.ToString().Replace("\n", "\r\n"), ct);
        }


        private async Task SITE_USERS(CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE USERS requires admin privileges.\r\n", ct);
                return;
            }

            var users = _s.Users.GetAllUsers();

            var sb = new StringBuilder();
            sb.AppendLine("211-Configured users:");
            foreach (var u in users)
            {
                sb.Append(" USER=").Append(u.UserName);
                sb.Append(" GROUP=").Append(u.GroupName ?? "-");
                sb.Append(" ADMIN=").Append(u.IsAdmin ? "Y" : "N");
                sb.Append(" FXP=").Append(u.AllowFxp ? "Y" : "N");
                sb.Append(" UP=").Append(u.AllowUpload ? "Y" : "N");
                sb.Append(" DOWN=").Append(u.AllowDownload ? "Y" : "N");
                sb.Append(" ACTIVE=").Append(u.AllowActiveMode ? "Y" : "N");
                sb.Append(" MAXLOGINS=").Append(u.MaxConcurrentLogins);
                sb.Append(" IDLE=").Append((int)u.IdleTimeout.TotalSeconds).Append("s");
                sb.Append(" UL=").Append(u.MaxUploadKbps).Append("kB/s");
                sb.Append(" DL=").Append(u.MaxDownloadKbps).Append("kB/s");
                sb.AppendLine();
            }
            sb.AppendLine("211 End");

            await _s.WriteAsync(sb.ToString().Replace("\n", "\r\n"), ct);
        }

        private async Task SITE_GROUPS(CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE GROUPS requires admin privileges.\r\n", ct);
                return;
            }

            var users = _s.Users.GetAllUsers();
            var groups = users
                .GroupBy(u => u.GroupName ?? "(none)")
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            sb.AppendLine("211-Groups:");
            foreach (var g in groups)
            {
                sb.Append(" GROUP=").Append(g.Key);
                sb.Append(" USERS=").Append(g.Count());
                sb.AppendLine();
            }
            sb.AppendLine("211 End");

            await _s.WriteAsync(sb.ToString().Replace("\n", "\r\n"), ct);
        }

        private async Task SITE_CHPASS(string arg, CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE CHPASS requires admin privileges.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync("501 Usage: SITE CHPASS <user> <newpassword>\r\n", ct);
                return;
            }

            var parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await _s.WriteAsync("501 Usage: SITE CHPASS <user> <newpassword>\r\n", ct);
                return;
            }

            var userName = parts[0];
            var newPass = parts[1];

            var existing = _s.Users.FindUser(userName);
            if (existing is null)
            {
                await _s.WriteAsync("550 User not found.\r\n", ct);
                return;
            }

            var newHash = PasswordHasher.HashPassword(newPass);
            var updated = existing with { PasswordHash = newHash };

            if (_s.Users.TryUpdateUser(updated, out var error))
            {
                await _s.WriteAsync("200 Password changed.\r\n", ct);
            }
            else
            {
                await _s.WriteAsync($"550 Failed to change password: {error}\r\n", ct);
            }
        }

        private async Task SITE_SETLIMITS(string arg, CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE SETLIMITS requires admin privileges.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync("501 Usage: SITE SETLIMITS <user> <upKbps> <downKbps>\r\n", ct);
                return;
            }

            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                await _s.WriteAsync("501 Usage: SITE SETLIMITS <user> <upKbps> <downKbps>\r\n", ct);
                return;
            }

            var userName = parts[0];
            if (!int.TryParse(parts[1], out var up) || up < 0 ||
                !int.TryParse(parts[2], out var down) || down < 0)
            {
                await _s.WriteAsync("501 Invalid limits.\r\n", ct);
                return;
            }

            var existing = _s.Users.FindUser(userName);
            if (existing is null)
            {
                await _s.WriteAsync("550 User not found.\r\n", ct);
                return;
            }

            var updated = existing with
            {
                MaxUploadKbps = up,
                MaxDownloadKbps = down
            };

            if (_s.Users.TryUpdateUser(updated, out var error))
            {
                await _s.WriteAsync("200 Limits updated.\r\n", ct);
            }
            else
            {
                await _s.WriteAsync($"550 Failed to update limits: {error}\r\n", ct);
            }
        }

        private async Task SITE_SETFLAGS(string arg, CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE SETFLAGS requires admin privileges.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync("501 Usage: SITE SETFLAGS <user> <flags>\r\n", ct);
                return;
            }

            var parts = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                await _s.WriteAsync("501 Usage: SITE SETFLAGS <user> <flags>\r\n", ct);
                return;
            }

            var userName = parts[0];
            var flagsStr = parts[1];

            var existing = _s.Users.FindUser(userName);
            if (existing is null)
            {
                await _s.WriteAsync("550 User not found.\r\n", ct);
                return;
            }

            bool isAdmin = existing.IsAdmin;
            bool allowFxp = existing.AllowFxp;
            bool allowUpload = existing.AllowUpload;
            bool allowDownload = existing.AllowDownload;
            bool allowActive = existing.AllowActiveMode;

            var flags = flagsStr.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in flags)
            {
                var f = raw.Trim();
                if (f.Length < 2) continue;

                var op = f[0];
                var name = f[1..].ToLowerInvariant();

                bool value = op == '+';

                switch (name)
                {
                    case "admin":
                        isAdmin = value;
                        break;
                    case "fxp":
                        allowFxp = value;
                        break;
                    case "upload":
                        allowUpload = value;
                        break;
                    case "download":
                        allowDownload = value;
                        break;
                    case "active":
                        allowActive = value;
                        break;
                }
            }

            var updated = existing with
            {
                IsAdmin = isAdmin,
                AllowFxp = allowFxp,
                AllowUpload = allowUpload,
                AllowDownload = allowDownload,
                AllowActiveMode = allowActive
            };

            if (_s.Users.TryUpdateUser(updated, out var error))
            {
                await _s.WriteAsync("200 Flags updated.\r\n", ct);
            }
            else
            {
                await _s.WriteAsync($"550 Failed to update flags: {error}\r\n", ct);
            }
        }

        private async Task SITE_ADDUSER(string arg, CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE ADDUSER requires admin privileges.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync("501 Usage: SITE ADDUSER <user> <password> <homedir> [group]\r\n", ct);
                return;
            }

            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                await _s.WriteAsync("501 Usage: SITE ADDUSER <user> <password> <homedir> [group]\r\n", ct);
                return;
            }

            var userName = parts[0];
            var password = parts[1];
            var homeDir = parts[2];
            var group = parts.Length >= 4 ? parts[3] : null;

            var passwordHash = PasswordHasher.HashPassword(password);

            var user = new FtpUser(
                UserName: userName,
                PasswordHash: passwordHash,
                HomeDir: homeDir,
                IsAdmin: false,
                AllowFxp: false,          // default: FXP off
                AllowUpload: true,
                AllowDownload: true,
                AllowActiveMode: true,
                MaxConcurrentLogins: 3,
                IdleTimeout: TimeSpan.FromMinutes(30),
                MaxUploadKbps: 0,
                MaxDownloadKbps: 0,
                GroupName: group,
                CreditsKb: 0,             // start with 0 credits, admin can GIVECRED/SITE CREDITS
                AllowedIpMask: null,
                RequireIdentMatch: false,
                RequiredIdent: null
            );

            if (_s.Users.TryAddUser(user, out var error))
                await _s.WriteAsync("200 User added.\r\n", ct);
            else
                await _s.WriteAsync($"550 Failed to add user: {error}\r\n", ct);
        }

        private async Task SITE_GADDUSER(string arg, CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE GADDUSER requires admin privileges.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync("501 Usage: SITE GADDUSER <group> <user>\r\n", ct);
                return;
            }

            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await _s.WriteAsync("501 Usage: SITE GADDUSER <group> <user>\r\n", ct);
                return;
            }

            var group = parts[0];
            var userName = parts[1];

            var existing = _s.Users.FindUser(userName);
            if (existing is null)
            {
                await _s.WriteAsync("550 User not found.\r\n", ct);
                return;
            }

            var updated = existing with { GroupName = group };

            if (_s.Users.TryUpdateUser(updated, out var error))
            {
                await _s.WriteAsync("200 User assigned to group.\r\n", ct);
            }
            else
            {
                await _s.WriteAsync($"550 Failed to update user: {error}\r\n", ct);
            }
        }

        private async Task SITE_WHO(CancellationToken ct)
        {
            // Admin-only
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE WHO requires admin privileges.\r\n", ct);
                return;
            }

            var sessions = FtpSession.GetActiveSessions();

            var sb = new StringBuilder();
            sb.AppendLine("211-Active sessions:");
            foreach (var sess in sessions.OrderBy(x => x.SessionId))
            {
                var ep = sess.Control.Client.RemoteEndPoint?.ToString() ?? "unknown";
                var user = sess.UserName ?? "(not logged in)";
                var idle = (DateTimeOffset.UtcNow - sess.LastActivity).TotalSeconds;
                sb.AppendLine($" ID={sess.SessionId} USER={user} IP={ep} IDLE={idle:F0}s");
            }
            sb.AppendLine("211 End");

            await _s.WriteAsync(sb.ToString().Replace("\n", "\r\n"), ct);
        }

        private async Task SITE_KILL(string arg, CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE KILL requires admin privileges.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg) || !int.TryParse(arg, out var id))
            {
                await _s.WriteAsync("501 Usage: SITE KILL <session-id>\r\n", ct);
                return;
            }

            var target = FtpSession.GetActiveSessions().FirstOrDefault(s => s.SessionId == id);
            if (target is null)
            {
                await _s.WriteAsync("550 No such session.\r\n", ct);
                return;
            }

            if (ReferenceEquals(target, _s))
            {
                await _s.WriteAsync("550 Cannot kill your own session with SITE KILL.\r\n", ct);
                return;
            }

            try
            {
                target.MarkQuit();
                try { target.Control.Close(); } catch { /* ignore */ }
                await _s.WriteAsync("200 Session killed.\r\n", ct);
            }
            catch
            {
                await _s.WriteAsync("550 Failed to kill session.\r\n", ct);
            }
        }

        private async Task SITE_CHMOD(string arg, CancellationToken ct)
        {
            if (_s.Account is not { IsAdmin: true })
            {
                await _s.WriteAsync("550 SITE CHMOD requires admin privileges.\r\n", ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await _s.WriteAsync("501 Usage: SITE CHMOD <mode> <path>\r\n", ct);
                return;
            }

            var parts = arg.Split(' ', 2, StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                await _s.WriteAsync("501 Usage: SITE CHMOD <mode> <path>\r\n", ct);
                return;
            }

            var modeStr = parts[0];
            var pathArg = parts[1];

            if (!int.TryParse(modeStr, out var mode) || mode <= 0)
            {
                await _s.WriteAsync("501 Invalid mode.\r\n", ct);
                return;
            }

            var virt = FtpPath.Normalize(_s.Cwd, pathArg);
            string phys;
            try
            {
                phys = _fs.MapToPhysical(virt);
            }
            catch
            {
                await _s.WriteAsync("550 Permission denied.\r\n", ct);
                return;
            }

            if (!File.Exists(phys) && !Directory.Exists(phys))
            {
                await _s.WriteAsync("550 File or directory not found.\r\n", ct);
                return;
            }

            try
            {
                // Very rough semantics:
                // If owner's write bit is 0 (e.g. 444), mark as ReadOnly.
                // If owner's write bit is 1 (e.g. 644, 755, 777), clear ReadOnly.
                var ownerWritable = ((mode / 10) % 10) >= 2; // second digit, >=2 => write

                if (File.Exists(phys))
                {
                    var attrs = File.GetAttributes(phys);

                    if (ownerWritable)
                        attrs &= ~FileAttributes.ReadOnly;
                    else
                        attrs |= FileAttributes.ReadOnly;

                    File.SetAttributes(phys, attrs);
                }
                else if (Directory.Exists(phys))
                {
                    var attrs = File.GetAttributes(phys);

                    if (ownerWritable)
                        attrs &= ~FileAttributes.ReadOnly;
                    else
                        attrs |= FileAttributes.ReadOnly;

                    File.SetAttributes(phys, attrs);
                }

                await _s.WriteAsync("200 CHMOD applied (best effort).\r\n", ct);
            }
            catch
            {
                await _s.WriteAsync("550 Failed to change mode.\r\n", ct);
            }
        }
    }
}
