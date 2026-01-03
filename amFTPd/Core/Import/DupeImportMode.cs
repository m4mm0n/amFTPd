namespace amFTPd.Core.Import;

/// <summary>
/// Specifies how duplicate items are handled during an import operation.
/// </summary>
/// <remarks>Use this enumeration to control whether duplicate items are merged, overwritten, or skipped when
/// importing data. The selected mode determines how existing items with matching identifiers are processed.</remarks>
public enum DupeImportMode
{
    Merge,
    Overwrite,
    Skip
}