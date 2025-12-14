/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FxpTlsState.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-13 21:10:50
 *  Last Modified:  2025-12-13 21:10:50
 *  CRC32:          0x1CACC8C1
 *  
 *  Description:
 *      TODO: Describe this file.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
using amFTPd.Config.Fxp;

namespace amFTPd.Core.Fxp;

/// <summary>Logical TLS state used by FXP policy.</summary>
public sealed record FxpTlsState(
    bool Active,
    TlsVersion Protocol,
    string? CipherSuite);