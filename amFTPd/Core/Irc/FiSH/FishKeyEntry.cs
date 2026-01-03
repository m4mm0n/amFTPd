namespace amFTPd.Core.Irc.FiSH;

/// <summary>
/// Represents an entry containing a cryptographic key, its associated target, and the key mode for use with the Fish
/// encryption algorithm.
/// </summary>
/// <remarks>Instances of this class are immutable with respect to the target, but the key and mode may change
/// depending on usage. This class is intended for use with Fish encryption operations that require tracking of key
/// material and its mode.</remarks>
public sealed class FishKeyEntry
{
    public string Target { get; }
    public string Key { get; private set; }
    public FishKeyMode Mode { get; internal set; }

    public FishKeyEntry(string target, string key, FishKeyMode mode)
    {
        Target = target;
        Key = key;
        Mode = mode;
    }

    internal void UpgradeToCbc(string newKey)
    {
        Key = newKey;
        Mode = FishKeyMode.Cbc;
    }
}