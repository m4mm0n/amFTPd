using System.Collections.Concurrent;

namespace amFTPd.Core.Stats.Live;

/// <summary>
/// Provides thread-safe registries for tracking live statistics of users, sections, and IP addresses.
/// </summary>
/// <remarks>This class exposes concurrent dictionaries for aggregating and accessing real-time statistics. It is
/// intended for scenarios where multiple threads may update or query live stats concurrently. The class is sealed to
/// prevent inheritance.</remarks>
public sealed class LiveStatsRegistry
{
    public ConcurrentDictionary<string, UserLiveStats> Users { get; } = new();
    public ConcurrentDictionary<string, SectionLiveStats> Sections { get; } = new();
    public ConcurrentDictionary<string, IpLiveStats> Ips { get; } = new();

    public void AttachUserToIp(string userName, string ipKey)
    {
        var user = Users.GetOrAdd(
            userName,
            u => new UserLiveStats { UserName = u });

        user.CurrentIpKey = ipKey;
    }

    public void DetachUserFromIp(string userName)
    {
        if (Users.TryGetValue(userName, out var user))
        {
            user.CurrentIpKey = null;
        }
    }
}