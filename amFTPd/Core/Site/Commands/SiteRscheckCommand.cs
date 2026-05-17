/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           SiteRscheckCommand.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 09:00:58
 *  Last Modified:  2025-12-14 21:36:47
 *  CRC32:          0x43E030CF
 *  
 *  Description:
 *      SITE RSCHECK reports release rescan/SFV health.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

namespace amFTPd.Core.Site.Commands;

/// <summary>
/// Implements <c>SITE RSCHECK</c>, a scene-style release health check backed by the zipscript state database.
/// </summary>
public sealed class SiteRscheckCommand : SiteCommandBase
{
    public override string Name => "RSCHECK";
    public override bool RequiresAdmin => false;
    public override bool RequiresSiteop => true;
    public override string HelpText => "RSCHECK <path> - check rescan status (zipscript integration)";

    public override async Task ExecuteAsync(
        SiteCommandContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var engine = context.Runtime.Zipscript;
        if (engine is null)
        {
            await context.Session.WriteAsync("550 Zipscript is not enabled.\r\n", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            await context.Session.WriteAsync("501 Usage: SITE RSCHECK <path>\r\n", cancellationToken);
            return;
        }

        var path = argument.Trim();
        var status = engine.GetStatus(path);
        if (status is null)
        {
            await context.Session.WriteAsync("211 No zipscript status for this path.\r\n", cancellationToken);
            return;
        }

        var total = status.Files.Count;
        var ok = status.Files.Count(file => file.State == Zipscript.ZipscriptFileState.Ok);
        var bad = status.Files.Count(file => file.State == Zipscript.ZipscriptFileState.BadCrc);
        var missing = status.Files.Count(file => file.State == Zipscript.ZipscriptFileState.Missing);
        var pending = total - ok - bad - missing;

        var verdict = status.IsComplete && bad == 0 && missing == 0
            ? "OK"
            : bad > 0
                ? "BAD"
                : missing > 0
                    ? "MISSING"
                    : "INCOMPLETE";

        var response =
            $"211- RSCHECK {status.ReleasePath}\r\n" +
            $"211- Section: {status.SectionName}\r\n" +
            $"211- Verdict: {verdict}\r\n" +
            $"211- Has SFV: {(status.HasSfv ? "YES" : "NO")}\r\n" +
            $"211- Complete: {(status.IsComplete ? "YES" : "NO")}\r\n" +
            $"211- Files: total={total} ok={ok} bad={bad} missing={missing} pending={pending}\r\n" +
            "211 End of RSCHECK.\r\n";

        await context.Session.WriteAsync(response, cancellationToken);
    }
}
