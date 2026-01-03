namespace amFTPd.Utils.Cryptography;

/// <summary>
/// Write-only stream wrapper that computes CRC32 incrementally
/// using the existing Crc32 API.
/// </summary>
internal sealed class Crc32WriteStream : Stream
{
    private readonly Stream _inner;
    private uint _crc;

    public Crc32WriteStream(Stream inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _crc = Crc32.Seed();
    }

    /// <summary>
    /// Final CRC32 value (valid after last write).
    /// </summary>
    public uint Hash => Crc32.Finalize(_crc);

    public override void Write(byte[] buffer, int offset, int count)
    {
        _crc = Crc32.Append(_crc, buffer.AsSpan(offset, count));
        _inner.Write(buffer, offset, count);
    }

    public override async Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        _crc = Crc32.Append(_crc, buffer.AsSpan(offset, count));
        await _inner.WriteAsync(buffer, offset, count, cancellationToken)
            .ConfigureAwait(false);
    }

    #region passthrough
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);
    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();
    public override void SetLength(long value) => _inner.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
    #endregion
}