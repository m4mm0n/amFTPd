namespace amFTPd.Logging;

/// <summary>
/// Runtime control surface for QuickLog verbosity.
/// </summary>
public interface IQuickLogModeController
{
    QuickLogMode Mode { get; }

    void SetMode(QuickLogMode mode);
}
