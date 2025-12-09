namespace amFTPd.Core.Site.Commands
{
    public sealed class SiteMkdirCommand : SiteCommandBase
    {
        public override string Name => "MKDIR";
        public override string HelpText => "MKDIR <virt-path>  - create directory (same as MKD)";

        public override async Task ExecuteAsync(
            SiteCommandContext context,
            string argument,
            CancellationToken cancellationToken)
        {
            var s = context.Session;

            if (string.IsNullOrWhiteSpace(argument))
            {
                await s.WriteAsync("501 Syntax: SITE MKDIR <path>\r\n", cancellationToken);
                return;
            }

            // Reuse the same virtual-path normalization as MKD/STOR/DELE etc.
            var virt = FtpPath.Normalize(s.Cwd, argument);

            string phys;
            try
            {
                phys = context.Router.FileSystem.MapToPhysical(virt);
            }
            catch
            {
                await s.WriteAsync("550 Permission denied.\r\n", cancellationToken);
                return;
            }

            try
            {
                Directory.CreateDirectory(phys);
            }
            catch
            {
                await s.WriteAsync("550 Failed to create directory.\r\n", cancellationToken);
                return;
            }

            await s.WriteAsync(FtpResponses.PathCreated, cancellationToken);
        }
    }
}