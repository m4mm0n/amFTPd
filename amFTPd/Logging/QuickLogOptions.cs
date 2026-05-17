namespace amFTPd.Logging;

/// <summary>
/// Runtime settings for the QuickLog-backed daemon logger.
/// </summary>
public sealed record QuickLogOptions
{
    public static QuickLogOptions Default { get; } = new();

    public string Mode { get; init; } = "something";

    public string TextLogPath { get; init; } = "logs/amftpd.log";

    public string BinaryLogPath { get; init; } = "logs/amftpd.qlbin";

    public bool Console { get; init; } = true;

    public bool Binary { get; init; } = true;

    public int QueueCapacity { get; init; } = 8192;

    public QuickLogMode GetMode() => ParseMode(Mode);

    public static QuickLogMode ParseMode(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "everything" or "all" or "trace" or "debug" => QuickLogMode.Everything,
            "something" or "normal" or "info" or "" => QuickLogMode.Something,
            "quiet" or "little" or "minimal" or "error" or "errors" => QuickLogMode.Quiet,
            _ => throw new ArgumentException(
                "Logging mode must be one of: everything, something, quiet.",
                nameof(value))
        };
    }

    public QuickLogOptions WithMode(QuickLogMode mode) =>
        this with { Mode = mode.ToString().ToLowerInvariant() };
}
