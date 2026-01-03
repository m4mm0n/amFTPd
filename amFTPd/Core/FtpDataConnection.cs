/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpDataConnection.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-14 17:19:02
 *  CRC32:          0x8FA650BE
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
 * ====================================================================================================
 */


using amFTPd.Config.Daemon;
using amFTPd.Core.Stats;
using amFTPd.Logging;
using amFTPd.Security;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using amFTPd.Utils;

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
    private readonly AmFtpdRuntimeConfig _runtime;

    private TcpClient? _client;
    private TcpListener? _listener;
    private Stream? _stream;

    // Shared buffer pool for all data transfers to avoid per-transfer allocations.
    internal static readonly ArrayPool<byte> BufferPool = ArrayPool<byte>.Shared;

    /// <summary>
    /// Default buffer size used for uploads/downloads.
    /// Exposed so higher layers can align their buffers if they want.
    /// </summary>
    internal const int TransferBufferSize = 64 * 1024;

    private long _bytesTransferred;
    #endregion

    /// <summary>
    /// Gets the total number of bytes that have been transferred.
    /// </summary>
    public long BytesTransferred => _bytesTransferred;

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
    public FtpDataConnection(
        IFtpLogger log,
        TlsConfig tls,
        bool controlTlsActive,
        string protectionMode,
        AmFtpdRuntimeConfig runtime)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _tls = tls ?? throw new ArgumentNullException(nameof(tls));
        _controlTlsActive = controlTlsActive;
        _prot = protectionMode ?? "C";
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    /// <summary>
    /// Establishes an active data connection to the specified remote endpoint.
    /// </summary>
    public async Task SetActiveAsync(IPEndPoint remoteEndPoint, CancellationToken ct)
    {
        await DisposeAsync().ConfigureAwait(false);

        if (_controlTlsActive &&
            !_prot.Equals("P", StringComparison.OrdinalIgnoreCase) &&
            _tls.RefuseClearDataOnSecureControl)
        {
            throw new InvalidOperationException(
                "Clear-text data channel is refused because control is TLS and policy forbids it.");
        }

        _log.Log(FtpLogLevel.Debug, $"DATA(ACTIVE): Connecting to {remoteEndPoint}...");
        _client = new TcpClient { NoDelay = true };

        await _client.ConnectAsync(remoteEndPoint.Address, remoteEndPoint.Port, ct)
            .ConfigureAwait(false);

        var baseStream = _client.GetStream();
        _stream = await WrapAsync(baseStream, ct).ConfigureAwait(false);
        Mode = FtpTransferMode.Active;

        _log.Log(FtpLogLevel.Debug, "DATA(ACTIVE): Connected.");
    }

    /// <summary>
    /// Starts a passive FTP listener on the specified IP address and port.
    /// </summary>
    public async Task<int> StartPassiveAsync(IPAddress bindAddress, int port, CancellationToken ct)
    {
        await DisposeAsync().ConfigureAwait(false);

        if (_controlTlsActive &&
            !_prot.Equals("P", StringComparison.OrdinalIgnoreCase) &&
            _tls.RefuseClearDataOnSecureControl)
        {
            throw new InvalidOperationException(
                "Clear-text passive data channel is refused because control is TLS and policy forbids it.");
        }

        var ep = new IPEndPoint(bindAddress, port);
        _listener = new TcpListener(ep);
        _listener.Start();

        _log.Log(FtpLogLevel.Debug, $"DATA(PASSIVE): Listening on {ep}.");
        Mode = FtpTransferMode.Passive;

        return ((IPEndPoint)_listener.LocalEndpoint).Port;
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
            var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);

            _log.Log(FtpLogLevel.Debug,
                $"DATA(PASSIVE): Client connected from {client.Client.RemoteEndPoint}.");

            client.NoDelay = true;
            _client = client;

            var baseStream = client.GetStream();
            _stream = await WrapAsync(baseStream, ct).ConfigureAwait(false);
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

        if (_tls.Certificate is null)
            throw new InvalidOperationException(
                "Data channel TLS requested but no certificate is configured.");

        var ssl = new SslStream(baseStream, leaveInnerStreamOpen: false);

        try
        {
            var options = _tls.CreateServerOptions();
            if (options.ServerCertificate is null)
                throw new InvalidOperationException(
                    "TlsConfig returned no ServerCertificate for data channel.");

            await ssl.AuthenticateAsServerAsync(options, ct).ConfigureAwait(false);
            _log.Log(FtpLogLevel.Debug, "DATA: TLS handshake successful on data connection.");
            return ssl;
        }
        catch
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Sends data asynchronously over the established connection.
    /// </summary>
    public async Task<long> SendAsync(
        Func<Stream, Task<long>> send,
        CancellationToken ct)
    {
        await EnsureConnectedAsync(ct).ConfigureAwait(false);

        if (_stream is null)
            throw new InvalidOperationException("Data stream not available.");

        PerfCounters.ObserveTransferStarted();
        _runtime.RollingStats.Transfers5s.Add(1);
        _runtime.RollingStats.Transfers1m.Add(1);
        _runtime.RollingStats.Transfers5m.Add(1);

        var sw = Stopwatch.StartNew();

        try
        {
            var transferred = await send(_stream).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
            return transferred;
        }
        finally
        {
            sw.Stop();
            PerfCounters.ObserveTransferCompleted(sw.Elapsed);
        }
    }

    /// <summary>
    /// Asynchronously releases the resources used by the current instance.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try { if (_stream != null) await _stream.DisposeAsync().ConfigureAwait(false); } catch { }
        try { _client?.Close(); } catch { }
        try { _listener?.Stop(); } catch { }

        _stream = null;
        _client = null;
        _listener = null;
        Mode = FtpTransferMode.None;
    }
}