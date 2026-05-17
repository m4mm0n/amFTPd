namespace amFTPd.Config.Ftpd;

/// <summary>
/// Configuration for glFTPd-compatible TCL scripting support.
/// </summary>
public sealed record TclConfig
{
    /// <summary>
    /// Base directory for TCL scripts.
    /// </summary>
    public string ScriptsPath { get; init; } = "scripts";

    /// <summary>
    /// Mapping of custom SITE commands to script files.
    /// Example: "REQUEST" -> "request.tcl"
    /// </summary>
    public Dictionary<string, string> SiteCommands { get; init; } = [];

    /// <summary>
    /// Mapping of FTP events/triggers to script files.
    /// Example: "POST_STOR" -> "zipscript.tcl"
    /// </summary>
    public Dictionary<string, string> Triggers { get; init; } = [];
}
