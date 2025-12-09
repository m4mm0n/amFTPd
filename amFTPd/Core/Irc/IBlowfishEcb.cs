/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           IBlowfishEcb.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-03 04:32:48
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xA04726B2
 *  
 *  Description:
 *      Minimal Blowfish ECB interface used by FiSH. Implement this by wrapping your existing BlowfishECB class.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Core.Irc;

/// <summary>
/// Minimal Blowfish ECB interface used by FiSH.
/// Implement this by wrapping your existing BlowfishECB class.
/// </summary>
public interface IBlowfishEcb : IDisposable
{
    int BlockSize { get; } // should be 8

    void EncryptBlock(ReadOnlySpan<byte> input, Span<byte> output);
    void DecryptBlock(ReadOnlySpan<byte> input, Span<byte> output);
}