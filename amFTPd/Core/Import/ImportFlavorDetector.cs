namespace amFTPd.Core.Import;

/// <summary>
/// Provides methods for detecting the import flavor based on the structure and configuration files present in a
/// specified directory.
/// </summary>
/// <remarks>This class is intended to assist in identifying the type of FTP server or environment by examining
/// known configuration files or directory structures. It is useful when importing data from different FTP server
/// platforms.</remarks>
public static class ImportFlavorDetector
{
    /// <summary>
    /// Detects the import flavor of an FTP server configuration based on the specified root directory.
    /// </summary>
    /// <param name="rootPath">The full path to the root directory to inspect for known FTP server configuration files. Cannot be null or
    /// empty.</param>
    /// <returns>An ImportFlavor value indicating the detected FTP server type. Returns ImportFlavor.GlFtpd if a glFTPd
    /// configuration is found, ImportFlavor.IoFtpd if an ioFTPD configuration is found, or ImportFlavor.Unknown if no
    /// known configuration is detected.</returns>
    public static ImportFlavor Detect(string rootPath)
    {
        if (File.Exists(Path.Combine(rootPath, "glftpd.conf")) ||
            Directory.Exists(Path.Combine(rootPath, "ftp-data")))
            return ImportFlavor.GlFtpd;

        if (File.Exists(Path.Combine(rootPath, "ioFTPD.ini")) ||
            File.Exists(Path.Combine(rootPath, "ioFTPD.conf")))
            return ImportFlavor.IoFtpd;

        return ImportFlavor.Unknown;
    }
}
