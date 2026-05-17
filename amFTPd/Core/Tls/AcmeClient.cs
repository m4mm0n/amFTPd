/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AcmeClient.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-25 00:00:00
 *  Last Modified:  2026-04-25 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      Pure-BCL ACME v2 client (RFC 8555).  No third-party NuGet packages.
 *      Implements HTTP-01 challenge, EC P-256 account key / JWS signing,
 *      RSA-2048 certificate key, CSR generation, and PFX assembly.
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using amFTPd.Logging;

namespace amFTPd.Core.Tls;

/// <summary>
/// Represents the ACME v2 directory endpoint URLs retrieved on session open.
/// Shared between <see cref="AcmeClient"/> and <see cref="AcmeSession"/>.
/// </summary>
internal sealed record AcmeDirectory(string NewNonce, string NewAccount, string NewOrder);

/// <summary>
/// Thrown when the ACME CA returns an error or the local flow detects a protocol violation.
/// </summary>
public sealed class AcmeException : Exception
{
    public AcmeException(string message) : base(message) { }
    public AcmeException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Stateless ACME v2 client.  All state (account key, nonces) is encapsulated in the
/// <see cref="AcmeSession"/> helper returned by <see cref="OpenSessionAsync"/>.
/// Callers typically use the high-level <see cref="RequestCertificateAsync"/> entry point.
/// </summary>
public static class AcmeClient
{
    // ── public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Provisions (or renews) a certificate for <paramref name="domain"/> from the given
    /// ACME v2 <paramref name="directoryUrl"/>.
    /// </summary>
    /// <returns>
    /// Raw PKCS#12 (PFX) bytes containing the leaf certificate, intermediates,
    /// and the private key.  The PFX is protected by <paramref name="pfxPassword"/>
    /// (empty string = no password).
    /// </returns>
    public static async Task<byte[]> RequestCertificateAsync(
        string directoryUrl,
        string domain,
        string email,
        string accountKeyPath,
        int challengePort,
        string pfxPassword,
        IFtpLogger log,
        CancellationToken ct = default)
    {
        log.Log(FtpLogLevel.Info, $"[ACME] Starting certificate request for {domain}");

        await using var session = await OpenSessionAsync(directoryUrl, accountKeyPath, log, ct)
            .ConfigureAwait(false);

        // 1) Register / locate account
        await session.EnsureAccountAsync(email, ct).ConfigureAwait(false);

        // 2) Place order
        var orderUrl = await session.NewOrderAsync(domain, ct).ConfigureAwait(false);
        var order = await session.GetOrderAsync(orderUrl, ct).ConfigureAwait(false);

        if (order.Status is "valid" or "ready")
        {
            log.Log(FtpLogLevel.Info, "[ACME] Order already ready — skipping authorisation.");
        }
        else
        {
            // 3) Satisfy HTTP-01 challenge for each authorization
            foreach (var authUrl in order.Authorizations)
            {
                await session.SatisfyAuthorizationAsync(authUrl, challengePort, log, ct)
                    .ConfigureAwait(false);
            }
        }

        // 4) Generate cert key + CSR
        using var certKey = RSA.Create(2048);
        var csrDer = BuildCsr(domain, certKey);

        // 5) Finalise order
        await session.FinalizeOrderAsync(order.Finalize, csrDer, ct).ConfigureAwait(false);

        // Poll until cert is ready
        var finalOrder = await session.PollOrderAsync(orderUrl, ct).ConfigureAwait(false);
        if (finalOrder.Certificate is null)
            throw new AcmeException("ACME order completed but certificate URL is null.");

        // 6) Download certificate chain (PEM)
        var pemChain = await session.DownloadCertificateAsync(finalOrder.Certificate, ct)
            .ConfigureAwait(false);

        log.Log(FtpLogLevel.Info, "[ACME] Certificate issued successfully.");

        // 7) Assemble PFX
        return AssemblePfx(pemChain, certKey, pfxPassword);
    }

    // ── session factory ───────────────────────────────────────────────────────

    private static async Task<AcmeSession> OpenSessionAsync(
        string directoryUrl, string accountKeyPath, IFtpLogger log, CancellationToken ct)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("amFTPd-ACME/1.0");

        // Fetch directory
        var dirJson = await http.GetStringAsync(directoryUrl, ct).ConfigureAwait(false);
        using var dirDoc = JsonDocument.Parse(dirJson);
        var dir = new AcmeDirectory(
            NewNonce: dirDoc.RootElement.GetProperty("newNonce").GetString()!,
            NewAccount: dirDoc.RootElement.GetProperty("newAccount").GetString()!,
            NewOrder: dirDoc.RootElement.GetProperty("newOrder").GetString()!);

        // Load or create account key
        var accountKey = LoadOrCreateAccountKey(accountKeyPath, log);

        return new AcmeSession(http, dir, accountKey, log);
    }

    // ── account key ───────────────────────────────────────────────────────────

    private static ECDsa LoadOrCreateAccountKey(string path, IFtpLogger log)
    {
        if (File.Exists(path))
        {
            try
            {
                var pem = File.ReadAllText(path);
                var key = ECDsa.Create();
                key.ImportFromPem(pem);
                log.Log(FtpLogLevel.Info, $"[ACME] Loaded account key from {path}");
                return key;
            }
            catch (Exception ex)
            {
                log.Log(FtpLogLevel.Warn,
                    $"[ACME] Failed to load account key ({ex.Message}); generating new key.");
            }
        }

        log.Log(FtpLogLevel.Info, "[ACME] Generating new EC P-256 account key.");
        var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, newKey.ExportECPrivateKeyPem());
        }
        catch (Exception ex)
        {
            log.Log(FtpLogLevel.Warn, $"[ACME] Could not persist account key to {path}: {ex.Message}");
        }

        return newKey;
    }

    // ── CSR builder ──────────────────────────────────────────────────────────

    private static byte[] BuildCsr(string domain, RSA key)
    {
        var req = new CertificateRequest($"CN={domain}", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(domain);
        req.CertificateExtensions.Add(san.Build());

        return req.CreateSigningRequest();
    }

    // ── PFX assembly ─────────────────────────────────────────────────────────

    private static byte[] AssemblePfx(string pemChain, RSA certKey, string password)
    {
        // Parse all PEM certificate blocks from the chain
        var certs = new List<X509Certificate2>();
        var span = pemChain.AsSpan();

        const string beginMarker = "-----BEGIN CERTIFICATE-----";
        const string endMarker = "-----END CERTIFICATE-----";

        int pos = 0;
        while (pos < span.Length)
        {
            int start = pemChain.IndexOf(beginMarker, pos, StringComparison.Ordinal);
            if (start < 0) break;
            int end = pemChain.IndexOf(endMarker, start, StringComparison.Ordinal);
            if (end < 0) break;
            end += endMarker.Length;

            var block = pemChain[start..end];
            certs.Add(X509Certificate2.CreateFromPem(block));
            pos = end;
        }

        if (certs.Count == 0)
            throw new AcmeException("ACME returned an empty certificate chain.");

        // Bind private key to the leaf certificate
        var leaf = certs[0].CopyWithPrivateKey(certKey);
        var collection = new X509Certificate2Collection();

        const X509KeyStorageFlags Flags =
            X509KeyStorageFlags.UserKeySet |
            X509KeyStorageFlags.Exportable |
            X509KeyStorageFlags.PersistKeySet;

        // Re-import with persistent key flags so Schannel can use it on Windows
        var pwd = string.IsNullOrEmpty(password) ? null : password;
        var pfxTemp = leaf.Export(X509ContentType.Pkcs12, pwd);

#pragma warning disable SYSLIB0057
        var leafPersisted = new X509Certificate2(pfxTemp, pwd, Flags);
#pragma warning restore SYSLIB0057

        collection.Add(leafPersisted);
        for (int i = 1; i < certs.Count; i++)
            collection.Add(certs[i]);

        return collection.Export(X509ContentType.Pkcs12, pwd)
               ?? throw new AcmeException("Failed to export certificate collection as PFX.");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    internal static string Base64Url(byte[] data)
        => Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    internal static string Base64Url(string utf8Text)
        => Base64Url(Encoding.UTF8.GetBytes(utf8Text));

}

// ── AcmeSession (all stateful protocol logic) ─────────────────────────────────

/// <summary>
/// Holds per-session state: account key, nonce cache, kid, and the shared <see cref="HttpClient"/>.
/// Created by <see cref="AcmeClient.OpenSessionAsync"/> and disposed at the end of each provisioning run.
/// </summary>
internal sealed class AcmeSession : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly AcmeDirectory _dir;
    private readonly ECDsa _key;
    private readonly IFtpLogger _log;

    private readonly ECParameters _pub;
    private readonly string _xBase64;
    private readonly string _yBase64;
    private readonly string _thumbprint;  // base64url(SHA256(canonical jwk))

    private string? _nonce;
    private string? _kid;   // account URL returned by newAccount

    // ── record types for JSON parsing ─────────────────────────────────────────

    internal sealed class AcmeOrder
    {
        [JsonPropertyName("status")] public string Status { get; init; } = "";
        [JsonPropertyName("authorizations")] public string[] Authorizations { get; init; } = [];
        [JsonPropertyName("finalize")] public string Finalize { get; init; } = "";
        [JsonPropertyName("certificate")] public string? Certificate { get; init; }
    }

    internal sealed class AcmeAuthorization
    {
        [JsonPropertyName("status")] public string Status { get; init; } = "";
        [JsonPropertyName("challenges")] public AcmeChallenge[] Challenges { get; init; } = [];
    }

    internal sealed class AcmeChallenge
    {
        [JsonPropertyName("type")] public string Type { get; init; } = "";
        [JsonPropertyName("token")] public string Token { get; init; } = "";
        [JsonPropertyName("url")] public string Url { get; init; } = "";
        [JsonPropertyName("status")] public string? Status { get; init; }
    }

    internal sealed class AcmeError
    {
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("detail")] public string? Detail { get; init; }
    }

    // ── constructor ───────────────────────────────────────────────────────────

    internal AcmeSession(HttpClient http, AcmeDirectory dir, ECDsa key, IFtpLogger log)
    {
        _http = http;
        _dir = dir;
        _key = key;
        _log = log;

        _pub = key.ExportParameters(includePrivateParameters: false);
        _xBase64 = AcmeClient.Base64Url(PadTo32(_pub.Q.X!));
        _yBase64 = AcmeClient.Base64Url(PadTo32(_pub.Q.Y!));

        // JWK thumbprint: SHA256 of canonical {"crv":"P-256","kty":"EC","x":"...","y":"..."}
        var canonical = $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{_xBase64}\",\"y\":\"{_yBase64}\"}}";
        _thumbprint = AcmeClient.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    // ── public operations ─────────────────────────────────────────────────────

    /// <summary>Registers a new account or locates an existing one by account key.</summary>
    internal async Task EnsureAccountAsync(string email, CancellationToken ct)
    {
        var payload = new
        {
            termsOfServiceAgreed = true,
            contact = new[] { $"mailto:{email}" }
        };

        var resp = await PostJwsAsync(_dir.NewAccount, payload, ct, useJwk: true)
            .ConfigureAwait(false);

        CaptureNonce(resp);

        // 200 = existing account, 201 = created
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new AcmeException($"ACME newAccount failed ({resp.StatusCode}): {body}");
        }

        if (resp.Headers.Location is { } loc)
            _kid = loc.ToString();
        else
        {
            // Some CAs return kid in the payload
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("kid", out var kidProp))
                _kid = kidProp.GetString();
        }

        _log.Log(FtpLogLevel.Info, $"[ACME] Account: {_kid ?? "unknown"}");
    }

    /// <summary>Places a new order and returns the order URL (Location header).</summary>
    internal async Task<string> NewOrderAsync(string domain, CancellationToken ct)
    {
        var payload = new
        {
            identifiers = new[] { new { type = "dns", value = domain } }
        };

        var resp = await PostJwsAsync(_dir.NewOrder, payload, ct).ConfigureAwait(false);
        CaptureNonce(resp);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new AcmeException($"ACME newOrder failed ({resp.StatusCode}): {body}");
        }

        var orderUrl = resp.Headers.Location?.ToString()
                       ?? throw new AcmeException("ACME newOrder response missing Location header.");

        _log.Log(FtpLogLevel.Info, $"[ACME] Order placed: {orderUrl}");
        return orderUrl;
    }

    /// <summary>Fetches the current order object via POST-as-GET.</summary>
    internal async Task<AcmeOrder> GetOrderAsync(string orderUrl, CancellationToken ct)
    {
        var resp = await PostJwsAsync(orderUrl, payload: null, ct).ConfigureAwait(false);
        CaptureNonce(resp);
        return await DeserializeAsync<AcmeOrder>(resp, ct).ConfigureAwait(false);
    }

    /// <summary>Starts an HTTP-01 challenge listener, signals the CA, and waits for valid status.</summary>
    internal async Task SatisfyAuthorizationAsync(
        string authUrl, int challengePort, IFtpLogger log, CancellationToken ct)
    {
        var authResp = await PostJwsAsync(authUrl, payload: null, ct).ConfigureAwait(false);
        CaptureNonce(authResp);
        var auth = await DeserializeAsync<AcmeAuthorization>(authResp, ct).ConfigureAwait(false);

        if (auth.Status == "valid")
        {
            log.Log(FtpLogLevel.Info, "[ACME] Authorization already valid.");
            return;
        }

        var challenge = Array.Find(auth.Challenges, c => c.Type == "http-01")
                        ?? throw new AcmeException("No http-01 challenge found in ACME authorization.");

        var keyAuthz = $"{challenge.Token}.{_thumbprint}";

        log.Log(FtpLogLevel.Info,
            $"[ACME] Starting HTTP-01 challenge server on port {challengePort} for token {challenge.Token}");

        using var challengeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        challengeCts.CancelAfter(TimeSpan.FromMinutes(5));

        // Run challenge server in the background
        var serverTask = Task.Run(
            () => ServeHttp01ChallengeAsync(challenge.Token, keyAuthz, challengePort, challengeCts.Token),
            ct);

        try
        {
            // Small pause so the server is ready before we notify the CA
            await Task.Delay(500, ct).ConfigureAwait(false);

            // POST an empty JSON object {} to tell the CA we're ready
            var triggerResp = await PostJwsAsync(challenge.Url, new { }, ct).ConfigureAwait(false);
            CaptureNonce(triggerResp);

            // Poll authorization until valid or invalid
            var finalAuth = await PollAsync(
                authUrl,
                async (token) =>
                {
                    var r = await PostJwsAsync(authUrl, null, token).ConfigureAwait(false);
                    CaptureNonce(r);
                    return await DeserializeAsync<AcmeAuthorization>(r, token).ConfigureAwait(false);
                },
                a => a.Status != "pending",
                ct).ConfigureAwait(false);

            if (finalAuth.Status != "valid")
                throw new AcmeException($"HTTP-01 authorization ended with status '{finalAuth.Status}'.");

            log.Log(FtpLogLevel.Info, "[ACME] Authorization validated.");
        }
        finally
        {
            // Stop the challenge server
            await challengeCts.CancelAsync().ConfigureAwait(false);
            try { await serverTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    /// <summary>Posts the CSR to the finalize URL.</summary>
    internal async Task FinalizeOrderAsync(string finalizeUrl, byte[] csrDer, CancellationToken ct)
    {
        var payload = new { csr = AcmeClient.Base64Url(csrDer) };
        var resp = await PostJwsAsync(finalizeUrl, payload, ct).ConfigureAwait(false);
        CaptureNonce(resp);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new AcmeException($"ACME finalize failed ({resp.StatusCode}): {body}");
        }

        _log.Log(FtpLogLevel.Info, "[ACME] Order finalized, waiting for certificate.");
    }

    /// <summary>Polls the order until status is 'valid', 'invalid', or 'ready'.</summary>
    internal async Task<AcmeOrder> PollOrderAsync(string orderUrl, CancellationToken ct)
    {
        var order = await PollAsync(
            orderUrl,
            async (token) =>
            {
                var r = await PostJwsAsync(orderUrl, null, token).ConfigureAwait(false);
                CaptureNonce(r);
                return await DeserializeAsync<AcmeOrder>(r, token).ConfigureAwait(false);
            },
            o => o.Status is "valid" or "invalid" or "ready",
            ct).ConfigureAwait(false);

        if (order.Status != "valid")
            throw new AcmeException($"ACME order ended with status '{order.Status}'.");

        return order;
    }

    /// <summary>Downloads the PEM certificate chain via POST-as-GET.</summary>
    internal async Task<string> DownloadCertificateAsync(string certUrl, CancellationToken ct)
    {
        var resp = await PostJwsAsync(certUrl, payload: null, ct).ConfigureAwait(false);
        CaptureNonce(resp);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new AcmeException($"ACME certificate download failed ({resp.StatusCode}): {body}");
        }

        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    // ── JWS signing ───────────────────────────────────────────────────────────

    /// <summary>
    /// Performs a JWS-signed ACME POST.
    /// Pass <paramref name="payload"/> as <c>null</c> for POST-as-GET (empty payload in JWS terms).
    /// </summary>
    private async Task<HttpResponseMessage> PostJwsAsync(
        string url, object? payload, CancellationToken ct, bool useJwk = false)
    {
        var nonce = await FetchNonceAsync(ct).ConfigureAwait(false);
        var jwsBody = BuildJws(url, payload, nonce, useJwk);
        var content = new StringContent(jwsBody, Encoding.ASCII, "application/jose+json");
        var response = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
        CaptureNonce(response);
        return response;
    }

    private string BuildJws(string url, object? payload, string nonce, bool useJwk)
    {
        // Protected header
        string protectedJson;
        if (useJwk || _kid is null)
        {
            // Use JWK in header (newAccount or before we have a kid)
            var header = new
            {
                alg = "ES256",
                jwk = new { crv = "P-256", kty = "EC", x = _xBase64, y = _yBase64 },
                nonce,
                url
            };
            protectedJson = JsonSerializer.Serialize(header);
        }
        else
        {
            var header = new { alg = "ES256", kid = _kid, nonce, url };
            protectedJson = JsonSerializer.Serialize(header);
        }

        var protectedB64 = AcmeClient.Base64Url(protectedJson);

        // POST-as-GET: payload is literal empty string (base64url("") == "")
        // Normal POST: base64url(json_payload)
        var payloadB64 = payload is null
            ? ""
            : AcmeClient.Base64Url(JsonSerializer.Serialize(payload));

        // Sign  input = "<protected>.<payload>"
        var sigInput = Encoding.ASCII.GetBytes($"{protectedB64}.{payloadB64}");
        var sigBytes = _key.SignData(sigInput, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var sigB64 = AcmeClient.Base64Url(sigBytes);

        return JsonSerializer.Serialize(new
        {
            @protected = protectedB64,
            payload = payloadB64,
            signature = sigB64
        });
    }

    // ── nonce management ─────────────────────────────────────────────────────

    private void CaptureNonce(HttpResponseMessage resp)
    {
        if (resp.Headers.TryGetValues("Replay-Nonce", out var values))
            _nonce = values.FirstOrDefault();
    }

    private async Task<string> FetchNonceAsync(CancellationToken ct)
    {
        if (_nonce is { Length: > 0 } cached)
        {
            _nonce = null;
            return cached;
        }

        // HEAD on newNonce endpoint is cheapest
        var req = new HttpRequestMessage(HttpMethod.Head, _dir.NewNonce);
        var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);

        if (!resp.Headers.TryGetValues("Replay-Nonce", out var values))
            throw new AcmeException("ACME newNonce response missing Replay-Nonce header.");

        return values.First();
    }

    // ── HTTP-01 challenge server ───────────────────────────────────────────────

    private static async Task ServeHttp01ChallengeAsync(
        string token, string keyAuthz, int port, CancellationToken ct)
    {
        var listener = new HttpListener();
        // +: listens on all interfaces; requires elevated privileges on Windows
        // (run as admin or configure with: netsh http add urlacl url=http://+:80/ user=Everyone)
        listener.Prefixes.Add($"http://+:{port}/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new AcmeException(
                $"[ACME] Cannot bind HTTP-01 challenge server to port {port}: {ex.Message}. " +
                "On Windows ensure amFTPd runs as Administrator or grant URL ACL " +
                $"(netsh http add urlacl url=http://+:{port}/ user=Everyone).", ex);
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await listener.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch { break; }

                var reqToken = ctx.Request.Url?.Segments.LastOrDefault()?.Trim('/');
                if (reqToken == token)
                {
                    var body = Encoding.ASCII.GetBytes(keyAuthz);
                    ctx.Response.ContentType = "application/octet-stream";
                    ctx.Response.ContentLength64 = body.Length;
                    ctx.Response.StatusCode = 200;
                    try
                    {
                        await ctx.Response.OutputStream.WriteAsync(body, ct).ConfigureAwait(false);
                    }
                    catch { /* client disconnected */ }
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                }
                try { ctx.Response.Close(); } catch { }
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    // ── polling helper ────────────────────────────────────────────────────────

    private static async Task<T> PollAsync<T>(
        string description,
        Func<CancellationToken, Task<T>> fetch,
        Func<T, bool> isDone,
        CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        for (int attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
            var result = await fetch(ct).ConfigureAwait(false);
            if (isDone(result)) return result;
            delay = delay.TotalSeconds < 30 ? delay * 2 : TimeSpan.FromSeconds(30);
        }
        throw new AcmeException($"Timed out waiting for ACME status change on {description}.");
    }

    // ── JSON deserialisation ─────────────────────────────────────────────────

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(json)
               ?? throw new AcmeException($"Failed to deserialise ACME response as {typeof(T).Name}.");
    }

    // ── key coordinate helper ────────────────────────────────────────────────

    /// <summary>Pads an EC coordinate byte array to exactly 32 bytes (P-256 field size).</summary>
    private static byte[] PadTo32(byte[] src)
    {
        if (src.Length == 32) return src;
        if (src.Length > 32)
        {
            // Leading zeros were stripped — take last 32 bytes
            return src[^32..];
        }
        var padded = new byte[32];
        src.CopyTo(padded, 32 - src.Length);
        return padded;
    }

    // ── IAsyncDisposable ─────────────────────────────────────────────────────

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        _key.Dispose();
        return ValueTask.CompletedTask;
    }

}
