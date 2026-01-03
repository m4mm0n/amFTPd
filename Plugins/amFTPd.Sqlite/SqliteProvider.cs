using amFTPd.Db.Abstractions;
using Microsoft.Data.Sqlite;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace amFTPd.Sqlite;

/// <summary>
/// Provides an implementation of the ISqlProvider interface for SQLite databases.
/// </summary>
/// <remarks>Use this provider to create connections to SQLite databases using a connection string. This class is
/// typically registered with a provider registry and is not intended to be instantiated directly by application
/// code.</remarks>
public sealed class SqliteProvider : ISqlProvider
{
    public string Name => "sqlite";

    public DbConnection Create(string connectionString)
        => new SqliteConnection(connectionString);

    [ModuleInitializer]
    internal static void Init()
    {
        SqlProviderRegistry.Register(new SqliteProvider());
    }
}