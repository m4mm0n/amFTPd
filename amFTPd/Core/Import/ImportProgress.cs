namespace amFTPd.Core.Import;

/// <summary>
/// Represents the progress of an import operation, including status information and metrics.
/// </summary>
/// <remarks>This class is typically used to report or monitor the state of a long-running import process. It
/// provides details such as the name of the operation, the total number of items to process, the number of items
/// processed so far, whether cancellation has been requested, and the time the import started. Instances of this class
/// are immutable except for the fields, which may be updated to reflect progress.</remarks>
public sealed class ImportProgress
{
    public string Name { get; init; } = "";
    public int Total { get; init; }
    public int Processed;
    public volatile bool CancelRequested;
    public DateTimeOffset Started = DateTimeOffset.UtcNow;
}