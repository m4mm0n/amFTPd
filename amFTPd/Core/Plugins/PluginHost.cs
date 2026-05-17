/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           PluginHost.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-25 00:00:00
 *  Last Modified:  2026-04-25 00:00:00
 *  CRC32:          0x00000000
 *
 *  Description:
 *      Loads, initialises, and dispatches calls to all configured amFTPd plugins.
 *      Manages three extension points:
 *        • ISiteCommandPlugin  — custom SITE sub-commands
 *        • IAuthProviderPlugin — external authentication (LDAP, OAuth, file, …)
 *        • IEventHandlerPlugin — FTP lifecycle event subscribers
 *
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

using System.Reflection;
using amFTPd.Config.Daemon;
using amFTPd.Core.Events;
using amFTPd.Logging;
using amFTPd.Plugin.Abstractions;

namespace amFTPd.Core.Plugins;

/// <summary>
/// Loads all configured plugins and exposes dispatch methods for each extension point.
/// Create via <see cref="LoadAsync"/> and dispose on shutdown / REHASH.
/// </summary>
public sealed class PluginHost : IAsyncDisposable
{
    // ── loaded entries ────────────────────────────────────────────────────────

    private sealed record LoadedPlugin(
        PluginLoadContext Context,
        IAmFtpdPlugin Plugin,
        IPluginContext PluginCtx);

    private readonly List<LoadedPlugin> _all = [];
    private readonly Dictionary<string, ISiteCommandPlugin> _site = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IAuthProviderPlugin> _auth = [];
    private readonly List<(IEventHandlerPlugin, HashSet<string>)> _events = [];

    // ── factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads and initialises all plugins listed in <paramref name="entries"/>.
    /// Returns a fully-wired <see cref="PluginHost"/> ready to receive dispatch calls.
    /// </summary>
    public static async Task<PluginHost> LoadAsync(
        IReadOnlyList<AmFtpdPluginEntry> entries,
        string configDir,
        IFtpLogger log,
        CancellationToken ct = default)
    {
        var host = new PluginHost();

        foreach (var entry in entries)
        {
            if (!entry.Enabled)
            {
                log.Log(FtpLogLevel.Info,
                    $"[Plugin] Skipping disabled plugin '{entry.Path}'.");
                continue;
            }

            var dllPath = Path.IsPathRooted(entry.Path)
                ? entry.Path
                : Path.GetFullPath(Path.Combine(configDir, entry.Path));

            if (!File.Exists(dllPath))
            {
                log.Log(FtpLogLevel.Error,
                    $"[Plugin] DLL not found: '{dllPath}'. Skipping.");
                continue;
            }

            try
            {
                await host.LoadOneAsync(dllPath, entry, configDir, log, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log.Log(FtpLogLevel.Error,
                    $"[Plugin] Failed to load '{dllPath}': {ex.Message}", ex);
            }
        }

        log.Log(FtpLogLevel.Info,
            $"[Plugin] Loaded {host._all.Count} plugin(s): " +
            (host._all.Count == 0 ? "none"
             : string.Join(", ", host._all.Select(p => p.Plugin.Name))));

        return host;
    }

    // ── dispatch: SITE commands ───────────────────────────────────────────────

    /// <summary>
    /// Tries to dispatch a SITE sub-command to a registered plugin.
    /// Returns <c>true</c> if the command was handled (response already written).
    /// The plugin context is resolved internally; the caller does not need to supply it.
    /// </summary>
    public async Task<bool> TryHandleSiteCommandAsync(
        string verb,
        string argument,
        string username,
        bool isAdmin,
        bool isSiteop,
        string remoteIp,
        string? currentPath,
        Func<string, CancellationToken, Task> write,
        CancellationToken ct)
    {
        if (!_site.TryGetValue(verb, out var plugin))
            return false;

        // Each plugin has its own context stored during load.
        var entry = _all.Find(p => p.Plugin == plugin);
        var pluginCtx = entry?.PluginCtx ?? throw new InvalidOperationException(
                            $"No context found for plugin that owns SITE {verb}");

        var siteCtx = new PluginSiteContext
        {
            Verb = verb,
            Argument = argument,
            Username = username,
            IsAdmin = isAdmin,
            IsSiteop = isSiteop,
            RemoteIp = remoteIp,
            CurrentPath = currentPath,
            WriteResponseAsync = write,
            PluginContext = pluginCtx
        };

        try
        {
            var result = await plugin.ExecuteAsync(siteCtx, ct).ConfigureAwait(false);
            return result.Handled;
        }
        catch (Exception ex)
        {
            pluginCtx.LogError($"[Plugin:{plugin.Name}] SITE {verb} threw: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>Returns the names of all registered SITE command plugins, for SITE HELP / SITE PLUGINS.</summary>
    public IReadOnlyDictionary<string, ISiteCommandPlugin> SiteCommands => _site;

    // ── dispatch: authentication ─────────────────────────────────────────────

    /// <summary>
    /// Runs all registered auth providers in order.
    /// Returns the first non-Passthrough result, or <c>null</c> if all pass through.
    /// </summary>
    public async Task<PluginAuthResult?> TryAuthenticateAsync(
        string username, string password, string remoteIp, CancellationToken ct)
    {
        if (_auth.Count == 0) return null;

        var req = new PluginAuthRequest
        {
            Username = username,
            Password = password,
            RemoteIp = remoteIp
        };

        foreach (var provider in _auth)
        {
            PluginAuthResult result;
            try
            {
                result = await provider.AuthenticateAsync(req, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Plugin threw — treat as passthrough and log
                _all.Find(p => p.Plugin == provider)?.PluginCtx
                    .LogError($"[Plugin:{provider.Name}] Auth threw: {ex.Message}", ex);
                continue;
            }

            if (result.Outcome != PluginAuthOutcome.Passthrough)
                return result;
        }

        return null; // all passed through — use built-in auth
    }

    // ── dispatch: events ─────────────────────────────────────────────────────

    /// <summary>
    /// Subscribes all event-handler plugins to the daemon's <see cref="EventBus"/>.
    /// Must be called after <see cref="LoadAsync"/> and before the server starts accepting connections.
    /// </summary>
    public void WireEventHandlers(EventBus eventBus)
    {
        foreach (var (handler, types) in _events)
        {
            var capturedHandler = handler;
            var capturedTypes = types;

            eventBus.Subscribe(ev =>
            {
                var typeName = ev.Type.ToString();
                if (capturedTypes.Count > 0 && !capturedTypes.Contains("*") && !capturedTypes.Contains(typeName))
                    return;

                var pluginEvent = MapToPluginEvent(ev);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        await capturedHandler.OnEventAsync(pluginEvent, cts.Token)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        /* swallowed — plugin event handlers must not crash the daemon */
                    }
                });
            });
        }
    }

    // ── IAsyncDisposable ─────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        // Shut down plugins in reverse load order (LIFO)
        for (int i = _all.Count - 1; i >= 0; i--)
        {
            var (ctx, plugin, _) = _all[i];
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await plugin.ShutdownAsync(cts.Token).ConfigureAwait(false);
            }
            catch { /* best effort */ }

            // Allow the AssemblyLoadContext to be garbage-collected
            ctx.Unload();
        }

        _all.Clear();
        _site.Clear();
        _auth.Clear();
        _events.Clear();
    }

    // ── private helpers ───────────────────────────────────────────────────────

    private async Task LoadOneAsync(
        string dllPath, AmFtpdPluginEntry entry,
        string configDir, IFtpLogger log, CancellationToken ct)
    {
        var loadCtx = new PluginLoadContext(dllPath);
        var asm = loadCtx.LoadFromAssemblyPath(dllPath);

        var pluginCtx = new PluginContextImpl(entry, configDir, log);
        int found = 0;

        foreach (var type in asm.GetExportedTypes())
        {
            if (type.IsAbstract || type.IsInterface)
                continue;
            if (!typeof(IAmFtpdPlugin).IsAssignableFrom(type))
                continue;
            if (type.GetConstructor(Type.EmptyTypes) is null)
                continue;

            if (Activator.CreateInstance(type) is not IAmFtpdPlugin plugin)
                continue;

            // Initialize
            await plugin.InitializeAsync(pluginCtx, ct).ConfigureAwait(false);

            _all.Add(new LoadedPlugin(loadCtx, plugin, pluginCtx));

            // Register extension points
            if (plugin is ISiteCommandPlugin sitePlugin)
            {
                foreach (var cmd in sitePlugin.CommandNames)
                {
                    _site[cmd] = sitePlugin;
                    log.Log(FtpLogLevel.Info,
                        $"[Plugin:{plugin.Name}] Registered SITE {cmd.ToUpperInvariant()}");
                }
            }

            if (plugin is IAuthProviderPlugin authPlugin)
            {
                _auth.Add(authPlugin);
                log.Log(FtpLogLevel.Info,
                    $"[Plugin:{plugin.Name}] Registered auth provider");
            }

            if (plugin is IEventHandlerPlugin eventPlugin)
            {
                var types = eventPlugin.HandledEventTypes;
                var typeSet = types.Count == 0
                    ? ["*"]
                    : new HashSet<string>(types, StringComparer.OrdinalIgnoreCase);
                _events.Add((eventPlugin, typeSet));
                log.Log(FtpLogLevel.Info,
                    $"[Plugin:{plugin.Name}] Registered event handler " +
                    $"({(typeSet.Contains("*") ? "all events" : string.Join(", ", typeSet))})");
            }

            found++;
            log.Log(FtpLogLevel.Info,
                $"[Plugin] Loaded '{plugin.Name}' v{plugin.Version} by {plugin.Author}");
        }

        if (found == 0)
        {
            log.Log(FtpLogLevel.Warn,
                $"[Plugin] No IAmFtpdPlugin implementations found in '{dllPath}'.");
            loadCtx.Unload();
        }
    }

    private static PluginFtpEvent MapToPluginEvent(FtpEvent ev)
        => new()
        {
            Type = ev.Type.ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            Username = ev.User,
            Group = ev.Group,
            Section = ev.Section,
            VirtualPath = ev.VirtualPath,
            ReleaseName = ev.ReleaseName,
            Bytes = ev.Bytes ?? 0,
            Reason = ev.Reason,
            RemoteHost = ev.RemoteHost,
            Extra = ParseExtra(ev.Extra)
        };

    private static IReadOnlyDictionary<string, object?>? ParseExtra(string? extra)
    {
        if (string.IsNullOrWhiteSpace(extra)) return null;

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        // Basic space-separated k=v parser
        var parts = extra.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2)
                dict[kv[0]] = kv[1];
            else
                dict[kv[0]] = true;
        }
        return dict;
    }
}

// ── IPluginContext implementation ─────────────────────────────────────────────

internal sealed class PluginContextImpl : IPluginContext
{
    private readonly IFtpLogger _log;

    public string ConfigDirectory { get; }
    public IReadOnlyDictionary<string, string> Settings { get; }

    internal PluginContextImpl(AmFtpdPluginEntry entry, string configDir, IFtpLogger log)
    {
        _log = log;
        ConfigDirectory = configDir;
        Settings = entry.Settings
                          ?? new Dictionary<string, string>();
    }

    public void LogInfo(string msg) => _log.Log(FtpLogLevel.Info, msg);
    public void LogWarn(string msg) => _log.Log(FtpLogLevel.Warn, msg);
    public void LogError(string msg, Exception? ex = null) => _log.Log(FtpLogLevel.Error, msg, ex);
}
