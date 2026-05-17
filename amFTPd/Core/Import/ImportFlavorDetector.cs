using System.IO;

namespace amFTPd.Core.Import;

/// <summary>
/// Provides methods for detecting the import flavor based on the structure and directory files present in a
/// specified root path.
/// </summary>
/// <remarks>Use this method before parsing any migration data. It falls back to structural heuristics when config
/// markers are unavailable.</remarks>
public static class ImportFlavorDetector
{
    /// <summary>
    /// Detects the import flavor of an FTP server configuration based on the specified root directory.
    /// </summary>
    /// <param name="rootPath">Root path to inspect for known FTP config/data markers.</param>
    /// <returns>
    /// <see cref="ImportFlavor.GlFtpd"/> when glFTPd structure is detected, <see cref="ImportFlavor.IoFtpd"/> for
    /// ioFTPD structure, or <see cref="ImportFlavor.Unknown"/> when no marker is found.
    /// </returns>
    public static ImportFlavor Detect(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            return ImportFlavor.Unknown;

        // glFTPd markers
        if (File.Exists(Path.Combine(rootPath, "glftpd.conf")) ||
            File.Exists(Path.Combine(rootPath, "glftpd.pre")) ||
            File.Exists(Path.Combine(rootPath, "glftpd.nuke")) ||
            File.Exists(Path.Combine(rootPath, "ftp-data", "misc", "dupefile.txt")) ||
            File.Exists(Path.Combine(rootPath, "ftp-data", "logs", "dupefile")) ||
            File.Exists(Path.Combine(rootPath, "ftp-data", "logs", "dupefile.txt")) ||
            Directory.Exists(Path.Combine(rootPath, "ftp-data")))
        {
            return ImportFlavor.GlFtpd;
        }

        // ioFTPD markers
        if (File.Exists(Path.Combine(rootPath, "ioFTPD.ini")) ||
            File.Exists(Path.Combine(rootPath, "ioFTPD.conf")) ||
            File.Exists(Path.Combine(rootPath, "pre.log")) ||
            File.Exists(Path.Combine(rootPath, "nuke.log")) ||
            File.Exists(Path.Combine(rootPath, "ioDUPE.db")) ||
            File.Exists(Path.Combine(rootPath, "dupelog")) ||
            Directory.Exists(Path.Combine(rootPath, "userfiles")) ||
            Directory.Exists(Path.Combine(rootPath, "groups")))
        {
            return ImportFlavor.IoFtpd;
        }

        return ImportFlavor.Unknown;
    }
}
