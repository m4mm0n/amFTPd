/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpDataConnection.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xDF07B6A8
 *  
 *  Description:
 *      Represents a data connection for FTP transfers, supporting both active and passive modes, with optional TLS encryptio...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using amFTPd.Logging;
using amFTPd.Security;

namespace amFTPd.Core;

/// <summary>
/// Represents a data connection for FTP transfers, supporting both active and passive modes, with optional TLS
/// encryption for secure data transmission.
/// </summary>
/// <remarks>This class manages the lifecycle of the data connection, including establishing connections, handling
/// secure streams, and disposing of resources. It supports both active and passive transfer modes, which can be
/// configured using the <see cref="SetActiveAsync"/> and <see cref="StartPassiveAsync"/> methods respectively. The
/// connection can optionally use TLS encryption if enabled on the control connection and specified by the protection
/// mode.</remarks>
internal sealed class FtpDataConnection : IAsyncDisposable
{
    #region Private Fields
    private readonly IFtpLogger _log;
    private readonly TlsConfig _tls;
    private readonly bool _controlTlsActive;
    private readonly string _prot; // "C" or "P"

    private TcpClient? _client;
    private TcpListener? _listener;
    private Stream? _stream;
    #endregion

    /// <summary>
    /// Gets the current transfer mode for the FTP operation.
    /// </summary>
    public FtpTransferMode Mode { get; private set; } = FtpTransferMode.None;

    /// <summary>
    /// Gets the local endpoint that the listener is bound to in passive mode.
    /// </summary>
    public IPEndPoint? PassiveEndPoint => _listener?.LocalEndpoint as IPEndPoint;

    private bool UseTlsOnData =>
        _controlTlsActive &&
        _prot.Equals("P", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="FtpDataConnection"/> class.
    /// </summary>
    /// <param name="log">The logger instance used to record FTP data connection events.</param>
    /// <param name="tls">The TLS configuration for securing the data connection.</param>
    /// <param name="controlTlsActive">Indicates whether the control connection is currently secured with TLS.</param>
    /// <param name="protectionMode">The protection mode for the data connection, specifying the level of security to apply ("C" or "P").</param>
    public FtpDataConnection(IFtpLogger log, TlsConfig tls, bool controlTlsActive, string protectionMode)
    {
        _log = log;
        _tls = tls;
        _controlTlsActive = controlTlsActive;
        _prot = protectionMode;
    }

    /// <summary>
    /// Establishes an active data connection to the specified remote endpoint.
    /// </summary>
    public async Task SetActiveAsync(IPEndPoint remoteEndPoint, CancellationToken ct)
    {
        await DisposeAsync();

        _log.Log(FtpLogLevel.Debug, $"DATA(ACTIVE): Connecting to {remoteEndPoint}...");
        _client = new TcpClient();
        await _client.ConnectAsync(remoteEndPoint.Address, remoteEndPoint.Port, ct);

        var baseStream = _client.GetStream();
        _stream = await WrapAsync(baseStream, ct);
        Mode = FtpTransferMode.Active;

        _log.Log(FtpLogLevel.Debug, "DATA(ACTIVE): Connected.");
    }

    /// <summary>
    /// Starts a passive FTP listener on the specified IP address and port.
    /// </summary>
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

            if (_listener is null)
                throw new InvalidOperationException("Passive listener not started.");

            _log.Log(FtpLogLevel.Debug, "DATA(PASSIVE): Waiting for incoming data connection...");
            var client = await _listener.AcceptTcpClientAsync(ct);
            _log.Log(FtpLogLevel.Debug, $"DATA(PASSIVE): Client connected from {client.Client.RemoteEndPoint}.");

            _client = client;
            var baseStream = client.GetStream();
            _stream = await WrapAsync(baseStream, ct);
        }
        else if (Mode == FtpTransferMode.Active)
        {
            if (_stream is null)
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

        if (_tls?.Certificate is null)
            throw new InvalidOperationException("Data channel TLS requested but no certificate is configured.");

        var ssl = new SslStream(baseStream, leaveInnerStreamOpen: false);

        try
        {
            var options = _tls.CreateServerOptions();
            if (options.ServerCertificate is null)
                throw new InvalidOperationException("TlsConfig returned no ServerCertificate for data channel.");

            await ssl.AuthenticateAsServerAsync(options, ct).ConfigureAwait(false);
            _log.Log(FtpLogLevel.Debug, "DATA: TLS handshake successful on data connection.");
            return ssl;
        }
        catch (Exception ex)
        {
            _log.Log(FtpLogLevel.Error, $"DATA: TLS handshake failed on data connection: {ex}");
            ssl.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Sends data asynchronously over the established connection.
    /// </summary>
    public async Task SendAsync(Func<Stream, Task> send, CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);

        if (_stream is null)
            throw new InvalidOperationException("Data stream not available.");

        await send(_stream);
        await _stream.FlushAsync(ct);
    }

    /// <summary>
    /// Asynchronously releases the resources used by the current instance.
    /// </summary>
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