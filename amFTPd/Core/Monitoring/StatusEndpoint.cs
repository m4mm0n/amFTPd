/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           StatusEndpoint.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 16:58:26
 *  Last Modified:  2025-12-14 17:18:34
 *  CRC32:          0x8B21A04E
 *  
 *  Description:
 *      Minimal HTTP status endpoint based on <see cref="HttpListener"/>. Intended for local monitoring / health checks.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
 }
            }
        }

        private async Task WriteStatusAsync(HttpListenerResponse res)
        {
            var now = DateTimeOffset.UtcNow;
            var ftpCfg = _runtime.FtpConfig;

            var perf = PerfCounters.GetSnapshot();
            var activeSessions = _runtime.EventBus.GetActiveSessions().Count;

            // Best-effort daily stats from session log
            object? daily = null;
            try
            {
                var from = now.AddDays(-1);
                var logPath = SessionLogStatsService.GetDefaultLogPath(_runtime);
                var stats = SessionLogStatsService.Compute(logPath, from, now);

                daily = new
                {
                    fromUtc = stats.FromUtc,
                    toUtc = stats.ToUtc,
                    uploads = stats.Uploads,
                    downloads = stats.Downloads,
                    bytesUploaded = stats.BytesUploaded,
                    bytesDownloaded = stats.BytesDownloaded,
                    nukes = stats.Nukes,
                    pres = stats.Pres,
                    logins = stats.Logins,
                    users = stats.UniqueUsers
                };
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Debug, $"[Status] Failed to compute daily stats: {ex.Message}", ex);
            }

            var assembly = Assembly.GetExecutingAssembly();
            var ver = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";

            var payload = new
            {
                status = "ok",
                nowUtc = now,
                version = ver,
                server = new
                {
                    bindAddress = ftpCfg.BindAddress,
                    port = ftpCfg.Port,
                    root = ftpCfg.RootPath
                },
                sessions = new
                {
                    active = activeSessions
                },
                transfers = new
                {
                    bytesUploaded = perf.BytesUploaded,
                    bytesDownloaded = perf.BytesDownloaded,
                    activeTransfers = perf.ActiveTransfers,
                    totalTransfers = perf.TotalTransfers,
                    averageMsPerTransfer = perf.AverageTransferMilliseconds,
                    maxConcurrentTransfers = perf.MaxConcurrentTransfers
                },
                daily
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            var data = Encoding.UTF8.GetBytes(json);
            res.StatusCode = (int)HttpStatusCode.OK;
            res.ContentType = "application/json";
            res.ContentEncoding = Encoding.UTF8;
            res.ContentLength64 = data.Length;

            await res.OutputStream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts?.CancelAsync();

            if (_loopTask is not null)
            {
                try { await _loopTask.ConfigureAwait(false); }
                catch { /* ignore */ }
            }

            if (_listener is not null)
            {
                try
                {
                    _listener.Stop();
                    _listener.Close();
                }
                catch
                {
                    // ignored
                }
            }
        }

        public void Stop()
        {
            _ = DisposeAsync();
        }
    }
}
