using amFTPd.Config.Ftpd;
using amFTPd.Credits;

namespace amFTPd.Core.Services;

/// <summary>
/// Provides credit and ratio management services for users, including credit balance queries, download eligibility
/// checks, and credit adjustments based on upload and download activity.
/// </summary>
/// <remarks>This service coordinates user and group information with credit and ratio engines to enforce download
/// restrictions and update user credits. It is intended to be used as the primary interface for managing user credits
/// and enforcing ratio-based access policies. This class is thread-safe for concurrent use.</remarks>
public sealed class CreditService : ICreditService
{
    private readonly IUserStore _users;
    private readonly CreditEngine? _engine;

    public CreditService(
        IUserStore users,
        CreditEngine? engine)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        // NOTE: JSON / config-only mode may not have a CreditEngine yet.
        // In that case we operate in "no-ratio" mode (always allow, no adjustments).
        _engine = engine;
    }

    public long GetCreditsKb(FtpUser user)
        => user.CreditsKb;

    public bool IsNoRatio(FtpUser user)
        => user.IsNoRatio;

    public bool CanDownload(
        FtpUser user,
        string sectionName,
        long sizeBytes,
        out string? denialReason)
    {
        denialReason = null;

        // No engine wired => treat as no-ratio.
        if (_engine is null)
            return true;

        if (user.IsNoRatio)
            return true;

        var remaining = _engine.ComputeDownloadCostKb(
            user,
            sectionName,
            sizeBytes);

        if (remaining < 0)
        {
            denialReason = "Insufficient credits.";
            return false;
        }

        return true;
    }

    public void ApplyDownload(
        FtpUser user,
        string sectionName,
        long sizeBytes,
        string? reason = null)
    {
        if (_engine is null)
            return;

        if (user.IsNoRatio)
            return;

        if (!_engine.TryConsumeCredits(
                user,
                sectionName,
                sizeBytes,
                out var newCredits))
            return;

        // Persist via store
        user = user with { CreditsKb = newCredits };
        _users.TryUpdateUser(user, out _);
    }

    public void ApplyUpload(
        FtpUser user,
        string sectionName,
        long sizeBytes,
        string? reason = null)
    {
        if (_engine is null)
            return;

        var newCredits = _engine.AwardCredits(
            user,
            sectionName,
            sizeBytes);

        user = user with { CreditsKb = newCredits };
        _users.TryUpdateUser(user, out _);
    }
}