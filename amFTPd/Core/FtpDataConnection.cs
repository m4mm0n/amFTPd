/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-20
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
        _controlTlsActive && _prot.Equals("P", StringComparison.OrdinalIgnoreCase);
    /// <summary>
    /// Initializes a new instance of the <see cref="FtpDataConnection"/> class.
    /// </summary>
    /// <param name="log">The logger instance used to record FTP data connection events.</param>
    /// <param name="tls">The TLS configuration for securing the data connection.</param>
    /// <param name="controlTlsActive">Indicates whether the control connection is currently secured with TLS.</param>
    /// <param name="protectionMode">The protection mode for the data connection, specifying the level of security to apply.</param>
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
    /// <remarks>This method disposes of any existing data connection before establishing a new one. The
    /// connection is established in active mode, and the transfer mode is set to <see
    /// cref="FtpTransferMode.Active"/>.</remarks>
    /// <param name="remoteEndPoint">The <see cref="IPEndPoint"/> representing the remote server to connect to.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to cancel the operation.</param>
    /// <returns></returns>
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
    /// <summary>
    /// Starts a passive FTP listener on the specified IP address and port.
    /// </summary>
    /// <remarks>This method initializes a new TCP listener for passive FTP data connections and starts
    /// listening on the specified endpoint. If the specified port is 0, the system will automatically assign an
    /// available port. The method disposes of any previously active listener before starting a new one.</remarks>
    /// <param name="bindAddress">The IP address to bind the listener to.</param>
    /// <param name="port">The port number to listen on. Specify 0 to allow the system to select an available port.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.</param>
    /// <returns>The port number on which the listener is actively listening.</returns>
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
    
    /// <summary>
    /// Sends data asynchronously over the established connection.
    /// </summary>
    /// <remarks>The connection must be established before calling this method. If the connection is not
    /// established, the method will attempt to establish it. The provided <paramref name="send"/> delegate is
    /// responsible for writing data to the stream.</remarks>
    /// <param name="send">A delegate that performs the data transmission using the provided <see cref="Stream"/>.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">Thrown if the data stream is not available.</exception>
    public async Task SendAsync(Func<Stream, Task> send, CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);

        if (_stream == null)
            throw new InvalidOperationException("Data stream not available.");

        await send(_stream);
        await _stream.FlushAsync(ct);
    }
    /// <summary>
    /// Asynchronously releases the resources used by the current instance of the class.
    /// </summary>
    /// <remarks>This method disposes of the underlying stream, client, and listener resources, if they are
    /// not null.  It also resets the transfer mode to <see cref="FtpTransferMode.None"/>. Exceptions during resource 
    /// disposal are caught and ignored. Call this method when the instance is no longer needed to ensure  proper
    /// cleanup of resources.</remarks>
    /// <returns></returns>
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