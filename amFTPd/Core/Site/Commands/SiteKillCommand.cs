using amFTPd.Logging;
using System.Net.Sockets;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteKillCommand : SiteCommandBase
{
    public override string Name => "KILL";

    public override bool RequiresAdmin => true;

    public override string HelpText =>
        "KILL <id> - Kill the current session (id is ignored in this build).";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var sess = context.Session;
        var acc = sess.Account;

        if (acc is null)
        {
            await sess.WriteAsync(FtpResponses.NotLoggedIn, cancellationToken);
            return;
        }

        if (!acc.IsAdmin)
        {
            await sess.WriteAsync("550 SITE KILL requires admin privileges.\r\n", cancellationToken);
            return;
        }

        argument = (argument ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(argument))
        {
            await sess.WriteAsync("501 Usage: SITE KILL <session-id>\r\n", cancellationToken);
            return;
        }

        // Log something server-side if you like
        context.Log.Log(FtpLogLevel.Info,
            $"Admin '{acc.UserName}' issued SITE KILL (current session will be closed).");

        // Tell the client and then close the control socket.
        await sess.WriteAsync("221 Session killed by administrator.\r\n", cancellationToken);

        try
        {
            // This should cause the session read loop to break and clean up.
            sess.Control.Client.Shutdown(SocketShutdown.Both);
        }
        catch
        {
            // ignored
        }

        try
        {
            sess.Control.Client.Close();
        }
        catch
        {
            // ignored
        }
    }
}