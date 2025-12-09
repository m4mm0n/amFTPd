/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AMScriptContext.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-16 06:46:15
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xF95BBF3A
 *  
 *  Description:
 *      Represents the context for an AMScript operation, providing details about the user, section, and file transfer parame...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Scripting;

/// <summary>
/// Represents the context for an AMScript operation, providing details about the user, section, and file transfer
/// parameters.
/// </summary>
/// <param name="IsFxp">Indicates whether the operation is an FXP (File eXchange Protocol) transfer. <see langword="true"/> if FXP;
/// otherwise, <see langword="false"/>.</param>
/// <param name="Section">The name of the section associated with the operation.</param>
/// <param name="FreeLeech">Indicates whether the operation is marked as free leech. <see langword="true"/> if free leech; otherwise, <see
/// langword="false"/>.</param>
/// <param name="UserName">The name of the user performing the operation.</param>
/// <param name="UserGroup">The group to which the user belongs.</param>
/// <param name="Bytes">The total size of the file(s) involved in the operation, in bytes.</param>
/// <param name="Kb">The total size of the file(s) involved in the operation, in kilobytes.</param>
/// <param name="CostDownload">The cost, in bytes, associated with downloading the file(s).</param>
/// <param name="EarnedUpload">The amount, in bytes, earned as upload credit from the operation.</param>
/// <param name="VirtualPath">The virtual path associated with the operation. Defaults to an empty string if not specified.</param>
/// <param name="PhysicalPath">The physical path associated with the operation. Defaults to an empty string if not specified.</param>
public sealed record AMScriptContext(
    bool IsFxp,
    string Section,
    bool FreeLeech,
    string UserName,
    string UserGroup,
    long Bytes,
    long Kb,
    long CostDownload,
    long EarnedUpload,

    // Added for section-routing, SITE scripting, user rules
    string VirtualPath = "",
    string PhysicalPath = "",
    string Event = ""
);
