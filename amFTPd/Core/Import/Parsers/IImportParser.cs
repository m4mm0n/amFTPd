namespace amFTPd.Core.Import.Parsers;

/// <summary>
/// Defines a parser that reads data from a specified root path and produces a collection of objects of type T.
/// </summary>
/// <remarks>Implementations of this interface are responsible for interpreting the data found at the given root
/// path and converting it into instances of type T. The specific format and structure of the data depend on the
/// implementation.</remarks>
/// <typeparam name="T">The type of objects produced by the parser.</typeparam>
public interface IImportParser<T>
{
    /// <summary>
    /// Parses the specified root directory and returns a collection of items of type T found within it.
    /// </summary>
    /// <param name="rootPath">The full path to the root directory to parse. Cannot be null or empty.</param>
    /// <returns>An enumerable collection of items of type T parsed from the specified root directory. The collection is empty if
    /// no items are found.</returns>
    IEnumerable<T> Parse(string rootPath);
}