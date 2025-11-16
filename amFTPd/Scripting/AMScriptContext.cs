namespace amFTPd.Scripting;

/// <summary>
/// Represents the context for an AM script, providing details about the user's session and download activity.
/// </summary>
/// <param name="IsFxp">Indicates whether the operation is an FXP (File Exchange Protocol) transfer. <see langword="true"/> if FXP;
/// otherwise, <see langword="false"/>.</param>
/// <param name="Section">The section or category associated with the operation.</param>
/// <param name="FreeLeech">Indicates whether the operation is free of download cost. <see langword="true"/> if free leech; otherwise, <see
/// langword="false"/>.</param>
/// <param name="UserName">The name of the user associated with the operation.</param>
/// <param name="UserGroup">The group or role of the user associated with the operation.</param>
/// <param name="Bytes">The total number of bytes involved in the operation.</param>
/// <param name="Kb">The total size of the operation in kilobytes.</param>
/// <param name="CostDownload">The cost of the download operation, typically measured in credits or points.</param>
/// <param name="EarnedUpload">The amount of upload credit earned as a result of the operation.</param>
public sealed record AMScriptContext(
    bool IsFxp,
    string Section,
    bool FreeLeech,
    string UserName,
    string UserGroup,
    long Bytes,
    long Kb,
    long CostDownload,
    long EarnedUpload
);