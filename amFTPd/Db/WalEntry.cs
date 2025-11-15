namespace amFTPd.Db
{
    /// <summary>
    /// Represents a write-ahead log (WAL) entry containing a specific type and associated binary payload.
    /// </summary>
    /// <param name="Type">The type of the WAL entry, indicating its purpose or category.</param>
    /// <param name="Payload">The binary data associated with the WAL entry. This payload is identical in structure to a snapshot record.</param>
    public sealed record WalEntry(
        WalEntryType Type,
        byte[] Payload // binary record identical to snapshot record
    );
}
