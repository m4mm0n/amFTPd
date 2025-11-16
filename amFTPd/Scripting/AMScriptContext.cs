namespace amFTPd.Scripting;


public sealed record AMScriptContext(
    bool IsFxp,
    string Section,
    bool FreeLeech,
    string UserName,
    string UserGroup,
    long Bytes,
    long Kb,
    long CostDownload,
    long EarnedUpload,

    // Added for section-routing, SITE scripting, user rules
    string VirtualPath = "",
    string PhysicalPath = ""
);
