using amFTPd.Config.Ftpd;

namespace amFTPd.Security
{
    public static class FtpAuthorization
    {
        public static bool IsCommandAllowedForUser(
            FtpUser user,
            string command,
            string? arguments)
            => command.ToUpperInvariant() switch
            {
                // --- Login / negotiation: always allowed pre-login; once logged in it’s fine too ---
                "USER" => true,
                "PASS" => true,
                "AUTH" => true,
                "PBSZ" => true,
                "PROT" => true,
                "FEAT" => true,
                "SYST" => true,
                "QUIT" => true,

                // --- Purely informational / harmless commands, no extra restriction ---
                "NOOP" => true,
                "TYPE" => true,
                "OPTS" => true,

                // --- Download-ish: listing and retrieval ---
                // If you want “list but no data download”, you can special-case LIST/NLST separately.
                "LIST" or "NLST" or "RETR" =>
                    user.AllowDownload,

                // --- Upload-ish ---
                "STOR" or "APPE" =>
                    user.AllowUpload,

                // --- Directory modification ---
                "MKD" or "RMD" or "DELE" or "RNFR" or "RNTO" =>
                    user.AllowUpload, // treat as upload/write permission

                // --- Changing working directory: often allowed if they can at least list/download ---
                "CWD" or "CDUP" =>
                    user.AllowDownload || user.AllowUpload,

                // --- FXP (server-to-server transfers) ---
                // Depending on your implementation, FXP may be triggered by PORT/EPRT or via a dedicated SITE command.
                "PORT" or "EPRT" =>
                    user.AllowActiveMode && user.AllowFxp,

                // --- Active/Passive modes generally ---
                // If you want to restrict active but not passive:
                "PASV" or "EPSV" =>
                    true,

                // --- Admin-only commands ---
                "SITE" =>
                    user.IsAdmin,

                // Default: allow unless you want a strict whitelist
                _ => true
            };
        /// <summary>
        /// Returns true if the given command is allowed before the user has logged in.
        /// All non-whitelisted commands should get a "530 Please login" response.
        /// </summary>
        public static bool IsCommandAllowedUnauthenticated(string command)
            => command.ToUpperInvariant() switch
            {
                // Auth / TLS negotiation
                "USER" => true,
                "PASS" => true,
                "AUTH" => true,
                "PBSZ" => true,
                "PROT" => true,

                // Informational / meta
                "FEAT" => true,
                "SYST" => true,
                "NOOP" => true,
                "OPTS" => true,
                "HELP" => true,
                "STAT" => true,

                // Session shutdown
                "QUIT" => true,

                // Everything else requires login
                _ => false
            };
    }
}
