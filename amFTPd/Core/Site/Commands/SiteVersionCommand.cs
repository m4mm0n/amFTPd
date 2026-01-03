using System.Reflection;

namespace amFTPd.Core.Site.Commands;

/// <summary>
/// Shows daemon version/build information.
/// </summary>
public sealed class SiteVersionCommand : SiteCommandBase
{
    public override string Name => "VERSION";

    // Safe to expose to any logged-in user; does not leak secrets.
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => false;

    public override string HelpText => "VERSION - show amFTPd version/build info";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        // Prefer the main assembly version (amFTPd).
        var asm = typeof(FtpServer).Assembly;
        var ver = asm.GetName().Version?.ToString() ?? "unknown";

        // Optional informational version if present (e.g., from GitVersion or CI).
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
            info = info.Trim();

        var line = info is { Length: > 0 } && !string.Equals(info, ver, StringComparison.OrdinalIgnoreCase)
            ? $"200 amFTPd v{ver} ({info})\r\n"
            : $"200 amFTPd v{ver}\r\n";

        await context.Session.WriteAsync(line, cancellationToken);
    }
}

