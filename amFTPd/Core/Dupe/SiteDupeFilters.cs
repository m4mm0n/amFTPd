namespace amFTPd.Core.Dupe;

/// <summary>
/// Represents a set of filter criteria for matching scene entries based on section, group, release name, nuked status,
/// age, size, file count, archive count, and required file types.
/// </summary>
/// <remarks>Use this class to define and apply complex filtering logic when searching or processing scene
/// entries. Each property corresponds to a specific filter condition; properties left unset are not used in filtering.
/// The class supports parsing filter definitions from command-line style strings and evaluating whether a given scene
/// entry matches all specified criteria. This type is not thread-safe.</remarks>
internal sealed class SiteDupeFilters
{
    public string? Section;
    public string? Group;
    public string? Release;

    public bool? Nuked;

    public int? NewerDays;
    public int? OlderDays;

    public long? MinSize;
    public long? MaxSize;

    public int? MinFiles;
    public int? MaxFiles;

    public int? MinArchives;
    public int? MaxArchives;

    public bool RequireSfv;
    public bool RequireNfo;
    public bool RequireDiz;

    /// <summary>
    /// Parses a command-line style filter string and returns a corresponding SiteDupeFilters instance.
    /// </summary>
    /// <remarks>Filter options are case-insensitive and must be separated by spaces. Unknown or malformed
    /// options are ignored. Numeric values must be valid integers, and size values must be in a supported format.
    /// Boolean flags such as "-nuked" and "-ok" are mutually exclusive; the last one specified takes
    /// precedence.</remarks>
    /// <param name="arg">A single string containing filter options separated by spaces. Each option should use the expected prefix
    /// format, such as "-section=", "-group=", "-release=", "-nuked", "-ok", "-newer=", "-older=", "-size>", "-size<",
    /// "-files>", "-files<", "-archives>", "-archives<", "-has-sfv", "-has-nfo", or "-has-diz".</param>
    /// <returns>A SiteDupeFilters object populated with values parsed from the specified filter string. Properties not specified
    /// in the input string will retain their default values.</returns>
    public static SiteDupeFilters Parse(string arg)
    {
        var f = new SiteDupeFilters();

        var tokens = arg.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var t in tokens)
        {
            if (t.StartsWith("-section=", StringComparison.OrdinalIgnoreCase))
                f.Section = t[9..];
            else if (t.StartsWith("-group=", StringComparison.OrdinalIgnoreCase))
                f.Group = t[7..];
            else if (t.StartsWith("-release=", StringComparison.OrdinalIgnoreCase))
                f.Release = t[9..];

            else if (t.Equals("-nuked", StringComparison.OrdinalIgnoreCase))
                f.Nuked = true;
            else if (t.Equals("-ok", StringComparison.OrdinalIgnoreCase))
                f.Nuked = false;

            else if (t.StartsWith("-newer=", StringComparison.OrdinalIgnoreCase))
                f.NewerDays = int.Parse(t[7..]);
            else if (t.StartsWith("-older=", StringComparison.OrdinalIgnoreCase))
                f.OlderDays = int.Parse(t[7..]);

            else if (t.StartsWith("-size>", StringComparison.OrdinalIgnoreCase))
                f.MinSize = ParseSize(t[6..]);
            else if (t.StartsWith("-size<", StringComparison.OrdinalIgnoreCase))
                f.MaxSize = ParseSize(t[6..]);

            else if (t.StartsWith("-files>", StringComparison.OrdinalIgnoreCase))
                f.MinFiles = int.Parse(t[7..]);
            else if (t.StartsWith("-files<", StringComparison.OrdinalIgnoreCase))
                f.MaxFiles = int.Parse(t[7..]);

            else if (t.StartsWith("-archives>", StringComparison.OrdinalIgnoreCase))
                f.MinArchives = int.Parse(t[10..]);
            else if (t.StartsWith("-archives<", StringComparison.OrdinalIgnoreCase))
                f.MaxArchives = int.Parse(t[10..]);

            else if (t.Equals("-has-sfv", StringComparison.OrdinalIgnoreCase))
                f.RequireSfv = true;
            else if (t.Equals("-has-nfo", StringComparison.OrdinalIgnoreCase))
                f.RequireNfo = true;
            else if (t.Equals("-has-diz", StringComparison.OrdinalIgnoreCase))
                f.RequireDiz = true;
        }

        return f;
    }

    /// <summary>
    /// Determines whether the specified scene entry matches all of the current filter criteria.
    /// </summary>
    /// <remarks>This method compares the provided scene entry to the filter's configured properties, such as
    /// section, group, release name, nuked status, age, size, file count, archive count, and required file types. Only
    /// entries that meet every specified criterion will return true.</remarks>
    /// <param name="d">The scene entry to evaluate against the filter conditions. Cannot be null.</param>
    /// <returns>true if the specified scene entry satisfies all filter criteria; otherwise, false.</returns>
    public bool Match(SceneDupeEntry d)
    {
        var now = DateTimeOffset.UtcNow;

        if (Section != null &&
            !d.Section.Equals(Section, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Group != null &&
            !d.Group.Equals(Group, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Release != null &&
            !d.ReleaseName.Equals(Release, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Nuked.HasValue && d.IsNuked != Nuked.Value)
            return false;

        if (NewerDays.HasValue &&
            (now - d.ReleaseDate).TotalDays > NewerDays.Value)
            return false;

        if (OlderDays.HasValue &&
            (now - d.ReleaseDate).TotalDays < OlderDays.Value)
            return false;

        if (MinSize.HasValue && d.TotalBytes < MinSize.Value)
            return false;
        if (MaxSize.HasValue && d.TotalBytes > MaxSize.Value)
            return false;

        if (MinFiles.HasValue && d.FileCount < MinFiles.Value)
            return false;
        if (MaxFiles.HasValue && d.FileCount > MaxFiles.Value)
            return false;

        if (MinArchives.HasValue && d.ArchiveCount < MinArchives.Value)
            return false;
        if (MaxArchives.HasValue && d.ArchiveCount > MaxArchives.Value)
            return false;

        if (RequireSfv && !d.HasSfv)
            return false;
        if (RequireNfo && !d.HasNfo)
            return false;
        if (RequireDiz && !d.HasDiz)
            return false;

        return true;
    }

    static long ParseSize(string text)
    {
        text = text.Trim().ToUpperInvariant();

        long mul = 1;
        if (text.EndsWith("KB")) { mul = 1024; text = text[..^2]; }
        else if (text.EndsWith("MB")) { mul = 1024 * 1024; text = text[..^2]; }
        else if (text.EndsWith("GB")) { mul = 1024L * 1024 * 1024; text = text[..^2]; }

        return long.Parse(text) * mul;
    }
}