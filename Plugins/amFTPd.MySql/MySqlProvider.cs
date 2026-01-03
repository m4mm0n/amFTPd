using amFTPd.Db.Abstractions;
using System.Data.Common;
using System.Runtime.CompilerServices;
using MySql.Data.MySqlClient;

namespace amFTPd.MySql;

/// <summary>
/// Provides an implementation of the ISqlProvider interface for MySQL databases.
/// </summary>
/// <remarks>Use this provider to create MySQL database connections and to identify the provider type as "mysql"
/// when working with systems that support multiple SQL providers.</remarks>
public sealed class MySqlProvider : ISqlProvider
{
    public string Name => "mysql";

    public DbConnection Create(string connectionString)
        => new MySqlConnection(connectionString);

    [ModuleInitializer]
    internal static void Init()
    {
        SqlProviderRegistry.Register(new MySqlProvider());
    }
}
