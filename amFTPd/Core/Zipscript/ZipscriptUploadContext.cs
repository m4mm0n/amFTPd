/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ZipscriptUploadContext.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-14 09:03:41
 *  Last Modified:  2025-12-14 09:29:16
 *  CRC32:          0xBBAB6124
 *  
 *  Description:
 *      Context passed when a file upload has completed.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */
namespace amFTPd.Core.Zipscript;

/// <summary>
/// Context passed when a file upload has completed.
/// </summary>
public sealed record ZipscriptUploadContext(
    string SectionName,
    string VirtualFilePath,
    string PhysicalFilePath,
    long SizeBytes,
    string? UserName,
    DateTimeOffset CompletedAt);