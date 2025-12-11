/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           FtpAuthorization.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-17 05:27:55
 *  Last Modified:  2025-12-11 04:25:07
 *  CRC32:          0x7D155751
 *  
 *  Description:
 *      Per-user, per-command authorization based on FtpUser flags. This is a coarse gate; individual handlers and AMScript m...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







using amFTPd.Config.Ftpd;
using amFTPd.Core.Site;

namespace amFTPd.Security
{
    public static class FtpAuthorization
    {
        /// <summary>
        /// Per-user, per-command authorization based on FtpUser flags.
        /// This is a coarse gate; individual handlers and AMScript may still
        /// apply more specific checks (FXP rules, section rules, etc.).
        /// </summary>
        public static bool IsCommandAllowedForUser(
            FtpUser user,
            string command,
            string? arguments)
            => command.ToUpperInvariant() switch
            {
                // --- Login / negotiation & meta: always allowed once logged in ---
                "USER" => true,
                "PASS" => true,
                "AUTH" => true,
                "PBSZ" => true,
                "PROT" => true,

                "FEAT" => true,
                "SYST" => true,
                "NOOP" => true,
                "OPTS" => true,
                "HELP" => true,
                "STAT" => true,
                "ALLO" => true,
                "MODE" => true,
                "STRU" => true,
                "ABOR" => true,
                "TYPE" => true,
                "QUIT" => true,

                // --- Directory listing & download ---
                // LIST / NLST / MLSD / MLST / RETR => require download permission.
                "LIST" or "NLST" or "MLSD" or "MLST" or "RETR" =>
                    user.AllowDownload,

                // --- Upload / write-ish operations ---
                // STOR / APPE => actual data uploads.
                "STOR" or "APPE" =>
                    user.AllowUpload,

                // DELE / MKD / RMD / RNFR / RNTO => modifications to filesystem.
                "DELE" or "MKD" or "RMD" or "RNFR" or "RNTO" =>
                    user.AllowUpload,

                // --- Navigation ---
                // Changing directories is generally allowed if user can at least read OR write.
                "CWD" or "CDUP" =>
                    user.AllowDownload || user.AllowUpload,

                // --- Data connection setup ---
                // Active mode requires AllowActiveMode; FXP specifics are handled
                // later in the command handlers (using _isFxp + AllowFxp + AMScript).
                "PORT" or "EPRT" =>
                    user.AllowActiveMode,

                // Passive mode is generally allowed; FXP rules again live in handlers/scripts.
                "PASV" or "EPSV" =>
                    true,

                // --- SITE commands ---
                // Coarse gate: SITE is allowed once logged in.
                // Fine-grained gating is done in CanUseSiteCommand per SITE sub-verb.
                "SITE" =>
                    true,

                // Default: allow, and let handler/AMScript decide more fine-grained policy.
                _ => true
            };

        /// <summary>
        /// Returns true if the given command is allowed before the user has logged in.
        /// Non-whitelisted commands should get a "530 Please login with USER and PASS." response.
        /// </summary>
        public static bool IsCommandAllowedUnauthenticated(string command)
            => !CommandRequiresLogin(command);

        /// <summary>
        /// Returns true if the command is meant to require login.
        /// This is the single source of truth for "530 Please login..." gating.
        /// </summary>
        public static bool CommandRequiresLogin(string command)
        {
            var upper = command.ToUpperInvariant();
            return upper switch
            {
                // Auth / TLS negotiation
                "USER" => false,
                "PASS" => false,
                "AUTH" => false,
                "PBSZ" => false,
                "PROT" => false,

                // Informational / harmless
                "FEAT" => false,
                "SYST" => false,
                "NOOP" => false,
                "OPTS" => false,
                "HELP" => false,
                "STAT" => false,

                // Session shutdown
                "QUIT" => false,

                // Everything else requires login
                _ => true
            };
        }

        /// <summary>
        /// Fine-grained permissions for SITE sub-commands.
        /// The coarse "SITE" gate is in IsCommandAllowedForUser; this decides per-verb.
        /// </summary>
        public static bool CanUseSiteCommand(FtpUser user, string siteVerb, SiteCommandContext ctx)
        {
            switch (siteVerb.ToUpperInvariant())
            {
                // Hard admin-only stuff
                case "KICK":
                case "BAN":
                case "UNBAN":
                case "RELOAD":
                case "SHUTDOWN":
                case "SECURITY":
                    return user.IsAdmin;

                // Semi-admin / staff
                case "CHGRP":
                case "CHUSER":
                case "FLAG":
                case "GIVE":
                case "TAKE":
                    return user.IsAdmin || user.IsSiteop; // adjust to your model

                // Read-only / info
                case "WHO":
                case "UPTIME":
                case "HELP":
                case "RULES":
                    return true;

                default:
                    // Unknown SITE verb → deny by default
                    return false;
            }
        }
    }
}
