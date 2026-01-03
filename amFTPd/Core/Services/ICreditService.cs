using amFTPd.Config.Ftpd;

namespace amFTPd.Core.Services;

/// <summary>
/// Defines methods for querying and managing user credit balances, enforcing download permissions, and recording upload
/// and download activity.
/// </summary>
/// <remarks>Implementations of this interface are responsible for tracking user credits, determining eligibility
/// for downloads based on credit status, and updating credit balances in response to uploads and downloads. Methods may
/// enforce ratio rules or provide exceptions for certain users, depending on the application's credit policy.</remarks>
public interface ICreditService
{
    // --- queries ---
    long GetCreditsKb(FtpUser user);
    bool IsNoRatio(FtpUser user);

    // --- enforcement ---
    bool CanDownload(
        FtpUser user,
        string sectionName,
        long sizeBytes,
        out string? denialReason);

    // --- mutations ---
    void ApplyUpload(
        FtpUser user,
        string sectionName,
        long sizeBytes,
        string? reason = null);

    void ApplyDownload(
        FtpUser user,
        string sectionName,
        long sizeBytes,
        string? reason = null);
}