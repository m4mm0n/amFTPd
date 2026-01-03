using amFTPd.Db.Abstractions;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Npgsql;

namespace amFTPd.Postgres;

/// <summary>
/// Provides an implementation of <see cref="ISqlProvider"/> for PostgreSQL databases.
/// </summary>
/// <remarks>Use this provider to create connections to PostgreSQL databases using Npgsql. This class is typically
/// registered with a provider registry and used to obtain <see cref="DbConnection"/> instances for
/// PostgreSQL.</remarks>
public sealed class PostgresProvider : ISqlProvider
{
    public string Name => "postgres";

    public DbConnection Create(string connectionString)
        => new NpgsqlConnection(connectionString);

    [ModuleInitializer]
    internal static void Init()
    {
        SqlProviderRegistry.Register(new PostgresProvider());
    }
}