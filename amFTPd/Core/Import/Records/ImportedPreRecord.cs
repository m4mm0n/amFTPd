namespace amFTPd.Core.Import.Records;

/// <summary>
/// 
/// </summary>
/// <param name="Section"></param>
/// <param name="Path"></param>
/// <param name="Group"></param>
/// <param name="Timestamp"></param>
public sealed record ImportedPreRecord(
    string Section,
    string Path,
    string Group,
    DateTimeOffset Timestamp);