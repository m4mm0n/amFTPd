using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using amFTPd.Logging;
using amFTPd.Security;

namespace amFTPd.Core;

internal sealed class FtpDataConnection : IAsyncDisposable
{
    private readonly IFtpLogger _log;
    private readonly TlsConfig _tls;
    private readonly bool _controlTlsActive;
    private readonly string _prot; // "C" or "P"

    private TcpClient? _client;
    private TcpListener? _listener;
    private Stream? _stream;

    public FtpTransferMode Mode { get; private set; } = FtpTransferMode.None;
    public IPEndPoint? PassiveEndPoint => _listener?.LocalEndpoint as IPEndPoint;

    private bool UseTlsOnData =>
        _controlTlsActive && _prot.Equals("P", StringComparison.OrdinalIgnoreCase);

    public FtpDataConnection(IFtpLogger log, TlsConfig tls, bool controlTlsActive, string protectionMode)
    {
        _log = log;
        _tls = tls;
        _controlTlsActive = controlTlsActive;
        _prot = protectionMode;
    }

    public async Task SetActiveAsync(IPEndPoint remoteEndPoint, CancellationToken ct)
    {
        await DisposeAsync();

        _log.Log(FtpLogLevel.Debug, $"DATA(ACTIVE): Connecting to {remoteEndPoint}...");
        _client = new TcpClient();
        await _client.ConnectAsync(remoteEndPoint.Address, remoteEndPoint.Port, ct);
        _stream = await WrapAsync(_client.GetStream(), ct);
        Mode = FtpTransferMode.Active;
        _log.Log(FtpLogLevel.Debug, "DATA(ACTIVE): Connected.");
    }

    public async Task<int> StartPassiveAsync(IPAddress bindAddress, int port, CancellationToken ct)
    {
        await DisposeAsync();

        var ep = new IPEndPoint(bindAddress, port);
        _listener = new TcpListener(ep);
        _listener.Start();
        Mode = FtpTransferMode.Passive;

        var actual = (IPEndPoint)_listener.LocalEndpoint;
        _log.Log(FtpLogLevel.Debug, $"DATA(PASSIVE): Listening on {actual}.");
        return actual.Port;
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (Mode == FtpTransferMode.Passive)
        {
            if (_stream != null)
                return;

            if (_listener == null)
                throw new InvalidOperationException("Passive listener not started.");

            _log.Log(FtpLogLevel.Debug, "DATA(PASSIVE): Waiting for incoming data connection...");
            var client = await _listener.AcceptTcpClientAsync(ct);
            _client = client;
            _stream = await WrapAsync(client.GetStream(), ct);
            _log.Log(FtpLogLevel.Debug, "DATA(PASSIVE): Client connected.");
        }
        else if (Mode == FtpTransferMode.Active)
        {
            if (_stream == null)
                throw new InvalidOperationException("Active data connection not established.");
        }
        else
        {
            throw new InvalidOperationException("No data transfer mode set.");
        }
    }

    private async Task<Stream> WrapAsync(Stream baseStream, CancellationToken ct)
    {
        if (!UseTlsOnData)
            return baseStream;

        var ssl = new SslStream(baseStream, leaveInnerStreamOpen: false);
        await ssl.AuthenticateAsServerAsync(_tls.CreateServerOptions(), ct);
        return ssl;
    }

    public async Task SendAsync(Func<Stream, Task> send, CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);

        if (_stream == null)
            throw new InvalidOperationException("Data stream not available.");

        await send(_stream);
        await _stream.FlushAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        try { _stream?.Dispose(); } catch { /* ignore */ }
        try { _client?.Close(); } catch { /* ignore */ }
        try { _listener?.Stop(); } catch { /* ignore */ }

        _stream = null;
        _client = null;
        _listener = null;
        Mode = FtpTransferMode.None;

        await Task.CompletedTask;
    }
}