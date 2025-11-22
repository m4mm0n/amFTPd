/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-22
 *  Last Modified:  2025-11-22
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

namespace amFTPd.Config.Ident;

/// <summary>
/// Specifies the modes of operation for identity verification, allowing combinations of behaviors such as user
/// matching, logging, and group mapping. This enumeration supports bitwise operations due to the <see
/// cref="FlagsAttribute"/>.
/// </summary>
/// <remarks>The <see cref="IdentMode"/> enumeration defines various flags that can be combined to
/// configure identity verification behavior. Each flag represents a specific feature or check, such as enforcing
/// strict user matching, performing reverse DNS checks, or caching results. Use bitwise operations to combine
/// multiple modes as needed.</remarks>
[Flags]
public enum IdentMode
{
    None = 0,
    Standard = 1 << 0, // RFC1413 lookup
    StrictUserMatch = 1 << 1, // FTP USER must equal IDENT username
    LoggingOnly = 1 << 2, // Only log result, no enforcement
    GroupMapping = 1 << 3, // IDENT -> group
    ReverseDnsCheck = 1 << 4, // PTR + IDENT consistency
    TlsBinding = 1 << 5, // IDENT user == TLS cert CN
    Caching = 1 << 6  // cache per IP
}