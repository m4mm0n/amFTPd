using System.Collections.Concurrent;
using System.Collections.Generic;

namespace amFTPd.Core.Irc.FiSH;

/// <summary>
/// Provides a thread-safe store for managing FishKey cryptographic keys associated with specific targets.
/// </summary>
/// <remarks>FishKeyStore allows adding, retrieving, and updating cryptographic keys for different targets,
/// supporting multiple key modes. All operations are safe for concurrent access from multiple threads.</remarks>
public sealed class FishKeyStore
{
    private readonly ConcurrentDictionary<string, FishKeyEntry> _keys = new();

    /// <summary>
    /// Adds a new entry for the specified target using the provided key in ECB (Electronic Codebook) mode.
    /// </summary>
    /// <param name="target">The identifier for which the key entry will be associated. Cannot be null or empty.</param>
    /// <param name="key">The cryptographic key to associate with the specified target. Cannot be null or empty.</param>
    public void AddEcb(string target, string key) => _keys[target] = new FishKeyEntry(target, key, FishKeyMode.Ecb);
    /// <summary>
    /// Adds a new key entry for the specified target using CBC mode encryption.
    /// </summary>
    /// <param name="target">The identifier for which the key entry is associated. Cannot be null or empty.</param>
    /// <param name="key">The encryption key to associate with the target. Cannot be null or empty.</param>
    public void AddCbc(string target, string key) => _keys[target] = new FishKeyEntry(target, key, FishKeyMode.Cbc);

    /// <summary>
    /// Attempts to retrieve the entry associated with the specified target key.
    /// </summary>
    /// <param name="target">The key to locate in the collection. Cannot be null.</param>
    /// <param name="entry">When this method returns, contains the entry associated with the specified key, if the key is found; otherwise,
    /// the default value for <see cref="FishKeyEntry"/>. This parameter is passed uninitialized.</param>
    /// <returns>true if the entry was found for the specified key; otherwise, false.</returns>
    public bool TryGet(string target, out FishKeyEntry entry)
        => _keys.TryGetValue(target, out entry);
    /// <summary>
    /// Marks the specified key as pending, indicating that it is awaiting further processing or action.
    /// </summary>
    /// <param name="target">The identifier of the key to mark as pending. Cannot be null.</param>
    public void MarkPending(string target)
    {
        if (_keys.TryGetValue(target, out var entry))
            entry.Mode = FishKeyMode.Pending;
    }
    /// <summary>
    /// Upgrades the encryption mode for the specified target to CBC using the provided key.
    /// </summary>
    /// <param name="target">The identifier of the target whose encryption mode will be upgraded. Cannot be null or empty.</param>
    /// <param name="newKey">The new encryption key to use for CBC mode. Cannot be null or empty.</param>
    public void UpgradeToCbc(string target, string newKey)
    {
        if (_keys.TryGetValue(target, out var entry))
            entry.UpgradeToCbc(newKey);
    }
    /// <summary>
    /// Rebinds an existing entry from the specified old nickname to a new nickname.
    /// </summary>
    /// <remarks>If the specified old nickname does not exist, the method performs no action. If the new
    /// nickname already exists, its entry will be overwritten.</remarks>
    /// <param name="oldNick">The current nickname associated with the entry to be rebound. Cannot be null.</param>
    /// <param name="newNick">The new nickname to associate with the entry. Cannot be null.</param>
    public void Rebind(string oldNick, string newNick)
    {
        if (_keys.Remove(oldNick, out var entry)) _keys[newNick] = entry;
    }
}