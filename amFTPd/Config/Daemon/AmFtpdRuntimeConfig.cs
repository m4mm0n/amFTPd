using amFTPd.Config.Ftpd;
using amFTPd.Security;

namespace amFTPd.Config.Daemon
{
    public sealed class AmFtpdRuntimeConfig
    {
        public required FtpConfig FtpConfig { get; init; }
        public required IUserStore UserStore { get; init; }
        public required SectionManager Sections { get; init; }
        public required TlsConfig TlsConfig { get; init; }
    }
}
