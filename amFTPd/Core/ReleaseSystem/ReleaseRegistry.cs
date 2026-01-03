using System.Collections.Concurrent;

namespace amFTPd.Core.ReleaseSystem;

/// <summary>
/// Provides a thread-safe registry for managing and retrieving release contexts by section and release name.
/// </summary>
/// <remarks>This class is designed for concurrent access and can be safely used from multiple threads. Release
/// contexts are identified by a combination of section and release name, using case-insensitive comparison. The
/// registry ensures that each unique (section, release) pair corresponds to a single ReleaseContext instance.</remarks>
public sealed class ReleaseRegistry
{
    private readonly ConcurrentDictionary<string, ReleaseContext> _releases
        = new(StringComparer.OrdinalIgnoreCase);

    private static string MakeKey(string section, string release)
        => $"{section}|{release}";

    public IEnumerable<ReleaseContext> All => _releases.Values;

    public IEnumerable<string> Sections
        => _releases.Values
            .Select(r => r.Section)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    public ReleaseContext GetOrCreate(
        string section,
        string release)
    {
        var key = MakeKey(section, release);

        return _releases.GetOrAdd(
            key,
            _ => new ReleaseContext
            {
                Section = section,
                ReleaseName = release
                // timestamps come from zipscript
            });
    }

    public bool TryGet(
        string section,
        string release,
        out ReleaseContext ctx)
        => _releases.TryGetValue(
            MakeKey(section, release),
            out ctx!);

    public IEnumerable<ReleaseContext> EnumerateBySection(string section)
        => _releases.Values
            .Where(r =>
                r.Section.Equals(
                    section,
                    StringComparison.OrdinalIgnoreCase));
}