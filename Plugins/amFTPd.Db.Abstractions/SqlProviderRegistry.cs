using System.Collections.Concurrent;

namespace amFTPd.Db.Abstractions;

/// <summary>
/// Provides a registry for managing and retrieving named SQL provider implementations.
/// </summary>
/// <remarks>The SqlProviderRegistry class enables registration and lookup of ISqlProvider instances by name.
/// Provider names are compared using a case-insensitive ordinal comparison. This class is thread-safe and intended for
/// use in scenarios where multiple SQL provider implementations need to be registered and accessed by name.</remarks>
public static class SqlProviderRegistry
{
    private static readonly ConcurrentDictionary<string, ISqlProvider> _providers =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers the specified SQL provider for use by the application.
    /// </summary>
    /// <remarks>If a provider with the same name is already registered, it will be replaced by the specified
    /// provider.</remarks>
    /// <param name="provider">The SQL provider to register. The provider's Name property must be unique and not null.</param>
    public static void Register(ISqlProvider provider) => _providers[provider.Name] = provider;
    /// <summary>
    /// Attempts to retrieve the SQL provider associated with the specified name.
    /// </summary>
    /// <param name="name">The name of the SQL provider to retrieve. Cannot be null.</param>
    /// <param name="provider">When this method returns, contains the SQL provider associated with the specified name, if found; otherwise,
    /// null. This parameter is passed uninitialized.</param>
    /// <returns>true if a provider with the specified name was found; otherwise, false.</returns>
    public static bool TryGet(
        string name,
        out ISqlProvider? provider)
        => _providers.TryGetValue(name, out provider);
    /// <summary>
    /// Gets a read-only collection of the names of all available providers.
    /// </summary>
    public static IReadOnlyCollection<string> Providers
        => _providers.Keys.ToArray();
}

