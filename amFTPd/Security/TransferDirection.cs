/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           TransferDirection.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-01 05:20:10
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xCDB1E92F
 *  
 *  Description:
 *      Specifies the direction of a data transfer operation.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





namespace amFTPd.Security;

/// <summary>
/// Specifies the direction of a data transfer operation.
/// </summary>
/// <remarks>This enumeration is used to indicate the type of transfer being performed, such as
/// downloading, uploading,  or transferring data between two remote locations (FXP). The <see cref="None"/> value
/// represents the absence  of a transfer direction.</remarks>
public enum TransferDirection
{
    None = 0,
    Download,
    Upload,
    FxpSource,
    FxpTarget
}