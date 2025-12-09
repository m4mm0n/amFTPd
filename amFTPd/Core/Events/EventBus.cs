/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           EventBus.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 03:48:28
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xF3289256
 *  
 *  Description:
 *      Simple in-process pub/sub bus for FTP events. Thread-safe, low overhead.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using System.Collections.Concurrent;

namespace amFTPd.Core.Events;

/// <summary>
/// Simple in-process pub/sub bus for FTP events.
/// Thread-safe, low overhead.
/// </summary>
public sealed class EventBus
{
    private readonly Lock _lock = new();
    private readonly List<Action<FtpEvent>> _handlers = new();

    /// <summary>Subscribe to all future events.</summary>
    public void Subscribe(Action<FtpEvent> handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        lock (_lock) _handlers.Add(handler);
    }

    /// <summary>Publish an event to all subscribers.</summary>
    public void Publish(FtpEvent ev)
    {
        if (ev is null) throw new ArgumentNullException(nameof(ev));

        Action<FtpEvent>[] snapshot;
        lock (_lock)
        {
            if (_handlers.Count == 0)
                return;

            snapshot = _handlers.ToArray();
        }

        foreach (var handler in snapshot)
        {
            try
            {
                handler(ev);
            }
            catch
            {
                // Swallow handler exceptions so a bad subscriber can't take down the daemon.
                // You can log here if you want.
            }
        }
    }

    // --------------------------------------------------------------------
    // ACTIVE SESSION REGISTRY
    // --------------------------------------------------------------------
    private readonly ConcurrentDictionary<FtpSession, byte> _sessions
        = new ConcurrentDictionary<FtpSession, byte>();

    /// <summary>
    /// Register an FTP session as active. Called by FtpSession/FtpServer on connect.
    /// </summary>
    public void RegisterSession(FtpSession session)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        _sessions[session] = 0;
    }

    /// <summary>
    /// Unregister an FTP session. Called on disconnect.
    /// </summary>
    public void UnregisterSession(FtpSession session)
    {
        if (session is null) return;
        _sessions.TryRemove(session, out _);
    }

    /// <summary>
    /// Returns the current snapshot of all active FTP sessions.
    /// Delegates to FtpSession's internal registry.
    /// </summary>
    public IReadOnlyCollection<FtpSession> GetActiveSessions()
        => FtpSession.GetActiveSessions();
}