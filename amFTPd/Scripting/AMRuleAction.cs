namespace amFTPd.Scripting;

/// <summary>
/// Specifies the possible actions that can be taken when evaluating a rule.
/// </summary>
/// <remarks>This enumeration is typically used to define the outcome of a rule evaluation, such as
/// allowing or denying a specific operation.</remarks>
public enum AMRuleAction
{
    None,
    Allow,
    Deny
}