namespace amFTPd.Core.Import.Records;

/// <summary>
/// Represents a record containing a target identifier, a user flag, and an associated ratio value.
/// </summary>
/// <param name="Target">The identifier of the target entity to which the ratio applies. Cannot be null.</param>
/// <param name="IsUser">A value indicating whether the target is a user. Set to <see langword="true"/> if the target is a user; otherwise,
/// <see langword="false"/>.</param>
/// <param name="Ratio">The ratio value associated with the target. Typically a value between 0.0 and 1.0.</param>
public sealed record ImportedRatioRecord(
    string Target,
    bool IsUser,
    double Ratio);