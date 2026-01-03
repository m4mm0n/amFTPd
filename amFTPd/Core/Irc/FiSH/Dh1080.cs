using amFTPd.Logging;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace amFTPd.Core.Irc.FiSH;

/// <summary>
/// Provides an implementation of the DH1080 key exchange protocol for securely negotiating a shared secret between two
/// parties using Diffie-Hellman key exchange with a 1080-bit prime.
/// </summary>
/// <remarks>The DH1080 class is typically used to establish a shared secret over an insecure channel, such as in
/// secure messaging or encrypted communication protocols. The class manages the generation of public and private keys,
/// as well as the packing and unpacking of protocol messages required for the key exchange process. Instances of this
/// class are not thread-safe.</remarks>
public class Dh1080
{
    private int state = 0;
    private readonly BigInteger prime = new([
        0xFB, 0xE1, 0x02, 0x2E, 0x23, 0xD2, 0x13, 0xE8,
        0xAC, 0xFA, 0x9A, 0xE8, 0xB9, 0xDF, 0xAD, 0xA3,
        0xEA, 0x6B, 0x7A, 0xC7, 0xA7, 0xB7, 0xE9, 0x5A,
        0xB5, 0xEB, 0x2D, 0xF8, 0x58, 0x92, 0x1F, 0xEA,
        0xDE, 0x95, 0xE6, 0xAC, 0x7B, 0xE7, 0xDE, 0x6A,
        0xDB, 0xAB, 0x8A, 0x78, 0x3E, 0x7A, 0xF7, 0xA7,
        0xFA, 0x6A, 0x2B, 0x7B, 0xEB, 0x1E, 0x72, 0xEA,
        0xE2, 0xB7, 0x2F, 0x9F, 0xA2, 0xBF, 0xB2, 0xA2,
        0xEF, 0xBE, 0xFA, 0xC8, 0x68, 0xBA, 0xDB, 0x3E,
        0x82, 0x8F, 0xA8, 0xBA, 0xDF, 0xAD, 0xA3, 0xE4,
        0xCC, 0x1B, 0xE7, 0xE8, 0xAF, 0xE8, 0x5E, 0x96,
        0x98, 0xA7, 0x83, 0xEB, 0x68, 0xFA, 0x07, 0xA7,
        0x7A, 0xB6, 0xAD, 0x7B, 0xEB, 0x61, 0x8A, 0xCF,
        0x9C, 0xA2, 0x89, 0x7E, 0xB2, 0x8A, 0x61, 0x89,
        0xEF, 0xA0, 0x7A, 0xB9, 0x9A, 0x8A, 0x7F, 0xA9,
        0xAE, 0x29, 0x9E, 0xFA, 0x7B, 0xA6, 0x6D, 0xEA,
        0xFE, 0xFB, 0xEF, 0xBF, 0x0B, 0x7D, 0x8B
    ], isBigEndian: true, isUnsigned: true);
    private readonly BigInteger q;
    private readonly BigInteger g = 2;
    private readonly BigInteger publicKey = 0;
    private readonly BigInteger privateKey = 0;
    private BigInteger secret = 0;
    private readonly IFtpLogger? _log;

    public byte[] SecretBytes
    {
        get
        {
            if (secret <= 0)
                throw new InvalidOperationException("DH1080 secret not established");

            var bytes = secret.ToByteArray(isUnsigned: true, isBigEndian: true);

            // mIRC trims leading zeros
            var i = 0;
            while (i < bytes.Length && bytes[i] == 0)
                i++;

            return bytes[i..];
        }
    }

    /// <summary>
    /// Initializes a new instance of the Dh1080 class and generates a Diffie-Hellman key pair for secure key exchange.
    /// </summary>
    /// <remarks>This constructor generates a private and public key pair using cryptographically secure
    /// random values. The generated keys are suitable for use in DH1080 key exchange protocols. Logging is performed at
    /// the debug level if a logger is provided.</remarks>
    /// <param name="log">An optional logger used to record debug information during initialization. If null, no logging is performed.</param>
    public Dh1080(IFtpLogger? log)
    {
        _log = log;
        q = (prime - 1) / 2;

        using var rng = RandomNumberGenerator.Create();

        // privateKey ∈ [2, q−1]
        var qBytes = q.ToByteArray(isUnsigned: true, isBigEndian: true);

        while (true)
        {
            var buf = new byte[qBytes.Length];
            rng.GetBytes(buf);

            privateKey = new BigInteger(buf, isUnsigned: true, isBigEndian: true);

            if (privateKey <= 1 || privateKey >= q)
                continue;

            publicKey = BigInteger.ModPow(g, privateKey, prime);

            // 🔥 DO NOT validate your own public key
            if (publicKey > 1 && publicKey < prime)
                break;
        }

        _log?.Log(FtpLogLevel.Debug, "[Dh1080] Initialized");
    }

    private bool ValidatePublicKey(BigInteger publicKey, BigInteger q, BigInteger p)
    {
        if (publicKey <= 1 || publicKey >= p - 1)
            return false;

        // Prevent negative exponent crashes
        if (q.Sign < 0)
            return false;

        return BigInteger.ModPow(publicKey, q, p) == BigInteger.One;
    }

    public string Pack()
    {
        string message;
        if (state == 0)
        {
            state = 1;
            message = "DH1080_INIT ";
        }
        else
        {
            message = "DH1080_FINISH ";
        }

        return message + Encode(Int2Bytes(publicKey));
    }

    public string CreateInit() => "DH1080_INIT " + Encode(Int2Bytes(publicKey));

    public string CreateFinish() => "DH1080_FINISH " + Encode(Int2Bytes(publicKey)) + " CBC";

    public void Unpack(string publicB64)
    {
        var publicKey = Bytes2Int(Decode(publicB64));

        if (publicKey <= 1 || publicKey >= prime || !ValidatePublicKey(publicKey, q, prime))
            throw new CryptographicException("Invalid DH1080 public key");

        secret = BigInteger.ModPow(publicKey, privateKey, prime);
    }

    private byte[] Int2Bytes(BigInteger n)
    {
        if (n.Sign < 0)
            throw new ArgumentOutOfRangeException(nameof(n));

        // mIRC / FiSH uses unsigned big-endian
        var bytes = n.ToByteArray(isUnsigned: true, isBigEndian: true);

        // Strip leading zeroes (mIRC behavior)
        var i = 0;
        while (i < bytes.Length && bytes[i] == 0)
            i++;

        return i == 0 ? bytes : bytes[i..];
    }

    private BigInteger Bytes2Int(byte[] b)
    {
        // Force positive BigInteger (mIRC behavior)
        var tmp = new byte[b.Length + 1];
        Buffer.BlockCopy(b, 0, tmp, 1, b.Length);
        return new BigInteger(tmp, isUnsigned: true, isBigEndian: true);
    }

    private string Encode(byte[] data)
    {
        const string b64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        var sb = new StringBuilder();

        var buffer = 0;
        var bits = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;

            while (bits >= 6)
            {
                bits -= 6;
                sb.Append(b64[(buffer >> bits) & 0x3F]);
            }
        }

        if (bits > 0)
            sb.Append(b64[(buffer << (6 - bits)) & 0x3F]);

        return sb.ToString();
    }

    private byte[] Decode(string str)
    {
        const string b64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        var output = new List<byte>();
        var buffer = 0;
        var bits = 0;

        foreach (var c in str)
        {
            var val = b64.IndexOf(c);
            if (val < 0)
                throw new FormatException($"Invalid base64 char '{c}'");

            buffer = (buffer << 6) | val;
            bits += 6;

            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        return output.ToArray();
    }
}