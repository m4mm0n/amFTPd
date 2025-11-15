namespace amFTPd.Db;

/// <summary>
/// Defines a contract for managing and retrieving FTP configuration sections.
/// </summary>
/// <remarks>This interface provides methods to find, retrieve, add, update, and delete FTP sections.
/// Implementations of this interface are expected to handle the storage and management of <see cref="FtpSection"/>
/// objects, ensuring thread safety and data consistency where applicable.</remarks>
public interface ISectionStore
{
    FtpSection? FindSection(string sectionName);
    IEnumerable<FtpSection> GetAllSections();

    bool TryAddSection(FtpSection section, out string? error);
    bool TryUpdateSection(FtpSection section, out string? error);
    bool TryDeleteSection(string sectionName, out string? error);
}