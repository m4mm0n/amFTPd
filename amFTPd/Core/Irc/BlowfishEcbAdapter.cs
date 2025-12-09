/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03
 *  Last Modified:  2025-12-03
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

using amFTPd.Security;

namespace amFTPd.Core.Irc
{
    /// <summary>
    /// Provides an adapter for the Blowfish encryption algorithm in Electronic Codebook (ECB) mode.
    /// </summary>
    /// <remarks>This class wraps the functionality of the BlowfishECB implementation, exposing a simplified
    /// interface for encrypting and decrypting data in fixed-size blocks. The Blowfish algorithm operates on 8-byte
    /// blocks and requires a key to initialize the encryption context.</remarks>
    public sealed class BlowfishEcbAdapter : IBlowfishEcb
    {
        private readonly BlowfishECB _inner;

        public BlowfishEcbAdapter(string key)
        {
            _inner = new BlowfishECB(key); // whatever your ctor looks like
        }

        public int BlockSize => 8;

        public void EncryptBlock(ReadOnlySpan<byte> input, Span<byte> output)
            => _inner.EncryptBlock(input, output);

        public void DecryptBlock(ReadOnlySpan<byte> input, Span<byte> output)
            => _inner.DecryptBlock(input, output);

        public void Dispose() => _inner.Dispose();
    }
}
