/*
 * ====================================================================================================
 *  Project:        amFTPd.Plugin.Abstractions
 *  File:           IAmFtpdPlugin.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-25 00:00:00
 *  Last Modified:  2026-04-25 00:00:00
 *
 *  Description:
 *      Root interface every amFTPd plugin must implement plus all extension-point
 *      interfaces, context types, and result types.
 *
 *      To build a plugin:
 *        1. Create a .NET 10 class library project.
 *        2. Add a PackageReference to amFTPd.Plugin.Abstractions.
 *        3. Implement IAmFtpdPlugin (required) and one or more extension interfaces.
 *        4. Build → drop the output folder next to amftpd.json.
 *        5. Register the DLL in amftpd.json under the "Plugins" array.
 *
 *  License:  MIT — https://opensource.org/licenses/MIT
 * ====================================================================================================
 */

namespace amFTPd.Plugin.Abstractions;

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  Base plugin interface                                                   ║
// ╚══════════════════════════════════════════════════════════════════════════╝

/// <summary>
/// Every amFTPd plugin must implement this interface.
/// </summary>
public interface IAmFtpdPlugin
{
    /// <summary>Plugin display name (e.g. "LDAP Auth", "Discord Announce").</summary>
    string Name { get; }

    /// <summary>Plugin version string (e.g. "1.0.0").</summary>
    string Version { get; }

    /// <summary>Plugin author / organisation.</summary>
    string Author { get; }

    /// <summary>One-line description displayed in SITE PLUGINS output.</summary>
    string Description { get; }

    /// <summary>
    /// Called once when the plugin is loaded (on daemon start or REHASH).
    /// Perform expensive initialisation here (open connections, read config files, etc.).
    /// </summary>
    Task InitializeAsync(IPluginContext context, CancellationToken ct = default);

    /// <summary>
    /// Called when the daemon shuts down or the plugin is unloaded via REHASH.
    /// Release all held resources.
    /// </summary>
    Task ShutdownAsync(CancellationToken ct = default);
}

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  Extension point: custom SITE commands                                   ║
// ╚══════════════════════════════════════════════════════════════════════════╝

/// <summary>
/// Implement this interface (in addition to <see cref="IAmFtpdPlugin"/>) to register
/// one or more custom <c>SITE</c> sub-commands that are handled by the plugin.
/// </summary>
public interface ISiteCommandPlugin : IAmFtpdPlugin
{
    /// <summary>
    /// The SITE sub-command names this plugin handles (case-insensitive).
    /// E.g. <c>["DISCORD", "DISCORDTEST"]</c>.
    /// Must not overlap with built-in amFTPd commands.
    /// </summary>
    IReadOnlyList<string> CommandNames { get; }

    /// <summary>
    /// Optional help text lines for each command returned by <c>SITE HELP</c>.
    /// Keys match <see cref="CommandNames"/>; values are short usage descriptions.
    /// </summary>
    IReadOnlyDictionary<string, string> HelpLines { get; }

    /// <summary>
    /// Execute the SITE sub-command.
    /// </summary>
    /// <param name="context">Session context and FTP response writer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="PluginSiteResult"/> indicating whether the command was handled
    /// and the raw FTP response to send (including CRLF, e.g. <c>"200 Done.\r\n"</c>).
    /// Return <see cref="PluginSiteResult.NotHandled"/> to fall through to the built-in
    /// "502 Unknown SITE command" response.
    /// </returns>
    Task<PluginSiteResult> ExecuteAsync(PluginSiteContext context, CancellationToken ct);
}

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  Extension point: custom authentication providers                        ║
// ╚══════════════════════════════════════════════════════════════════════════╝

/// <summary>
/// Implement this interface to add an external authentication source such as LDAP,
/// OAuth2, a remote API, or a static password file.
/// </summary>
/// <remarks>
/// Auth providers are tried in the order they appear in the config.
/// The first non-<see cref="PluginAuthOutcome.Passthrough"/> result wins.
/// If all providers return <see cref="PluginAuthOutcome.Passthrough"/>, the
/// built-in amFTPd user-store authentication is used as the final fallback.
/// </remarks>
public interface IAuthProviderPlugin : IAmFtpdPlugin
{
    /// <summary>
    /// Attempt to authenticate a user.
    /// </summary>
    /// <param name="request">Username, password, and remote address.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="PluginAuthOutcome.Authenticated"/> to accept the login,
    /// <see cref="PluginAuthOutcome.Rejected"/> to deny it with a reason,
    /// or <see cref="PluginAuthOutcome.Passthrough"/> to skip this provider.
    /// </returns>
    Task<PluginAuthResult> AuthenticateAsync(PluginAuthRequest request, CancellationToken ct);
}

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  Extension point: FTP event handlers                                     ║
// ╚══════════════════════════════════════════════════════════════════════════╝

/// <summary>
/// Implement this interface to subscribe to FTP lifecycle events: uploads, downloads,
/// logins, nukes, pre, etc. Typical use cases are custom IRC announces, webhooks,
/// or custom logging.
/// </summary>
public interface IEventHandlerPlugin : IAmFtpdPlugin
{
    /// <summary>
    /// FTP event type names this plugin wants to receive.
    /// Use <c>["*"]</c> or an empty list to receive all events.
    /// Well-known type names: <c>Upload, Download, Delete, Login, Logout, Nuke,
    /// Unnuke, Pre, RaceUpdate, RaceComplete, AutoNuke, Oneliner, Request, Wipe</c>.
    /// </summary>
    IReadOnlyList<string> HandledEventTypes { get; }

    /// <summary>
    /// Called on every subscribed FTP event.
    /// This method is invoked asynchronously (fire-and-forget from the FTP session thread);
    /// exceptions are logged and swallowed.
    /// </summary>
    Task OnEventAsync(PluginFtpEvent ftpEvent, CancellationToken ct);
}
