using amFTPd.Logging;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteLogCommand : SiteCommandBase
{
    public override string Name => "LOG";

    public override bool RequiresAdmin => true;

    public override string HelpText => "LOG <STATUS|EVERYTHING|SOMETHING|QUIET> - inspect or change QuickLog verbosity";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var session = context.Session;
        var account = session.Account;
        if (account is not { IsAdmin: true })
        {
            await session.WriteAsync("550 SITE LOG requires admin privileges.\r\n", cancellationToken);
            return;
        }

        if (context.Log is not IQuickLogModeController controller)
        {
            await session.WriteAsync("550 QuickLog mode controller is unavailable.\r\n", cancellationToken);
            return;
        }

        var verb = (argument ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(verb) ||
            verb.Equals("STATUS", StringComparison.OrdinalIgnoreCase))
        {
            await WriteModeAsync(session, controller.Mode, cancellationToken);
            return;
        }

        QuickLogMode newMode;
        try
        {
            newMode = QuickLogOptions.ParseMode(verb);
        }
        catch (ArgumentException)
        {
            await session.WriteAsync(
                "501 Syntax: SITE LOG <STATUS|EVERYTHING|SOMETHING|QUIET>\r\n",
                cancellationToken);
            return;
        }

        var oldMode = controller.Mode;
        controller.SetMode(newMode);

        context.Log.Log(
            FtpLogLevel.Warn,
            $"SITE LOG mode changed by {account.UserName}: {oldMode} -> {newMode}");

        context.Runtime.AuditLog?.Log(
            actor: account.UserName,
            action: "LOG",
            detail: $"mode {oldMode} -> {newMode}",
            ip: session.RemoteEndPoint?.Address.ToString());

        await WriteModeAsync(session, newMode, cancellationToken);
    }

    private static Task WriteModeAsync(
        FtpSession session,
        QuickLogMode mode,
        CancellationToken cancellationToken)
    {
        var detail = mode switch
        {
            QuickLogMode.Everything => "logging trace/debug/info/warn/error/critical",
            QuickLogMode.Something => "logging info/warn/error/critical",
            QuickLogMode.Quiet => "logging only error/critical plus lifecycle/crash records",
            _ => "logging info/warn/error/critical"
        };

        return session.WriteAsync(
            $"200 LOG MODE {mode.ToString().ToUpperInvariant()} - {detail}\r\n",
            cancellationToken);
    }
}
