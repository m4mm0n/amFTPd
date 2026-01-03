namespace amFTPd.Core.Import.Records;

public sealed record ImportedNukeRecord(
    string Section,
    string Path,
    int Multiplier,
    string Reason,
    string Nuker,
    DateTimeOffset Timestamp);