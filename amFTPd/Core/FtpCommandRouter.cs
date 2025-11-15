using amFTPd.Config.Ftpd;
using amFTPd.Logging;
using amFTPd.Security;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace amFTPd.Core;

internal sealed partial class FtpCommandRouter
{
    private readonly FtpSession _s;
    private readonly IFtpLogger _log;
    private readonly FtpFileSystem _fs;
    private readonly FtpConfig _cfg;
    private readonly TlsConfig _tls;
    private readonly SectionManager _sections;

    public FtpCommandRouter(FtpSession s, IFtpLogger log, FtpFileSystem fs, FtpConfig cfg, TlsConfig tls, SectionManager sections)
    {
        _s = s;
        _log = log;
        _fs = fs;
        _cfg = cfg;
        _tls = tls;
        _sections = sections;
    }

    public async Task HandleAsync(string line, CancellationToken ct)
    {
        var sp = line.Split(' ', 2, StringSplitOptions.TrimEntries);
        var cmd = sp[0].ToUpperInvariant();
        var arg = sp.Length > 1 ? sp[1] : string.Empty;

        _s.Touch();
        _log.Log(FtpLogLevel.Debug, $"CMD: {cmd} ARG: {arg}");

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

            // SITE stub
            case "SITE": await SITE(arg, ct); break;

            default:
                await _s.WriteAsync(FtpResponses.UnknownCmd, ct);
                break;
        }
    }
    private FtpSection GetSectionForVirtual(string virtPath)
        => _sections.GetSectionForPath(virtPath);
    private static async Task<long> CopyWithThrottleAsync(
        Stream source,
        Stream destination,
        int maxKbps,
        CancellationToken ct)
    {
        const int bufferSize = 16 * 1024;
        var buffer = new byte[bufferSize];

        // Unlimited: we still count bytes manually
        if (maxKbps <= 0)
        {
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
            }
            return total;
        }

        long totalBytes = 0;
        long bytesPerSecondLimit = maxKbps * 1024L;
        long bytesThisWindow = 0;
        long windowStartTicks = Stopwatch.GetTimestamp();
        double ticksPerSecond = Stopwatch.Frequency;

        while (true)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read <= 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            totalBytes += read;
            bytesThisWindow += read;

            var elapsedSeconds = (Stopwatch.GetTimestamp() - windowStartTicks) / ticksPerSecond;
            if (elapsedSeconds > 0)
            {
                var currentRate = bytesThisWindow / elapsedSeconds; // bytes/s
                if (currentRate > bytesPerSecondLimit)
                {
                    var desiredSeconds = (double)bytesThisWindow / bytesPerSecondLimit;
                    var sleepSeconds = desiredSeconds - elapsedSeconds;
                    if (sleepSeconds > 0)
                    {
                        var delayMs = (int)(sleepSeconds * 1000.0);
                        if (delayMs > 0)
                            await Task.Delay(delayMs, ct);
                    }
                }

                if (elapsedSeconds >= 1.0)
                {
                    windowStartTicks = Stopwatch.GetTimestamp();
                    bytesThisWindow = 0;
                }
            }
        }

        return totalBytes;
    }
    private async Task<bool> CheckDownloadCreditsAsync(FtpSection section, long bytes, CancellationToken ct)
    {
        var account = _s.Account;
        if (account is null) return true;
        if (section.FreeLeech) return true;

        long kb = bytes / 1024;
        if (kb <= 0) return true;

        if (account.CreditsKb < kb)
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

        long kb = bytes / 1024;
        if (kb <= 0) return;

        var updated = account with { CreditsKb = Math.Max(0, account.CreditsKb - kb) };
        if (_s.Users.TryUpdateUser(updated, out _))
            _s.SetAccount(updated);
    }

    private void ApplyUploadCredits(FtpSection section, long bytes)
    {
        var account = _s.Account;
        if (account is null) return;
        if (bytes <= 0) return;

        long kb = bytes / 1024;
        if (kb <= 0) return;

        long delta;
        if (!section.FreeLeech &&
            section.RatioUploadUnit > 0 &&
            section.RatioDownloadUnit > 0)
        {
            var factor = (double)section.RatioDownloadUnit / section.RatioUploadUnit;
            delta = (long)Math.Round(kb * factor);
        }
        else
        {
            // Default 1:1 if free-leech or ratio not configured properly
            delta = kb;
        }

        var updated = account with { CreditsKb = account.CreditsKb + delta };
        if (_s.Users.TryUpdateUser(updated, out _))
            _s.SetAccount(updated);
    }

    private static bool IsIpAllowed(IPAddress addr, string mask)
    {
        // CIDR: 1.2.3.0/24
        if (mask.Contains('/'))
        {
            return IsIpInCidr(addr, mask);
        }

        // Wildcard v4: 1.2.3.* or 10.*.*.*
        if (mask.Contains('*'))
        {
            if (addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return false;

            var addrParts = addr.ToString().Split('.');
            var maskParts = mask.Split('.');

            if (addrParts.Length != 4 || maskParts.Length != 4)
                return false;

            for (int i = 0; i < 4; i++)
            {
                if (maskParts[i] == "*")
                    continue;
                if (!string.Equals(maskParts[i], addrParts[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        // Exact match
        if (IPAddress.TryParse(mask, out var exact))
            return addr.Equals(exact);

        return false;
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

        int bits = prefixLength;
        for (int i = 0; i < addrBytes.Length && bits > 0; i++)
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
        using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true);
        using var reader = new StreamReader(stream, Encoding.ASCII, false, leaveOpen: true);

        // Request: "server-port , client-port\r\n"
        // server-port = our local port; client-port = their remote port
        var query = $"{localEp.Port} , {remoteEp.Port}\r\n";
        await writer.WriteAsync(query);
        await writer.FlushAsync();

        var line = await reader.ReadLineAsync();
        if (string.IsNullOrEmpty(line))
            return null;

        // Expected: "<local> , <remote> : USERID : <OS> : <USER>"
        var parts = line.Split(':');
        if (parts.Length < 4)
            return null;

        var userPart = parts[3].Trim();
        return string.IsNullOrWhiteSpace(userPart) ? null : userPart;
    }

}