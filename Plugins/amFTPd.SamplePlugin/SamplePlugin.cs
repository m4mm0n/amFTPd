/*
 * ====================================================================================================
 *  Project:        amFTPd.SamplePlugin
 *  File:           SamplePlugin.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-25 00:00:00
 *  Last Modified:  2026-04-25 00:00:00
 *
 *  Description:
 *      Demonstration plugin for amFTPd showing all three extension points:
 *        • ISiteCommandPlugin  — adds SITE HELLO and SITE PING
 *        • IAuthProviderPlugin — allows a hardcoded "magic" password for dev/test
 *        • IEventHandlerPlugin — logs every Upload event to a text file
 *
 *      This plugin is intentionally simple.  It is meant to be read alongside
 *      docs/Plugins.md as a quick-start reference.
 *
 *      ⚠ DO NOT deploy the hardcoded-password auth provider to production.
 *
 *  License:
 *      MIT License — https://opensource.org/licenses/MIT
 * ====================================================================================================
 */

using amFTPd.Plugin.Abstractions;

namespace amFTPd.SamplePlugin;

// ┌─────────────────────────────────────────────────────────────────────────────┐
// │  SamplePlugin                                                               │
// │  One class that implements all three extension interfaces.                  │
// │  A single class may implement any combination of the three.                 │
// └─────────────────────────────────────────────────────────────────────────────┘

/// <summary>
/// Sample amFTPd plugin demonstrating SITE commands, auth providers, and event handlers.
/// </summary>
public sealed class SamplePlugin
    : IAmFtpdPlugin,
      ISiteCommandPlugin,
      IAuthProviderPlugin,
      IEventHandlerPlugin
{
    // ── Plugin metadata ───────────────────────────────────────────────────────

    public string Name => "amFTPd SamplePlugin";
    public string Version => "1.0.0";
    public string Author => "Geir Gustavsen, ZeroLinez Softworx";
    public string Description => "Demo plugin: SITE HELLO/PING, magic-password auth, upload logger.";

    // ── State set during InitializeAsync ─────────────────────────────────────

    private IPluginContext _ctx = null!;
    private string _logPath = "";

    // ── IAmFtpdPlugin ─────────────────────────────────────────────────────────

    public Task InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        _ctx = context;

        // Resolve the upload-log path from Settings, defaulting to ConfigDirectory.
        if (!context.Settings.TryGetValue("UploadLogPath", out var uploadLog) ||
            string.IsNullOrWhiteSpace(uploadLog))
        {
            uploadLog = Path.Combine(context.ConfigDirectory, "sampleplugin-uploads.log");
        }
        _logPath = uploadLog;

        context.LogInfo($"[SamplePlugin] Initialised. Upload log → '{_logPath}'");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken ct = default)
    {
        _ctx?.LogInfo("[SamplePlugin] Shutdown.");
        return Task.CompletedTask;
    }

    // ── ISiteCommandPlugin ────────────────────────────────────────────────────

    public IReadOnlyList<string> CommandNames =>
        ["HELLO", "PING"];

    public IReadOnlyDictionary<string, string> HelpLines =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HELLO"] = "HELLO [name] — greet the server.",
            ["PING"] = "PING          — check plugin round-trip latency.",
        };

    public async Task<PluginSiteResult> ExecuteAsync(
        PluginSiteContext context,
        CancellationToken ct)
    {
        switch (context.Verb.ToUpperInvariant())
        {
            case "HELLO":
                {
                    var name = string.IsNullOrWhiteSpace(context.Argument)
                        ? context.Username
                        : context.Argument.Trim();

                    await context.WriteResponseAsync(
                        $"200 Hello, {name}! Welcome from the SamplePlugin.\r\n", ct);
                    return PluginSiteResult.Done;
                }

            case "PING":
                {
                    var ts = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
                    await context.WriteResponseAsync(
                        $"200 PONG — server time is {ts}.\r\n", ct);
                    return PluginSiteResult.Done;
                }

            default:
                return PluginSiteResult.NotHandled;
        }
    }

    // ── IAuthProviderPlugin ───────────────────────────────────────────────────

    // The magic password is read from the plugin's Settings block.
    // amftpd.json example:
    //
    //   "Settings": { "MagicPassword": "s3cr3t-dev-only" }
    //
    // ⚠ This is a DEV CONVENIENCE ONLY. Remove or disable in production.

    public Task<PluginAuthResult> AuthenticateAsync(
        PluginAuthRequest request,
        CancellationToken ct)
    {
        if (!_ctx.Settings.TryGetValue("MagicPassword", out var magic) ||
            string.IsNullOrWhiteSpace(magic))
        {
            // No magic password configured → pass through to built-in auth.
            return Task.FromResult(PluginAuthResult.Passthrough);
        }

        if (string.Equals(request.Password, magic, StringComparison.Ordinal))
        {
            _ctx.LogWarn(
                $"[SamplePlugin] Magic-password auth granted for '{request.Username}' from {request.RemoteIp}.");
            return Task.FromResult(PluginAuthResult.Authenticated);
        }

        // Wrong password but the magic password feature is enabled.
        // Return Passthrough so built-in hashed password can still succeed.
        return Task.FromResult(PluginAuthResult.Passthrough);
    }

    // ── IEventHandlerPlugin ───────────────────────────────────────────────────

    // Subscribe only to Upload events (demonstrate selective subscription).
    public IReadOnlyList<string> HandledEventTypes => ["Upload"];

    public async Task OnEventAsync(PluginFtpEvent ftpEvent, CancellationToken ct)
    {
        if (!string.Equals(ftpEvent.Type, "Upload", StringComparison.OrdinalIgnoreCase))
            return;

        var line = string.Format(
            "[{0:yyyy-MM-dd HH:mm:ss}] user={1} group={2} section={3} bytes={4} path={5}\n",
            ftpEvent.Timestamp.ToUniversalTime(),
            ftpEvent.Username ?? "-",
            ftpEvent.Group ?? "-",
            ftpEvent.Section ?? "-",
            ftpEvent.Bytes,
            ftpEvent.VirtualPath ?? "-");

        try
        {
            await File.AppendAllTextAsync(_logPath, line, ct);
        }
        catch (Exception ex)
        {
            _ctx.LogError($"[SamplePlugin] Failed to write upload log: {ex.Message}", ex);
        }
    }
}
