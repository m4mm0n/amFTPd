namespace amFTPd.Core.Services;

/// <summary>
/// Defines a set of services for executing site-level commands, including messaging, credit management, scene actions,
/// user and group queries, and auditing operations.
/// </summary>
/// <remarks>This interface provides methods for interacting with site infrastructure, such as sending messages,
/// managing user credits, triggering scene-related actions, verifying user and group existence, and logging site
/// activities. Implementations should ensure thread safety and handle cancellation tokens appropriately for
/// asynchronous operations.</remarks>
public interface ISiteCommandServices
{
    // --- messaging ---
    Task WriteLineAsync(string message, CancellationToken ct);

    // --- credits / ratio (PATCH 3 will implement logic) ---
    Task AddCreditsAsync(string username, long bytes);
    Task RemoveCreditsAsync(string username, long bytes);

    // --- scene actions (PATCH 4/5 will wire) ---
    Task TriggerPreAsync(string path);
    Task TriggerNukeAsync(string path, string reason);
    Task TriggerUnnukeAsync(string path);

    // --- queries ---
    bool UserExists(string username);
    bool GroupExists(string group);

    // --- auditing ---
    void LogSiteAction(string action, string? details = null);
}
