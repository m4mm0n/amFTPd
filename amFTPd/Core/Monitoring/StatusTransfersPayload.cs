namespace amFTPd.Core.Monitoring;

/// <summary>
/// Represents a snapshot of transfer status metrics, including data volume and concurrency information for ongoing and
/// completed transfers.
/// </summary>
/// <remarks>This class is typically used to report or monitor the current state of data transfers, such as in
/// file upload or download operations. All properties are immutable and reflect the state at the time the object was
/// created.</remarks>
public sealed class StatusTransfersPayload
{
    public long BytesUploaded { get; init; }
    public long BytesDownloaded { get; init; }
    public int ActiveTransfers { get; init; }
    public long TotalTransfers { get; init; }
    public double AverageTransferMilliseconds { get; init; }
    public int MaxConcurrentTransfers { get; init; }
}