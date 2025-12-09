using amFTPd.Config.Ftpd;

namespace amFTPd.Core
{
    /// <summary>
    /// Context passed to ratio/login rule resolution when a user logs in.
    /// Extend this as needed with more info (groups, flags, etc.).
    /// </summary>
    public sealed record RatioLoginContext
    {
        /// <summary>
        /// Username the client is logging in as.
        /// </summary>
        public string UserName { get; init; } = string.Empty;
        public string GroupName { get; init; } = string.Empty;
        public string RealName { get; init; } = string.Empty;
        public DateTime NowUtc { get; init; }
        public string RemoteHost { get; init; } = string.Empty;
        /// <summary>
        /// Remote IP/host as a string (if you want IP-based rules).
        /// </summary>
        public string RemoteAddress { get; init; } = string.Empty;

        /// <summary>
        /// True if this is an anonymous/guest login.
        /// </summary>
        public bool IsAnonymous { get; init; }

        /// <summary>
        /// The resolved FTP user object, if the login maps to a configured user.
        /// </summary>
        public FtpUser? User { get; init; }
    }
}
