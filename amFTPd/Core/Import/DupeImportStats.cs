namespace amFTPd.Core.Import;

/// <summary>
/// Represents summary statistics for a duplicate import operation, including counts of total, inserted, updated,
/// skipped, and nuked items.
/// </summary>
/// <remarks>Use this class to track the results of an import process that may encounter duplicate records. Each
/// field provides the count of items in a specific outcome category.</remarks>
public sealed class DupeImportStats
{
    public int Total;
    public int Inserted;
    public int Updated;
    public int Skipped;
    public int Nuked;
}