/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           BanList.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-11 03:01:41
 *  Last Modified:  2025-12-11 03:01:41
 *  CRC32:          0xDA551F7E
 *  
 *  Description:
 *      Central IP / CIDR ban list, used by FtpServer (and optionally other subsystems).
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */

using System.Collections.Concurrent;
using System.Net;

namespace amFTPd.Security.BanList;

/// <summary>
/// Central IP / CIDR ban list, used by FtpServer (and optionally other subsystems).
/// </summary>
public sealed class BanList
{
    // Keys: "ip:1.2.3.4" or "cidr:1.2.3.0/24"
    private readonly ConcurrentDictionary<string, BanEntry> _bans = new();

    /// <summary>
    /// Checks whether an IP is banned. Cleans up expired bans on the fly.
    /// </summary>
    public bool IsBanned(IPAddress address, out string? reason)
    {
        var now = DateTime.UtcNow;
        reason = null;
        var banned = false;

        foreach (var (key, entry) in _bans)
        {
            if (entry.IsExpired(now))
            {
                _bans.TryRemove(key, out _);
                continue;
            }

            if (entry.Address is not null)
            {
                if (address.Equals(entry.Address))
                {
                    reason = entry.Reason;
                    banned = true;
                    break;
                }

                continue;
            }

            if (entry.Cidr is { } cidr)
            {
                if (cidr.Contains(address))
                {
                    reason = entry.Reason;
                    banned = true;
                    break;
                }
            }
        }

        return banned;
    }

    /// <summary>
    /// Adds a temporary ban for a single IP.
    /// </summary>
    public void AddTemporaryBan(IPAddress address, TimeSpan duration, string? reason = null)
    {
        var key = $"ip:{address}";
        var entry = new BanEntry
        {
            Address = address,
            Cidr = null,
            ExpiresUtc = DateTime.UtcNow + duration,
            Reason = reason
        };

        _bans.AddOrUpdate(key, entry, (_, _) => entry);
    }

    /// <summary>
    /// Adds a permanent ban for a single IP.
    /// </summary>
    public void AddPermanentBan(IPAddress address, string? reason = null)
    {
        var key = $"ip:{address}";
        var entry = new BanEntry
        {
            Address = address,
            Cidr = null,
            ExpiresUtc = null,
            Reason = reason
        };

        _bans.AddOrUpdate(key, entry, (_, _) => entry);
    }

    /// <summary>
    /// Adds a temporary ban for a CIDR block (IPv4).
    /// </summary>
    public void AddTemporaryCidrBan(IPAddress network, int prefixLength, TimeSpan duration, string? reason = null)
    {
        var cidr = new CidrBlock(network, prefixLength);
        var key = $"cidr:{cidr}";

        var entry = new BanEntry
        {
            Address = null,
            Cidr = cidr,
            ExpiresUtc = DateTime.UtcNow + duration,
            Reason = reason
        };

        _bans.AddOrUpdate(key, entry, (_, _) => entry);
    }

    /// <summary>
    /// Adds a permanent ban for a CIDR block (IPv4).
    /// </summary>
    public void AddPermanentCidrBan(IPAddress network, int prefixLength, string? reason = null)
    {
        var cidr = new CidrBlock(network, prefixLength);
        var key = $"cidr:{cidr}";

        var entry = new BanEntry
        {
            Address = null,
            Cidr = cidr,
            ExpiresUtc = null,
            Reason = reason
        };

        _bans.AddOrUpdate(key, entry, (_, _) => entry);
    }

    /// <summary>
    /// Optional manual cleanup if you want to run it periodically.
    /// </summary>
    public void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (key, entry) in _bans)
        {
            if (entry.IsExpired(now))
            {
                _bans.TryRemove(key, out _);
            }
        }
    }
}