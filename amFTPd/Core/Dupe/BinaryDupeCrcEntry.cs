namespace amFTPd.Core.Dupe;

/// <summary>
/// Represents an entry that associates a file name with its CRC32 checksum for duplicate detection purposes.
/// </summary>
internal readonly struct BinaryDupeCrcEntry
{
    public readonly uint Crc;
    public readonly string FileName;
    public readonly long FileSize;

    public BinaryDupeCrcEntry(string file, uint crc, long fileSize)
    {
        FileName = file;
        Crc = crc;
        FileSize = fileSize;
    }
}
