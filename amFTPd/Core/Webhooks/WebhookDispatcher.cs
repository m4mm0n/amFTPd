/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           WebhookDispatcher.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-25 00:00:00
 *  Last Modified:  2026-04-25 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      Subscribes to the EventBus and fires outbound HTTP POST webhooks for
 *      configured event types.
 *
 *      Design notes:
 *          - Subscribes synchronously to EventBus.Subscribe(); the handler
 *            immediately spawns a fire-and-forget Task so it never blocks
 *            the publishing FTP session thread.
 *          - HttpClient is shared (one per dispatcher instance) for
 *            connection-pool efficiency.
 *          - HMAC-SHA256 signing is computed over the UTF-8 JSON body.
 *          - On non-2xx or I/O error, retries up to MaxRetries times with
 *            a 2-second delay between attempts.
 *          - All errors are logged at Debug level so a dead webhook endpoint
 *            never disrupts normal FTP operations.
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using amFTPd.Config.Daemon;
using amFTPd.Core.Events;
using amFTPd.Logging;

namespace amFTPd.Core.Webhooks;

/// <summary>
/// Subscribes to the <see cref="EventBus"/> and fires HTTP POST webhooks
/// for configured event types.
/// </summary>
public sealed class WebhookDispatcher : IAsyncDisposable
{
    private readonly AmFtpdWebhookConfig _cfg;
    private readonly IFtpLogger _log;
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initialises the dispatcher and subscribes to the event bus.
    /// </summary>
    public WebhookDispatcher(AmFtpdWebhookConfig cfg, EventBus bus, IFtpLogger log)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        var timeout = cfg.TimeoutSeconds > 0
            ? TimeSpan.FromSeconds(cfg.TimeoutSeconds)
            : TimeSpan.FromSeconds(5);

        _http = new HttpClient { Timeout = timeout };
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("amFTPd-webhook", "1.0"));

        bus.Subscribe(OnEvent);

        _log.Log(FtpLogLevel.Info, "[Webhook] Dispatcher registered.");
    }

    // ------------------------------------------------------------------
    // EventBus handler (sync — must not block)
    // ------------------------------------------------------------------

    private void OnEvent(FtpEvent ev)
    {
        var url = ResolveUrl(ev.Type);
        if (string.IsNullOrWhiteSpace(url))
            return;

        // Fire-and-forget: never block the publishing FTP session thread.
        _ = Task.Run(() => FireAsync(ev, url));
    }

    // ------------------------------------------------------------------
    // URL resolution
    // ------------------------------------------------------------------

    private string? ResolveUrl(FtpEventType type) => type switch
    {
        FtpEventType.Upload => Coalesce(_cfg.UploadUrl, _cfg.DefaultUrl),
        FtpEventType.Download => Coalesce(_cfg.DownloadUrl, _cfg.DefaultUrl),
        FtpEventType.Nuke => Coalesce(_cfg.NukeUrl, _cfg.DefaultUrl),
        FtpEventType.AutoNuke => Coalesce(_cfg.NukeUrl, _cfg.DefaultUrl),
        FtpEventType.Unnuke => Coalesce(_cfg.UnnukeUrl, _cfg.DefaultUrl),
        FtpEventType.Pre => Coalesce(_cfg.PreUrl, _cfg.DefaultUrl),
        FtpEventType.RaceComplete => Coalesce(_cfg.RaceCompleteUrl, _cfg.DefaultUrl),
        FtpEventType.Login => Coalesce(_cfg.LoginUrl, _cfg.DefaultUrl),
        FtpEventType.Logout => Coalesce(_cfg.LogoutUrl, _cfg.DefaultUrl),
        FtpEventType.Request => Coalesce(_cfg.RequestUrl, _cfg.DefaultUrl),
        FtpEventType.Delete => Coalesce(_cfg.DeleteUrl, _cfg.DefaultUrl),
        FtpEventType.Wipe => Coalesce(_cfg.DeleteUrl, _cfg.DefaultUrl),
        _ => _cfg.DefaultUrl
    };

    private static string? Coalesce(string? specific, string? fallback)
        => string.IsNullOrWhiteSpace(specific) ? fallback : specific;

    // ------------------------------------------------------------------
    // HTTP dispatch with retry
    // ------------------------------------------------------------------

    private async Task FireAsync(FtpEvent ev, string url)
    {
        var json = BuildPayload(ev);

        for (int attempt = 0; attempt <= _cfg.MaxRetries; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                // HMAC-SHA256 signature
                var sig = ComputeSignature(json);
                if (sig is not null)
                    req.Headers.Add("X-AmFTPd-Signature", sig);

                req.Headers.Add("X-AmFTPd-Event", ev.Type.ToString());

                using var resp = await _http.SendAsync(req).ConfigureAwait(false);

                if (resp.IsSuccessStatusCode)
                {
                    _log.Log(FtpLogLevel.Debug,
                        $"[Webhook] {ev.Type} → {url} ({(int)resp.StatusCode})");
                    return;
                }

                _log.Log(FtpLogLevel.Debug,
                    $"[Webhook] {ev.Type} → {url} non-2xx {(int)resp.StatusCode} (attempt {attempt + 1}/{_cfg.MaxRetries + 1})");
            }
            catch (TaskCanceledException)
            {
                _log.Log(FtpLogLevel.Debug,
                    $"[Webhook] {ev.Type} → {url} timed out (attempt {attempt + 1}/{_cfg.MaxRetries + 1})");
            }
            catch (Exception ex)
            {
                _log.Log(FtpLogLevel.Debug,
                    $"[Webhook] {ev.Type} → {url} error (attempt {attempt + 1}/{_cfg.MaxRetries + 1}): {ex.Message}");
            }
        }
    }

    // ------------------------------------------------------------------
    // Payload serialisation
    // ------------------------------------------------------------------

    private static string BuildPayload(FtpEvent ev)
    {
        var payload = new
        {
            @event = ev.Type.ToString(),
            timestamp = ev.Timestamp,
            data = new
            {
                sessionId = ev.SessionId,
                user = ev.User,
                group = ev.Group,
                section = ev.Section,
                virtualPath = ev.VirtualPath,
                releaseName = ev.ReleaseName,
                bytes = ev.Bytes,
                reason = ev.Reason,
                remoteHost = ev.RemoteHost,
                extra = ev.Extra
            }
        };

        return JsonSerializer.Serialize(payload, _jsonOpts);
    }

    // ------------------------------------------------------------------
    // HMAC-SHA256 signing
    // ------------------------------------------------------------------

    private string? ComputeSignature(string body)
    {
        if (string.IsNullOrWhiteSpace(_cfg.SigningSecret))
            return null;

        var keyBytes = Encoding.UTF8.GetBytes(_cfg.SigningSecret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(bodyBytes);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ------------------------------------------------------------------
    // Disposal
    // ------------------------------------------------------------------

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
