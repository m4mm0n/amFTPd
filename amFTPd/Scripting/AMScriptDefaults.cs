namespace amFTPd.Scripting
{
    /// <summary>
    /// Provides default AMScript configuration files and ensures their existence in a specified directory.
    /// </summary>
    /// <remarks>This class contains methods to create default AMScript configuration files if they are
    /// missing in the specified directory. The default configuration files include rules for credits, FXP,  active
    /// mode, sections, site commands, speed limits, user policies, group policies, section-specific  rules, and
    /// messages. These files are initialized with predefined content.</remarks>
    public static class AMScriptDefaults
    {
        public static void EnsureAll(string baseDir)
        {
            Directory.CreateDirectory(baseDir);

            CreateIfMissing(Path.Combine(baseDir, "credits.msl"), DefaultCredits);
            CreateIfMissing(Path.Combine(baseDir, "fxp.msl"), DefaultFxp);
            CreateIfMissing(Path.Combine(baseDir, "active.msl"), DefaultActive);
            CreateIfMissing(Path.Combine(baseDir, "sections.msl"), DefaultSections);
            CreateIfMissing(Path.Combine(baseDir, "site.msl"), DefaultSite);
            CreateIfMissing(Path.Combine(baseDir, "speed.msl"), DefaultSpeed);
            CreateIfMissing(Path.Combine(baseDir, "user.msl"), DefaultUser);
            CreateIfMissing(Path.Combine(baseDir, "group.msl"), DefaultGroup);
            CreateIfMissing(Path.Combine(baseDir, "section-rules.msl"), DefaultSectionRules);
            CreateIfMissing(Path.Combine(baseDir, "messages.msl"), DefaultMessages);
        }

        private static void CreateIfMissing(string path, string content)
        {
            if (File.Exists(path))
                return;

            File.WriteAllText(path, content);
        }

        // ---------------------------------------------------------
        // Defaults
        // ---------------------------------------------------------

        private const string DefaultCredits = """
# Default credits rules.
# Variables:
# $is_fxp, $section, $freeleech, $user.name, $user.group, $bytes, $kb,
# $cost_download, $earned_upload

# Freeleech sections: don't charge download
if ($freeleech) cost_download = 0;

# FXP uses normal credits by default (no change)
# if ($is_fxp) cost_download = $cost_download;
""";

        private const string DefaultFxp = """
# FXP rules.
# $is_fxp, $user.group, $section

# Example: deny FXP for ANON group
# if ($is_fxp && $user.group == "ANON") return deny;
""";

        private const string DefaultActive = """
# Active mode rules.
# $user.group, $is_fxp

# Example: disallow active mode for anon users
# if ($user.group == "ANON") return deny;
""";

        private const string DefaultSections = """
# Section override rules, v1 placeholder.
""";

        private const string DefaultSite = """
# SITE command rules, v1 placeholder.
""";

        private const string DefaultSpeed = """
# Speed limit rules, v1 placeholder.
""";

        private const string DefaultUser = """
# User-based policy rules, v1 placeholder.
""";

        private const string DefaultGroup = """
# Group-based policy rules, v1 placeholder.
""";

        private const string DefaultSectionRules = """
# Section-specific policy rules, v1 placeholder.
""";

        private const string DefaultMessages = """
# Messages / welcome banners.
# Example:
# if ($user.group == "ADMIN") log "Admin logged in: $user.name";
""";
    }
}
