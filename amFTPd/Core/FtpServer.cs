using amFTPd.Logging;
using amFTPd.Security;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using amFTPd.Utils;
using amFTPd.Config.Ftpd;

namespace amFTPd.Core;

public sealed class FtpServer
{
    private readonly FtpConfig _cfg;
    private readonly IUserStore _users;
    private readonly TlsConfig _tls;
    private readonly IFtpLogger _log;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly SectionManager _sections;

    public FtpServer(FtpConfig cfg, IUserStore users, TlsConfig tls, IFtpLogger log, SectionManager sections)
    {
        _cfg = cfg;
        _users = users;
        _tls = tls;
        _log = log;
        _sections = sections;
    }

    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(new IPEndPoint(_cfg.BindAddress, _cfg.Port));
        _listener.Start();
        _log.Log(FtpLogLevel.Info, $"FTP(S) server listening on {_cfg.BindAddress}:{_cfg.Port}");

        var fs = new FtpFileSystem(_cfg.RootPath);

        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Error, "Accept failed", ex);
                continue;
            }

            _ = Task.Run(async () =>
            {
                var rem = client.Client.RemoteEndPoint?.ToString() ?? "??";
                _log.Log(FtpLogLevel.Info, $"Connection from {rem}");

                await using var session = new FtpSession(
                    client,
                    _log,
                    _cfg,
                    _users,
                    fs,
                    _cfg.DataChannelProtectionDefault,
                    _tls);

                var router = new FtpCommandRouter(session, _log, fs, _cfg, _tls, _sections);
                var ct = _cts!.Token;

                try
                {
                    await session.RunAsync(router, ct);
                }
                catch (Exception ex)
                {
                    _log.Log(FtpLogLevel.Error, $"Unhandled session error for {rem}", ex);
                }

                _log.Log(FtpLogLevel.Info, $"Connection closed: {rem}");
            });
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _log.Log(FtpLogLevel.Info, "Server stopped.");
    }
}