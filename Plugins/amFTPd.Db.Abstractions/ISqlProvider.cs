using System.Data.Common;

namespace amFTPd.Db.Abstractions;

/// <summary>
/// Defines a contract for creating database connections for a specific SQL provider.
/// </summary>
/// <remarks>Implementations of this interface encapsulate provider-specific logic for establishing connections to
/// SQL databases. This interface is typically used to abstract database connectivity in applications that support
/// multiple SQL providers.</remarks>
public interface ISqlProvider
{
    /// <summary>
    /// Gets the name associated with the current instance.
    /// </summary>
    string Name { get; }
    /// <summary>
    /// Creates and returns a new database connection using the specified connection string.
    /// </summary>
    /// <param name="connectionString">The connection string that contains the information required to establish the database connection. Cannot be
    /// null or empty.</param>
    /// <returns>A new <see cref="DbConnection"/> instance configured with the specified connection string. The connection is not
    /// opened automatically.</returns>
    DbConnection Create(string connectionString);
}