using amFTPd.Config.Daemon;
using amFTPd.Core.Stats;
using amFTPd.Logging;
using System.Net;
using System.Text;

namespace amFTPd.Core.Monitoring;

/// <summary>
/// Provides an HTTP endpoint that exposes Prometheus-compatible metrics for the FTP server.
/// </summary>
/// <remarks>The metrics endpoint listens on the specified address and port, serving real-time statistics about
/// server activity, performance counters, and per-section usage. Metrics are formatted according to the Prometheus
/// exposition format and can be scraped by monitoring systems. This class is intended for internal use and is not
/// thread-safe. Dispose the instance asynchronously to ensure proper shutdown of the HTTP listener and background
/// tasks.</remarks>
public sealed class MetricsEndpoint : IAsyncDisposable
{
    private readonly AmFtpdRuntimeConfig _runtime;
    private readonly IFtpLogger _log;
    private readonly HttpListener _listener = new();
    private readonly string _prefix;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public MetricsEndpoint(
        AmFtpdRuntimeConfig runtime,
        IFtpLogger log,
        string bindAddress,
        int port)
    {
        _runtime = runtime;
        _log = log;

        _prefix = $"http://{NormalizeHost(bindAddress)}:{port}/metrics/";
        _listener.Prefixes.Add(_prefix);
    }

    public void Start()
    {
        if (!HttpListener.IsSupported)
            return;

        _cts = new CancellationTokenSource();
        _listener.Start();

        _loop = Task.Run(() => RunAsync(_cts.Token));
        _log.Log(FtpLogLevel.Info, $"[Metrics] Prometheus endpoint listening at {_prefix}");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => HandleAsync(ctx), ct);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var res = ctx.Response;
            res.ContentType = "text/plain; version=0.0.4";

            var sb = new StringBuilder(4096);

            // ------------------------------------------------------------
            // Global PerfCounters
            // ------------------------------------------------------------
            var p = PerfCounters.GetSnapshot();

            sb.AppendLine("# TYPE amftpd_connections_active gauge");
            sb.AppendLine($"amftpd_connections_active {p.ActiveConnections}");

            sb.AppendLine("# TYPE amftpd_connections_total counter");
            sb.AppendLine($"amftpd_connections_total {p.TotalConnections}");

            sb.AppendLine("# TYPE amftpd_commands_total counter");
            sb.AppendLine($"amftpd_commands_total {p.TotalCommands}");

            sb.AppendLine("# TYPE amftpd_failed_logins_total counter");
            sb.AppendLine($"amftpd_failed_logins_total {p.FailedLogins}");

            sb.AppendLine("# TYPE amftpd_aborted_transfers_total counter");
            sb.AppendLine($"amftpd_aborted_transfers_total {p.AbortedTransfers}");

            sb.AppendLine("# TYPE amftpd_bytes_uploaded_total counter");
            sb.AppendLine($"amftpd_bytes_uploaded_total {p.BytesUploaded}");

            sb.AppendLine("# TYPE amftpd_bytes_downloaded_total counter");
            sb.AppendLine($"amftpd_bytes_downloaded_total {p.BytesDownloaded}");

            sb.AppendLine("# TYPE amftpd_transfers_active gauge");
            sb.AppendLine($"amftpd_transfers_active {p.ActiveTransfers}");

            sb.AppendLine("# TYPE amftpd_transfers_total counter");
            sb.AppendLine($"amftpd_transfers_total {p.TotalTransfers}");

            sb.AppendLine("# TYPE amftpd_transfers_max_concurrent gauge");
            sb.AppendLine($"amftpd_transfers_max_concurrent {p.MaxConcurrentTransfers}");

            // ------------------------------------------------------------
            // Rolling window rates
            // ------------------------------------------------------------
            var r = _runtime.RollingStats;

            sb.AppendLine("# TYPE amftpd_transfers_rate gauge");
            sb.AppendLine($"amftpd_transfers_rate{{window=\"5s\"}} {r.Transfers5s.RatePerSecond():0.###}");
            sb.AppendLine($"amftpd_transfers_rate{{window=\"1m\"}} {r.Transfers1m.RatePerSecond():0.###}");
            sb.AppendLine($"amftpd_transfers_rate{{window=\"5m\"}} {r.Transfers5m.RatePerSecond():0.###}");

            sb.AppendLine("# TYPE amftpd_upload_bytes_rate gauge");
            sb.AppendLine($"amftpd_upload_bytes_rate{{window=\"5s\"}} {r.UploadBytes5s.RatePerSecond():0.###}");
            sb.AppendLine($"amftpd_upload_bytes_rate{{window=\"1m\"}} {r.UploadBytes1m.RatePerSecond():0.###}");
            sb.AppendLine($"amftpd_upload_bytes_rate{{window=\"5m\"}} {r.UploadBytes5m.RatePerSecond():0.###}");

            sb.AppendLine("# TYPE amftpd_download_bytes_rate gauge");
            sb.AppendLine($"amftpd_download_bytes_rate{{window=\"5s\"}} {r.DownloadBytes5s.RatePerSecond():0.###}");
            sb.AppendLine($"amftpd_download_bytes_rate{{window=\"1m\"}} {r.DownloadBytes1m.RatePerSecond():0.###}");
            sb.AppendLine($"amftpd_download_bytes_rate{{window=\"5m\"}} {r.DownloadBytes5m.RatePerSecond():0.###}");

            // ------------------------------------------------------------
            // Per-section live stats (bounded cardinality)
            // ------------------------------------------------------------
            foreach (var sec in _runtime.LiveStats.Sections.Values)
            {
                var label = sec.SectionName.Replace("\"", "");

                sb.AppendLine($"amftpd_section_uploads{{section=\"{label}\"}} {sec.Uploads}");
                sb.AppendLine($"amftpd_section_downloads{{section=\"{label}\"}} {sec.Downloads}");
                sb.AppendLine($"amftpd_section_bytes_uploaded{{section=\"{label}\"}} {sec.BytesUploaded}");
                sb.AppendLine($"amftpd_section_bytes_downloaded{{section=\"{label}\"}} {sec.BytesDownloaded}");
                sb.AppendLine($"amftpd_section_active_users{{section=\"{label}\"}} {sec.ActiveUsers}");
            }

            var bytes = Encoding.ASCII.GetBytes(sb.ToString());
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            _log.Log(FtpLogLevel.Debug, $"[Metrics] scrape failed: {ex.Message}", ex);
        }
        finally
        {
            try { ctx.Response.OutputStream.Close(); } catch { }
        }
    }

    private string NormalizeHost(string bindAddress)
    {
        if (string.IsNullOrWhiteSpace(bindAddress)) return "localhost";
        if (bindAddress is "0.0.0.0" or "::") return "+"; // “all interfaces” for HttpListener
        return bindAddress;
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();

        if (_loop is not null)
            await _loop;

        _listener.Stop();
        _listener.Close();
    }
}
