/*
 * ====================================================================================================
 *  Project:        amFTPd.Plugin.Abstractions
 *  File:           PluginTypes.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2026-04-25 00:00:00
 *  Last Modified:  2026-04-25 00:00:00
 *
 *  Description:
 *      Context objects, result types, and supporting records used by the plugin interfaces.
 *
 *  License:  MIT — https://opensource.org/licenses/MIT
 * ====================================================================================================
 */

namespace amFTPd.Plugin.Abstractions;

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  IPluginContext — safe runtime view given to every plugin                ║
// ╚══════════════════════════════════════════════════════════════════════════╝

/// <summary>
/// Safe, limited view of the amFTPd runtime given to each plugin during
/// <see cref="IAmFtpdPlugin.InitializeAsync"/>.
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// Absolute path to the directory containing <c>amftpd.json</c>.
    /// Plugins should store any files they create in a subdirectory here.
    /// </summary>
    string ConfigDirectory { get; }

    /// <summary>
    /// Key-value settings from the plugin's own <c>Settings</c> block in
    /// <c>amftpd.json</c>.
    /// </summary>
    IReadOnlyDictionary<string, string> Settings { get; }

    /// <summary>Write an INFO-level log entry to the amFTPd log.</summary>
    void LogInfo(string message);

    /// <summary>Write a WARN-level log entry to the amFTPd log.</summary>
    void LogWarn(string message);

    /// <summary>Write an ERROR-level log entry to the amFTPd log.</summary>
    void LogError(string message, Exception? ex = null);
}

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  SITE command context + result                                           ║
// ╚══════════════════════════════════════════════════════════════════════════╝

/// <summary>
/// Session context passed to <see cref="ISiteCommandPlugin.ExecuteAsync"/>.
/// </summary>
public sealed class PluginSiteContext
{
    /// <summary>The SITE sub-command verb (e.g. "DISCORD").</summary>
    public required string Verb { get; init; }

    /// <summary>Everything after the verb on the command line.</summary>
    public required string Argument { get; init; }

    /// <summary>Username of the currently authenticated user.</summary>
    public required string Username { get; init; }

    /// <summary>Whether the session user has admin privileges.</summary>
    public bool IsAdmin { get; init; }

    /// <summary>Whether the session user has siteop privileges (or admin implies siteop).</summary>
    public bool IsSiteop { get; init; }

    /// <summary>Remote IP address of the connected client.</summary>
    public required string RemoteIp { get; init; }

    /// <summary>Current virtual working directory of the session.</summary>
    public string? CurrentPath { get; init; }

    /// <summary>
    /// Sends a raw FTP response line back to the client.
    /// The string must include the trailing CRLF, e.g. <c>"200 Done.\r\n"</c>.
    /// Multiple lines are allowed for multi-line responses.
    /// </summary>
    public required Func<string, CancellationToken, Task> WriteResponseAsync { get; init; }

    /// <summary>Plugin logger (same as the context passed during initialization).</summary>
    public required IPluginContext PluginContext { get; init; }
}

/// <summary>
/// Result returned by <see cref="ISiteCommandPlugin.ExecuteAsync"/>.
/// </summary>
public sealed class PluginSiteResult
{
    /// <summary>
    /// The plugin handled the command.
    /// The response was already written via <see cref="PluginSiteContext.WriteResponseAsync"/>.
    /// </summary>
    public bool Handled { get; init; }

    /// <summary>Convenience: command was not handled; fall through.</summary>
    public static readonly PluginSiteResult NotHandled = new() { Handled = false };

    /// <summary>Convenience: command handled, response already written.</summary>
    public static readonly PluginSiteResult Done = new() { Handled = true };
}

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  Auth provider request + result                                          ║
// ╚══════════════════════════════════════════════════════════════════════════╝

/// <summary>
/// Authentication request passed to <see cref="IAuthProviderPlugin.AuthenticateAsync"/>.
/// </summary>
public sealed class PluginAuthRequest
{
    /// <summary>Username supplied by the FTP client.</summary>
    public required string Username { get; init; }

    /// <summary>Password supplied by the FTP client (plaintext).</summary>
    public required string Password { get; init; }

    /// <summary>Remote IP address of the connecting client.</summary>
    public required string RemoteIp { get; init; }
}

/// <summary>
/// Outcome of an <see cref="IAuthProviderPlugin.AuthenticateAsync"/> call.
/// </summary>
public enum PluginAuthOutcome
{
    /// <summary>
    /// This provider has no opinion. Continue to the next provider or fall back
    /// to the built-in amFTPd user-store authentication.
    /// </summary>
    Passthrough,

    /// <summary>
    /// The credentials are valid. The user will be looked up in the local user store
    /// by their username. If the user does not exist locally, the login is denied.
    /// </summary>
    Authenticated,

    /// <summary>
    /// The credentials are explicitly rejected. No further providers are tried.
    /// </summary>
    Rejected
}

/// <summary>
/// Result returned by <see cref="IAuthProviderPlugin.AuthenticateAsync"/>.
/// </summary>
public sealed class PluginAuthResult
{
    /// <summary>
    /// Authentication decision returned by the plugin.
    /// </summary>
    public PluginAuthOutcome Outcome { get; init; }

    /// <summary>
    /// Human-readable rejection reason (shown to the client as a 530 response).
    /// Only relevant when <see cref="Outcome"/> is <see cref="PluginAuthOutcome.Rejected"/>.
    /// </summary>
    public string? RejectReason { get; init; }

    /// <summary>
    /// Continue to the next auth provider or the built-in user store.
    /// </summary>
    public static readonly PluginAuthResult Passthrough =
        new() { Outcome = PluginAuthOutcome.Passthrough };

    /// <summary>
    /// Accept the credentials and continue with local user lookup.
    /// </summary>
    public static readonly PluginAuthResult Authenticated =
        new() { Outcome = PluginAuthOutcome.Authenticated };

    /// <summary>
    /// Reject the credentials with a client-visible reason.
    /// </summary>
    public static PluginAuthResult Reject(string reason) =>
        new() { Outcome = PluginAuthOutcome.Rejected, RejectReason = reason };
}

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  FTP event                                                               ║
// ╚══════════════════════════════════════════════════════════════════════════╝

/// <summary>
/// A snapshot of a single FTP lifecycle event delivered to
/// <see cref="IEventHandlerPlugin.OnEventAsync"/>.
/// </summary>
public sealed class PluginFtpEvent
{
    /// <summary>
    /// Event type string. Well-known values:
    /// <c>Upload, Download, Delete, Mkdir, Rmdir, Login, Logout,
    /// Nuke, Unnuke, Wipe, Pre, RaceUpdate, RaceComplete,
    /// ZipscriptStatus, Oneliner, Request, AutoNuke</c>.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>UTC timestamp of the event.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Username of the session that produced the event (may be null for system events).</summary>
    public string? Username { get; init; }

    /// <summary>Primary group of the session user.</summary>
    public string? Group { get; init; }

    /// <summary>Section name (e.g. "MP3", "0DAY").</summary>
    public string? Section { get; init; }

    /// <summary>Virtual path of the file or directory involved.</summary>
    public string? VirtualPath { get; init; }

    /// <summary>Release name (directory name) when applicable.</summary>
    public string? ReleaseName { get; init; }

    /// <summary>Transfer size in bytes.</summary>
    public long Bytes { get; init; }

    /// <summary>
    /// Nuke / unnuke reason, or any other textual payload specific to the event type.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>Remote host of the session.</summary>
    public string? RemoteHost { get; init; }

    /// <summary>
    /// Additional event-type-specific fields.
    /// Check the amFTPd documentation for the keys available for each event type.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Extra { get; init; }
}
