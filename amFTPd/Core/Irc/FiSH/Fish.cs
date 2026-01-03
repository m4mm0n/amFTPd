using System.Security.Cryptography;
using System.Text;

namespace amFTPd.Core.Irc.FiSH;

/// <summary>
/// Provides Blowfish-based encryption and decryption of messages using the FiSH protocol style, with optional text
/// encoding support.
/// </summary>
/// <remarks>The Fish class enables symmetric encryption and decryption of string messages, compatible with the
/// FiSH protocol commonly used in IRC clients. Instances are initialized with a secret key and an optional text
/// encoding; if no encoding is specified, UTF-8 is used by default. This class is not thread-safe. For best security,
/// use a strong, unpredictable key.</remarks>
public sealed class Fish
{
    private readonly Blowfish _bf;
    private readonly Encoding _enc;
    private readonly FishKeyMode _mode;

    /// <summary>
    /// Initializes a new instance of the Fish class using the specified key, cipher mode, and character encoding.
    /// </summary>
    /// <param name="key">The secret key used for encryption and decryption operations. Cannot be null or empty.</param>
    /// <param name="mode">The cipher mode to use for encryption. Defaults to Ecb if not specified.</param>
    /// <param name="encoding">The character encoding used to convert the key to bytes. If null, UTF8 encoding is used.</param>
    public Fish(string key, FishKeyMode mode = FishKeyMode.Ecb, Encoding? encoding = null)
    {
        _mode = mode;
        _enc = encoding ?? Encoding.UTF8;
        _bf = new Blowfish(_enc.GetBytes(key));
    }
    /// <summary>
    /// Encrypts the specified message using the configured encryption mode and returns the result as a formatted
    /// string.
    /// </summary>
    /// <remarks>The encryption mode is determined by the current value of the <see cref="FishKeyMode"/>. The
    /// output format and encoding may differ depending on the selected mode. The method does not validate the message
    /// content; callers should ensure the input is suitable for encryption.</remarks>
    /// <param name="message">The plaintext message to encrypt. Cannot be null.</param>
    /// <returns>A string containing the encrypted message, formatted with a "+OK" prefix. The output is base64-encoded if using
    /// CBC mode, or FishBase64-encoded if using ECB mode.</returns>
    public string Encrypt(string message)
    {
        var plain = _enc.GetBytes(message);

        if (_mode == FishKeyMode.Cbc)
        {
            var enc = CbcEncrypt(plain);
            return "+OK *" + Convert.ToBase64String(enc);
        }
        else
        {
            var enc = EcbEncrypt(plain);
            return "+OK " + _enc.GetString(FishBase64.Encode(enc));
        }
    }
    /// <summary>
    /// Decrypts a message that is formatted according to the expected protocol, returning the original plaintext if
    /// decryption is successful.
    /// </summary>
    /// <remarks>Messages prefixed with "+OK *" are decrypted using CBC mode, while other messages prefixed
    /// with "+OK " are decrypted using ECB mode. If the input does not match the expected format, no decryption is
    /// performed.</remarks>
    /// <param name="message">The encrypted message to decrypt. Must begin with "+OK " to be recognized as an encrypted message; otherwise,
    /// the message is returned unchanged.</param>
    /// <returns>The decrypted plaintext string if the input is a recognized encrypted message; otherwise, returns the input
    /// string unchanged.</returns>
    public string Decrypt(string message)
    {
        if (!message.StartsWith("+OK "))
            return message;

        message = message[4..];

        if (message.StartsWith("*"))
        {
            var cipher = Convert.FromBase64String(message[1..]);
            return _enc.GetString(CbcDecrypt(cipher));
        }
        else
        {
            var cipher = FishBase64.Decode(_enc.GetBytes(message));
            return _enc.GetString(EcbDecrypt(cipher));
        }
    }

    private byte[] EcbEncrypt(byte[] plain)
    {
        const int BS = 8;

        var len = ((plain.Length + BS - 1) / BS) * BS;
        Array.Resize(ref plain, len);

        var enc = new byte[len];

        for (var i = 0; i < len; i += BS)
        {
            var block = Blowfish.Pack(plain, i);
            var outBlock = _bf.Encrypt(block);
            Blowfish.Unpack(outBlock, enc, i);
        }

        return enc;
    }

    private byte[] EcbDecrypt(byte[] cipher)
    {
        var plain = new byte[cipher.Length];

        for (var i = 0; i < cipher.Length; i += 8)
        {
            var block = Blowfish.Pack(cipher, i);
            var outBlock = _bf.Decrypt(block);
            Blowfish.Unpack(outBlock, plain, i);
        }

        return TrimZero(plain);
    }

    private static byte[] TrimZero(byte[] data)
    {
        var len = data.Length;
        while (len > 0 && data[len - 1] == 0) len--;
        Array.Resize(ref data, len);
        return data;
    }

    private byte[] CbcEncrypt(byte[] plain)
    {
        const int BS = 8;

        // zero pad
        var len = ((plain.Length + BS - 1) / BS) * BS;
        Array.Resize(ref plain, len);

        var iv = RandomNumberGenerator.GetBytes(BS);
        var outBuf = new byte[BS + len];

        Buffer.BlockCopy(iv, 0, outBuf, 0, BS);

        var prev = iv;

        for (var i = 0; i < len; i += BS)
        {
            var block = new byte[BS];
            for (var j = 0; j < BS; j++)
                block[j] = (byte)(plain[i + j] ^ prev[j]);

            var enc = _bf.Encrypt(Blowfish.Pack(block, 0));
            Blowfish.Unpack(enc, outBuf, BS + i);

            prev = outBuf.Skip(BS + i).Take(BS).ToArray();
        }

        return outBuf;
    }

    private byte[] CbcDecrypt(byte[] cipher)
    {
        const int BS = 8;

        var iv = cipher[..BS];
        var data = cipher[BS..];

        var plain = new byte[data.Length];
        var prev = iv;

        for (var i = 0; i < data.Length; i += BS)
        {
            var dec = _bf.Decrypt(Blowfish.Pack(data, i));
            var block = new byte[BS];
            Blowfish.Unpack(dec, block, 0);

            for (var j = 0; j < BS; j++)
                plain[i + j] = (byte)(block[j] ^ prev[j]);

            prev = data.Skip(i).Take(BS).ToArray();
        }

        return TrimZero(plain);
    }
}