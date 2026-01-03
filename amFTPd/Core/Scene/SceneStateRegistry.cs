using System.Collections.Concurrent;

namespace amFTPd.Core.Scene;

/// <summary>
/// Provides a thread-safe registry for tracking the state of scene releases, including their pre, nuke, and unnuke
/// status.
/// </summary>
/// <remarks>This class is designed for concurrent use and allows callers to mark scene releases as pre, nuke them
/// with a reason, or remove a nuke status. State lookups are case-insensitive with respect to the release path. All
/// operations are atomic and safe for use from multiple threads.</remarks>
public sealed class SceneStateRegistry
{
    private readonly ConcurrentDictionary<string, SceneReleaseState> _states
        = new(StringComparer.OrdinalIgnoreCase);

    public void MarkPre(string section, string path)
    {
        _states[path] = new SceneReleaseState(
            section,
            path,
            IsPre: true,
            IsCompleted: false,
            IsNuked: false,
            NukeReason: null,
            LastChanged: DateTimeOffset.UtcNow);
    }

    public void MarkCompleted(string section, string path)
    {
        _states.AddOrUpdate(
            path,
            _ => new SceneReleaseState(
                section,
                path,
                IsPre: false,
                IsCompleted: true,
                IsNuked: false,
                NukeReason: null,
                LastChanged: DateTimeOffset.UtcNow),
            (_, existing) => existing with
            {
                IsCompleted = true,
                LastChanged = DateTimeOffset.UtcNow
            });
    }

    public void Nuke(string section, string path, string reason)
    {
        _states.AddOrUpdate(
            path,
            _ => new SceneReleaseState(
                section,
                path,
                IsPre: false,
                IsCompleted: false,
                IsNuked: true,
                NukeReason: reason,
                LastChanged: DateTimeOffset.UtcNow),
            (_, existing) => existing with
            {
                IsNuked = true,
                NukeReason = reason,
                LastChanged = DateTimeOffset.UtcNow
            });
    }

    public void Unnuke(string section, string path)
    {
        if (_states.TryGetValue(path, out var s))
        {
            _states[path] = s with
            {
                IsNuked = false,
                NukeReason = null,
                LastChanged = DateTimeOffset.UtcNow
            };
        }
    }

    public bool TryGet(string path, out SceneReleaseState state)
        => _states.TryGetValue(path, out state!);

    public IReadOnlyList<SceneReleaseState> GetPres()
        => _states.Values
            .Where(s => s.IsPre)
            .OrderByDescending(s => s.LastChanged)
            .ToList();

    public IReadOnlyList<SceneReleaseState> GetCompleted()
        => _states.Values
            .Where(s => s.IsCompleted)
            .OrderByDescending(s => s.LastChanged)
            .ToList();

    internal IReadOnlyCollection<SceneReleaseState> GetAll()
        => _states.Values.ToList();

    internal void Restore(SceneReleaseState state) => _states[state.Path] = state;
}