/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FishCodec.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 04:32:48
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xBE68936F
 *  
 *  Description:
 *      Implements FiSH-style encryption for IRC messages: ircmsg = "+OK " + blowcryptBase64( Blowfish-ECB(key, utf8(plaintex...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





using System.Text;

namespace amFTPd.Core.Irc;

/// <summary>
/// Implements FiSH-style encryption for IRC messages:
/// ircmsg = "+OK " + blowcryptBase64( Blowfish-ECB(key, utf8(plaintext)) )
/// Keys are provided per target (channel or nick).
/// </summary>
public sealed class FishCodec
{
    private readonly IDictionary<string, string> _keys;
    private readonly Func<string, IBlowfishEcb> _cipherFactory;

    // Blowcrypt base64 alphabets (custom <-> standard)
    // custom: "./0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"
    // standard: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
    private const string CustomAlphabet = "./0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string StandardAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    public FishCodec(
        IDictionary<string, string> keys,
        Func<string, IBlowfishEcb> cipherFactory)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _cipherFactory = cipherFactory ?? throw new ArgumentNullException(nameof(cipherFactory));
    }

    public bool HasKeyForTarget(string target)
        => _keys.ContainsKey(target);

    /// <summary>
    /// Encrypt a plaintext message for a given target.
    /// Returns "+OK ..." formatted FiSH text.
    /// </summary>
    public string EncryptMessage(string target, string plaintext)
    {
        if (!_keys.TryGetValue(target, out var key))
            throw new InvalidOperationException($"No FiSH key defined for target '{target}'.");

        if (string.IsNullOrEmpty(plaintext))
            return "+OK ";

        using var cipher = _cipherFactory(key);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var blockSize = cipher.BlockSize;

        // pad to block size with zero bytes (FiSH-style, simple zero padding)
        var paddedLen = ((plainBytes.Length + blockSize - 1) / blockSize) * blockSize;
        var padded = new byte[paddedLen];
        Buffer.BlockCopy(plainBytes, 0, padded, 0, plainBytes.Length);

        var enc = new byte[paddedLen];
        var tmpIn = new byte[blockSize];
        var tmpOut = new byte[blockSize];

        for (var offset = 0; offset < paddedLen; offset += blockSize)
        {
            Buffer.BlockCopy(padded, offset, tmpIn, 0, blockSize);
            cipher.EncryptBlock(tmpIn, tmpOut);
            Buffer.BlockCopy(tmpOut, 0, enc, offset, blockSize);
        }

        var blowcrypt = EncodeBlowcrypt(enc);
        return "+OK " + blowcrypt;
    }

    private static string EncodeBlowcrypt(byte[] ciphertext)
    {
        // Use standard base64 then remap to custom alphabet and strip '=' padding
        var base64 = Convert.ToBase64String(ciphertext);
        var sb = new StringBuilder(base64.Length);

        foreach (var ch in base64)
        {
            if (ch == '=') break; // blowcrypt omits padding

            var idx = StandardAlphabet.IndexOf(ch);
            if (idx < 0)
                throw new FormatException($"Unexpected base64 char '{ch}' in EncodeBlowcrypt.");

            sb.Append(CustomAlphabet[idx]);
        }

        return sb.ToString();
    }

    // If you later want decryption, you can add DecodeBlowcrypt + DecryptMessage here.
}