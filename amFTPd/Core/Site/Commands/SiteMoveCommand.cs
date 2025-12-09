using amFTPd.Logging;

namespace amFTPd.Core.Site.Commands;

public sealed class SiteMoveCommand : SiteCommandBase
{
    public override string Name => "MOVE";
    public override bool RequiresAdmin => true;
    public override string HelpText => "MOVE";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {

        if (context.Session.Account is not { IsAdmin: true })
        {
            await context.Session.WriteAsync("550 SITE MOVE requires admin privileges.\r\n", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync(
                "501 Usage: SITE MOVE <src> <dst>\r\n",
                cancellationToken);
            return;
        }

        // NOTE: for now, paths cannot contain spaces.
        var parts = argument.Split(' ', 3, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await context.Session.WriteAsync(
                "501 Usage: SITE MOVE <src> <dst>\r\n",
                cancellationToken);
            return;
        }

        var srcArg = parts[0];
        var dstArg = parts[1];

        var srcVirt = FtpPath.Normalize(context.Session.Cwd, srcArg);
        var dstVirt = FtpPath.Normalize(context.Session.Cwd, dstArg);

        string srcPhys;
        string dstPhys;
        try
        {
            srcPhys = context.Router.FileSystem.MapToPhysical(srcVirt);
            dstPhys = context.Router.FileSystem.MapToPhysical(dstVirt);
        }
        catch
        {
            await context.Session.WriteAsync("550 Permission denied.\r\n", cancellationToken);
            return;
        }

        var isDir = Directory.Exists(srcPhys);
        var isFile = File.Exists(srcPhys);

        if (!isDir && !isFile)
        {
            await context.Session.WriteAsync("550 Source file or directory not found.\r\n", cancellationToken);
            return;
        }

        // Basic safety: do not overwrite existing target.
        if (Directory.Exists(dstPhys) || File.Exists(dstPhys))
        {
            await context.Session.WriteAsync("550 Destination already exists.\r\n", cancellationToken);
            return;
        }

        try
        {
            var dstParent = Path.GetDirectoryName(dstPhys);
            if (!string.IsNullOrEmpty(dstParent) && !Directory.Exists(dstParent))
            {
                await context.Session.WriteAsync("550 Destination parent directory does not exist.\r\n", cancellationToken);
                return;
            }

            if (isDir)
            {
                Directory.Move(srcPhys, dstPhys);
            }
            else
            {
                File.Move(srcPhys, dstPhys);
            }

            context.Log.Log(
                FtpLogLevel.Info,
                $"SITE MOVE by {context.Session.Account?.UserName ?? "unknown"}: {srcVirt} -> {dstVirt}");

            await context.Session.WriteAsync(
                $"250 MOVE completed: {srcVirt} -> {dstVirt}\r\n",
                cancellationToken);
        }
        catch (Exception ex)
        {
            context.Log.Log(
                FtpLogLevel.Error,
                $"SITE MOVE failed {srcVirt} -> {dstVirt}: {ex.Message}");

            await context.Session.WriteAsync("550 MOVE failed.\r\n", cancellationToken);
        }

    }
}