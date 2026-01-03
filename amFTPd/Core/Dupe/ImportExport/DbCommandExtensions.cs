using System.Data.Common;

namespace amFTPd.Core.Dupe.ImportExport;

/// <summary>
/// Provides extension methods for the <see cref="System.Data.Common.DbCommand"/> class to simplify parameter handling.
/// </summary>
/// <remarks>These extension methods are intended to streamline common operations when working with database
/// commands, such as adding parameters. They can help reduce boilerplate code and improve readability when constructing
/// database queries.</remarks>
public static class DbCommandExtensions
{
    public static void AddParam(
        this DbCommand cmd,
        string name,
        object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}