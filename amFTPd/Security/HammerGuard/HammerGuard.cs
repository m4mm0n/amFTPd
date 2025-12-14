/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           HammerGuard.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-11 03:00:22
 *  Last Modified:  2025-12-14 00:24:09
 *  CRC32:          0x63DCADCE
 *  
 *  Description:
 *      Centralized rate limiting and abuse detection for commands and logins. Stateless callers, stateful per-IP inside.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


using System.Collections.Concurrent;
using System.Net;
using amFTPd.Config.Ftpd;

namespace amFTPd.Security.HammerGuard;

/// <summary>
/// Centralized rate limiting and abuse detection for commands and logins.
/// Stateless callers, stateful per-IP inside.
/// </summary>
public sealed class HammerGuard
{
    private readonly FtpConfig _config;
    private readonly ConcurrentDictionary<IPAddress, HammerState> _states =
        new();

    // Tuning knobs (internal constants)
    private static readonly TimeSpan FailedLoginWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultBanDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CommandWindow = TimeSpan.FromMinutes(1);

    public HammerGuard(FtpConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Registers a failed login attempt for the given IP.
    /// Returns a decision that may request a temp ban.
    /// </summary>
    public HammerDecision RegisterFailedLogin(IPAddress address)
    {
        var now = DateTime.UtcNow;
        var state = _states.GetOrAdd(address, _ => new HammerState
        {
            LastTouchedUtc = now,
            FirstFailureUtc = now,
            FailedLoginCount = 0,
            WindowStartUtc = now,
            WindowCommandCount = 0
        });

        lock (state)
        {
            state.LastTouchedUtc = now;

            if (now - state.FirstFailureUtc > FailedLoginWindow)
            {
                // Window expired, reset
                state.FirstFailureUtc = now;
                state.FailedLoginCount = 0;
            }

            state.FailedLoginCount++;

            var maxFailures = _config.MaxFailedLoginsPerIp;
            if (maxFailures <= 0)
            {
                // Tracking only, no auto-ban
                return HammerDecision.None;
            }

            if (state.FailedLoginCount > maxFailures)
            {
                return new HammerDecision(
                    ShouldThrottle: false,
                    ShouldBan: true,
                    ThrottleDelay: TimeSpan.Zero,
                    BanDuration: DefaultBanDuration,
                    Reason: $"Too many failed logins ({state.FailedLoginCount}/{maxFailures}) from {address}");
            }

            return HammerDecision.None;
        }
    }

    /// <summary>
    /// Registers a command execution for a given IP.
    /// Uses per-IP window and per-session commands-per-minute
    /// to decide throttling or short bans.
    /// </summary>
    /// <param name="address">Client IP.</param>
    /// <param name="verb">Command verb (USER, PASS, RETR, etc.).</param>
    /// <param name="currentSessionCommandsPerMinute">Already computed commands per minute for the current session.</param>
    /// <param name="maxCommandsPerMinute"></param>
    public HammerDecision RegisterCommand(
        IPAddress address,
        string verb,
        int currentSessionCommandsPerMinute,
        int maxCommandsPerMinute)
    {
        var now = DateTime.UtcNow;
        var state = _states.GetOrAdd(address, _ => new HammerState
        {
            LastTouchedUtc = now,
            FirstFailureUtc = now,
            FailedLoginCount = 0,
            WindowStartUtc = now,
            WindowCommandCount = 0
        });

        var maxPerMinute = maxCommandsPerMinute;
        if (maxPerMinute <= 0)
        {
            // No command-rate limits configured
            return HammerDecision.None;
        }

        lock (state)
        {
            state.LastTouchedUtc = now;

            // Reset window if expired
            if (now - state.WindowStartUtc > CommandWindow)
            {
                state.WindowStartUtc = now;
                state.WindowCommandCount = 0;
            }

            state.WindowCommandCount++;

            var ipWindowCount = state.WindowCommandCount;

            // Heuristic thresholds:
            //
            // - If session CPM is slightly above max -> throttle.
            // - If session CPM is way above max OR IP window way above -> temp ban.
            //
            var sessionCpm = currentSessionCommandsPerMinute;

            // Over-max but not insane -> throttle
            if (sessionCpm > maxPerMinute && sessionCpm <= maxPerMinute * 2)
            {
                return new HammerDecision(
                    ShouldThrottle: true,
                    ShouldBan: false,
                    ThrottleDelay: TimeSpan.FromMilliseconds(500),
                    BanDuration: null,
                    Reason:
                    $"Session CPM {sessionCpm} > max {maxPerMinute} for {address}, throttling.");
            }

            // Very abusive session, or IP-wide hammering
            if (sessionCpm > maxPerMinute * 2 || ipWindowCount > maxPerMinute * 3)
            {
                var reason =
                    $"Command flood detected from {address} (verb={verb}, " +
                    $"session CPM={sessionCpm}, ip-window={ipWindowCount}, max={maxPerMinute}).";

                return new HammerDecision(
                    ShouldThrottle: false,
                    ShouldBan: true,
                    ThrottleDelay: TimeSpan.Zero,
                    BanDuration: TimeSpan.FromMinutes(10),
                    Reason: reason);
            }

            return HammerDecision.None;
        }
    }

    /// <summary>
    /// Optional: can be called periodically to discard stale IP entries.
    /// </summary>
    public void CleanupStaleEntries(TimeSpan olderThan)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        foreach (var (ip, state) in _states)
        {
            if (state.LastTouchedUtc < cutoff)
            {
                _states.TryRemove(ip, out _);
            }
        }
    }
}