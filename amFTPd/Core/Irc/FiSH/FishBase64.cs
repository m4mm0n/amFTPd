namespace amFTPd.Core.Irc.FiSH;

/// <summary>
/// Provides methods for encoding and decoding data using the crypt(3) "fish" variant of Base64 encoding, commonly used
/// in Unix password hashing schemes.
/// </summary>
/// <remarks>This class is intended for internal use in scenarios where compatibility with the "fish" Base64
/// encoding is required. The encoding uses a custom 64-character alphabet
/// ("./0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ") and is not compatible with standard Base64.
/// Methods in this class expect input and output buffers to be sized according to the encoding's block requirements;
/// incorrect buffer sizes may result in data loss or exceptions.</remarks>
internal static class FishBase64
{
    private const string A = "./0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static byte[] Encode(byte[] data)
    {
        var outBuf = new byte[data.Length / 2 * 3];
        var j = 0;

        for (var i = 0; i < data.Length; i += 8)
        {
            var l = (uint)(data[i] << 24 | data[i + 1] << 16 | data[i + 2] << 8 | data[i + 3]);
            var r = (uint)(data[i + 4] << 24 | data[i + 5] << 16 | data[i + 6] << 8 | data[i + 7]);

            for (var k = 0; k < 6; k++, j++) { outBuf[j] = (byte)A[(int)(r & 0x3F)]; r >>= 6; }
            for (var k = 0; k < 6; k++, j++) { outBuf[j] = (byte)A[(int)(l & 0x3F)]; l >>= 6; }
        }
        return outBuf;
    }

    public static byte[] Decode(byte[] data)
    {
        var outBuf = new byte[data.Length / 2 * 3];
        var j = 0;

        for (var i = 0; i < data.Length; i += 12)
        {
            uint r = 0, l = 0;
            for (var k = 0; k < 6; k++) r |= (uint)A.IndexOf((char)data[i + k]) << (k * 6);
            for (var k = 0; k < 6; k++) l |= (uint)A.IndexOf((char)data[i + k + 6]) << (k * 6);

            for (var k = 0; k < 4; k++, j++) outBuf[j] = (byte)(l >> (24 - k * 8));
            for (var k = 0; k < 4; k++, j++) outBuf[j] = (byte)(r >> (24 - k * 8));
        }
        return TrimZero(outBuf);
    }

    private static byte[] TrimZero(byte[] data)
    {
        var len = data.Length;
        while (len > 0 && data[len - 1] == 0) len--;
        Array.Resize(ref data, len);
        return data;
    }
}