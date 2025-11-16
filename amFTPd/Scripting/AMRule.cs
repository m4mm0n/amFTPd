namespace amFTPd.Scripting;

/// <summary>
/// Represents a rule consisting of a condition and an associated action.
/// </summary>
/// <remarks>This class is immutable. Once an instance is created, the condition and action cannot be
/// modified.</remarks>
internal sealed class AMRule
{
    public string Condition { get; }
    public string Action { get; }

    public AMRule(string condition, string action)
    {
        Condition = condition;
        Action = action;
    }
}