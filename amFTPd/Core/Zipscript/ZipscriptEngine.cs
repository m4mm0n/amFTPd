/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ZipscriptEngine.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-02 04:39:40
 *  Last Modified:  2025-12-14 13:59:23
 *  CRC32:          0x8B26F2C1
 *  
 *  Description:
 *      Zipscript engine: - Watches uploads of .sfv and listed files. - Computes CRC32 for uploaded files. - Tracks per-relea...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

using amFTPd.Config.Daemon;
using amFTPd.Core.Scene;
using amFTPd.Db;
using amFTPd.Logging;
using amFTPd.Utils.Cryptography;
using System.Collections.Concurrent;
using System.Globalization;

namespace amFTPd.Core.Zipscript;

/// <summary>
/// Zipscript engine:
/// - Watches uploads of .sfv and listed files.
/// - Computes CRC32 for uploaded files.
/// - Tracks per-release status (OK / BAD / MISSING / NUKED).
/// - Persists state to a small JSON DB for fast lookups on restart.
/// </summary>
public sealed class ZipscriptEngine
{
    private sealed class SfvEntry
    {
        public string FileName { get; init; } = string.Empty;
        public uint ExpectedCrc { get; init; }
    }

    private sealed class ReleaseState
    {
        public string ReleasePath { get; init; } = string.Empty;
        public string SectionName { get; init; } = string.Empty;

        public string? SfvVirtualPath { get; set; }
        public string? SfvPhysicalPath { get; set; }

        public DateTimeOffset Started { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

        public bool IsNuked { get; set; }
        public bool WasNuked { get; set; }
        public string? NukeReason { get; set; }
        public string? NukedBy { get; set; }
        public double? NukeMultiplier { get; set; }
        public DateTimeOffset? NukedAt { get; set; }

        public Dictionary<string, SfvEntry> SfvEntries { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ZipscriptFileInfo> Files { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly Lock _lock = new();
    private sealed class RescanGuard
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int RefCount;
    }

    private readonly ConcurrentDictionary<string, RescanGuard> _rescanGuards =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ReleaseState> _releases =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ZipscriptDbContext? _db;
    private readonly ZipscriptConfig _config;
    private readonly IFtpLogger? _log;

    private int _pendingDbWrites;
    private const int DbFlushThreshold = 32;

    private AmFtpdRuntimeConfig? _runtime;
    private readonly SceneStateRegistry? _sceneRegistry;

    /// <summary>
    /// Occurs when a release operation has completed, providing the final status of the release process.
    /// </summary>
    /// <remarks>Subscribers can use this event to perform actions after a release finishes, such as
    /// logging results or updating UI elements. The event provides a <see cref="ZipscriptReleaseStatus"/> value
    /// indicating the outcome of the release.</remarks>
    public event Action<ZipscriptReleaseStatus>? ReleaseCompleted;
    /// <summary>
    /// Occurs when the release status is updated.
    /// </summary>
    /// <remarks>Subscribers are notified whenever the associated release status changes. The event
    /// provides the new release status as an argument.</remarks>
    public event Action<ZipscriptReleaseStatus>? ReleaseUpdated;
    /// <summary>
    /// Occurs before a Zipscript context is detected, allowing subscribers to inspect or modify the context before
    /// processing continues.
    /// </summary>
    /// <remarks>Subscribe to this event to perform custom logic or validation prior to the detection
    /// of a Zipscript context. Handlers can access and modify the provided context as needed. If no handlers are
    /// attached, the detection proceeds without intervention.</remarks>
    public event Action<ZipscriptPreContext>? PreDetected;

    // ---------------------------------------------------------------------
    // Constructors
    // ---------------------------------------------------------------------
    /// <summary>
    /// Initializes a new instance of the ZipscriptEngine class with default settings.
    /// </summary>
    /// <remarks>This constructor creates a ZipscriptEngine instance using default configuration
    /// values. For advanced scenarios, use the overloaded constructor to specify custom options.</remarks>
    public ZipscriptEngine()
        : this(null, null, null)
    {
    }
    /// <summary>
    /// Initializes a new instance of the ZipscriptEngine class, optionally using the specified database context,
    /// logger, and configuration settings.
    /// </summary>
    /// <remarks>If a database context is provided, the engine attempts to ensure the database schema
    /// is up to date and loads initial data from the database. Any warnings or errors encountered during
    /// initialization are logged using the provided logger, if available.</remarks>
    /// <param name="db">The database context to use for loading and managing Zipscript data. If null, the engine will not load data
    /// from a database.</param>
    /// <param name="log">The logger used to record warnings and operational messages during initialization and runtime. If null,
    /// logging is disabled.</param>
    /// <param name="config">The configuration settings for the engine. If null, a default configuration is used.</param>
    public ZipscriptEngine(
        ZipscriptDbContext? db,
        IFtpLogger? log,
        ZipscriptConfig? config,
        SceneStateRegistry? sceneRegistry = null)
    {

        _db = db;
        _log = log;
        _config = config ?? new ZipscriptConfig();

        if (_db is not null)
        {
            try
            {
                ZipscriptDbMigrations.EnsureSchema(_db, msg =>
                    _log?.Log(FtpLogLevel.Warn, msg));

                var (_, files) = _db.Load();
                RebuildFromDb(files);
            }
            catch (Exception ex)
            {
                _log?.Log(
                    FtpLogLevel.Warn,
                    $"ZipscriptEngine: failed to initialize from DB '{_db.DbFilePath}': {ex.Message}",
                    ex);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------
    /// <summary>
    /// Associates the specified runtime configuration with the current instance.
    /// </summary>
    /// <param name="runtime">The runtime configuration to attach. Cannot be null.</param>
    public void AttachRuntime(AmFtpdRuntimeConfig runtime) => _runtime = runtime;

    /// <summary>
    /// Legacy API kept for compatibility: wraps into <see cref="ZipscriptUploadContext"/>.
    /// Call this from STOR/APPE handlers if you don't want to build the context manually.
    /// </summary>
    public void OnFileUploaded(
        string virtualFilePath,
        string physicalFilePath,
        string sectionName,
        long sizeBytes)
    {
        var ctx = new ZipscriptUploadContext(
            sectionName,
            virtualFilePath,
            physicalFilePath,
            sizeBytes,
            UserName: null,
            CompletedAt: DateTimeOffset.UtcNow);

        OnUploadComplete(ctx);
    }

    /// <summary>
    /// Notify the engine that an upload has completed successfully.
    /// This is the main entry point from STOR/APPE.
    /// </summary>
    public void OnUploadComplete(ZipscriptUploadContext ctx)
    {
        if (_runtime?.IsRecovering == true)
            return;

        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (string.IsNullOrWhiteSpace(ctx.VirtualFilePath))
            return;

        var normalizedVirt = NormalizeVirtualPath(ctx.VirtualFilePath);
        var releasePath = GetReleasePath(normalizedVirt);
        var fileName = Path.GetFileName(normalizedVirt);

        // Compute CRC32 for non-SFV files *before* taking the lock to avoid holding it while doing IO.
        uint? crc = null;
        var isSfv = fileName.EndsWith(".sfv", StringComparison.OrdinalIgnoreCase);

        if (!isSfv)
        {
            try
            {
                crc = Crc32.Compute(ctx.PhysicalFilePath);
            }
            catch (Exception ex)
            {
                _log?.Log(
                    FtpLogLevel.Warn,
                    $"Zipscript: failed to compute CRC32 for '{ctx.PhysicalFilePath}'.",
                    ex);
            }
        }

        lock (_lock)
        {
            var isNewRelease = false;

            if (!_releases.TryGetValue(releasePath, out var state))
            {
                state = new ReleaseState
                {
                    ReleasePath = releasePath,
                    SectionName = ctx.SectionName,
                    Started = ctx.CompletedAt,
                    LastUpdated = ctx.CompletedAt
                };

                _releases[releasePath] = state;
                isNewRelease = true;
            }
            else
            {
                state.LastUpdated = ctx.CompletedAt;
            }

            if (isSfv)
            {
                state.SfvVirtualPath = normalizedVirt;
                state.SfvPhysicalPath = ctx.PhysicalFilePath;
                LoadSfvIntoState(state);
            }
            else
            {
                UpdateFileInState(
                    state,
                    fileName,
                    ctx.SizeBytes,
                    crc,
                    ctx.CompletedAt);
            }

            var status = BuildStatus(state);

            ReleaseUpdated?.Invoke(status);

            if (status.IsComplete)
                ReleaseCompleted?.Invoke(status);

            if (isNewRelease)
                PreDetected?.Invoke(new ZipscriptPreContext(
                    ctx.SectionName,
                    Path.GetFileName(releasePath),
                    releasePath,
                    ctx.UserName ?? "UNKNOWN",
                    ctx.CompletedAt));
        }

        PersistSnapshotIfNeeded();
    }

    /// <summary>
    /// Notify the engine that a file or directory has been deleted.
    /// Call this from DELE/RMD/SITE WIPE handlers.
    /// </summary>
    public void OnDelete(ZipscriptDeleteContext ctx)
    {
        if (_runtime?.IsRecovering == true)
            return;

        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        var virt = NormalizeVirtualPath(ctx.VirtualPath);

        if (ctx.IsDirectory)
        {
            lock (_lock)
            {
                _releases.Remove(virt);
            }

            PersistSnapshotIfNeeded(force: true);
            return;
        }

        var releasePath = GetReleasePath(virt);
        var fileName = Path.GetFileName(virt);

        lock (_lock)
        {
            if (!_releases.TryGetValue(releasePath, out var state))
                return;

            if (!state.Files.TryGetValue(fileName, out var info))
                return;

            info.SizeBytes = 0;
            info.ActualCrc = null;
            info.State = ZipscriptFileState.Deleted;
            info.LastUpdatedAt = ctx.DeletedAt;
        }

        PersistSnapshotIfNeeded();
    }

    /// <summary>
    /// Rescan a directory from disk and rebuild its zipscript state.
    /// </summary>
    public ZipscriptReleaseStatus? OnRescanDir(ZipscriptRescanContext ctx)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        var virtRelease = NormalizeVirtualPath(ctx.VirtualReleasePath);


        var guard = _rescanGuards.GetOrAdd(virtRelease, _ => new RescanGuard());
        Interlocked.Increment(ref guard.RefCount);
        guard.Semaphore.Wait();
        try
        {

            if (!Directory.Exists(ctx.PhysicalReleasePath))
                return null;

            var searchOption = ctx.IncludeSubdirs || _config.IncludeSubdirsOnRescan
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            var state = new ReleaseState
            {
                ReleasePath = virtRelease,
                SectionName = ctx.SectionName,
                Started = ctx.RequestedAt,
                LastUpdated = ctx.RequestedAt
            };

            // If we already had state for this release, carry over nuke metadata.
            lock (_lock)
            {
                if (_releases.TryGetValue(virtRelease, out var existing))
                {
                    state.IsNuked = existing.IsNuked;
                    state.WasNuked = existing.WasNuked;
                    state.NukeReason = existing.NukeReason;
                    state.NukedBy = existing.NukedBy;
                    state.NukeMultiplier = existing.NukeMultiplier;
                    state.NukedAt = existing.NukedAt;
                }
            }

            string? sfvPath = null;
            string? sfvVirtPath = null;

            try
            {
                // Find the first SFV file in this release.
                foreach (var file in Directory.EnumerateFiles(ctx.PhysicalReleasePath, "*.sfv", searchOption))
                {
                    sfvPath = file;
                    var relative = Path.GetRelativePath(ctx.PhysicalReleasePath, file)
                        .Replace('\\', '/');
                    sfvVirtPath = virtRelease.TrimEnd('/') + "/" + relative;
                    break;
                }

                if (!string.IsNullOrEmpty(sfvPath))
                {
                    state.SfvPhysicalPath = sfvPath;
                    state.SfvVirtualPath = sfvVirtPath;
                    LoadSfvIntoState(state);
                }

                // Walk all files and update state.
                foreach (var file in Directory.EnumerateFiles(ctx.PhysicalReleasePath, "*.*", searchOption))
                {
                    var relative = Path.GetRelativePath(ctx.PhysicalReleasePath, file)
                        .Replace('\\', '/');
                    var fileVirt = virtRelease.TrimEnd('/') + "/" + relative;
                    var fileName = Path.GetFileName(fileVirt);

                    var fi = new FileInfo(file);
                    uint? crc = null;

                    var isSfv = fileName.EndsWith(".sfv", StringComparison.OrdinalIgnoreCase);
                    if (!isSfv)
                    {
                        try
                        {
                            crc = Crc32.Compute(file);
                        }
                        catch (Exception ex)
                        {
                            _log?.Log(
                                FtpLogLevel.Warn,
                                $"Zipscript RESCAN: failed to compute CRC32 for '{file}'.",
                                ex);
                        }
                    }

                    UpdateFileInState(
                        state,
                        fileName,
                        fi.Length,
                        crc,
                        ctx.RequestedAt);
                }
            }
            catch (Exception ex)
            {
                _log?.Log(
                    FtpLogLevel.Warn,
                    $"Zipscript RESCAN failed for '{ctx.PhysicalReleasePath}': {ex.Message}",
                    ex);
                return null;
            }

            lock (_lock)
            {
                _releases[virtRelease] = state;
            }

            PersistSnapshotIfNeeded(force: true);
            return BuildStatus(state);
        }
        finally
        {
            guard.Semaphore.Release();
            if (Interlocked.Decrement(ref guard.RefCount) == 0)
            {
                _rescanGuards.TryRemove(new KeyValuePair<string, RescanGuard>(virtRelease, guard));
                guard.Semaphore.Dispose();
            }
        }
    }

    /// <summary>
    /// Mark a release as nuked and propagate that information to all files.
    /// </summary>
    public void MarkReleaseNuked(
        string virtualReleasePath,
        string? sectionName,
        string nuker,
        string reason,
        double nukeMultiplier)
    {

        if (string.IsNullOrWhiteSpace(virtualReleasePath))
            return;

        var virt = NormalizeVirtualPath(virtualReleasePath);

        lock (_lock)
        {
            if (!_releases.TryGetValue(virt, out var state))
            {
                state = new ReleaseState
                {
                    ReleasePath = virt,
                    SectionName = sectionName,
                    Started = DateTimeOffset.UtcNow
                };
                _releases[virt] = state;
            }

            state.IsNuked = true;
            state.WasNuked = true;
            state.NukeReason = reason;
            state.NukedBy = nuker;
            state.NukeMultiplier = nukeMultiplier;
            state.NukedAt = DateTimeOffset.UtcNow;
            state.LastUpdated = DateTimeOffset.UtcNow;

            foreach (var fi in state.Files.Values)
            {
                fi.IsNuked = true;
                fi.NukeReason = reason;
                fi.NukedBy = nuker;
                fi.NukedAt = state.NukedAt;
                if (fi.State == ZipscriptFileState.Ok ||
                    fi.State == ZipscriptFileState.BadCrc ||
                    fi.State == ZipscriptFileState.Extra ||
                    fi.State == ZipscriptFileState.Pending)
                {
                    fi.State = ZipscriptFileState.Nuked;
                }
                fi.LastUpdatedAt = state.LastUpdated;
            }
        }

        PersistSnapshotIfNeeded();
    }

    /// <summary>
    /// Mark a release as unnuked (UNNUKE) while remembering that it was nuked before.
    /// </summary>
    public void MarkReleaseUnnuked(
        string virtualReleasePath,
        string unnuker)
    {
        if (string.IsNullOrWhiteSpace(virtualReleasePath))
            return;

        var virt = NormalizeVirtualPath(virtualReleasePath);

        lock (_lock)
        {
            if (!_releases.TryGetValue(virt, out var state))
                return;

            state.IsNuked = false;
            state.LastUpdated = DateTimeOffset.UtcNow;
            // keep WasNuked = true, keep reason/multiplier for history

            foreach (var fi in state.Files.Values)
            {
                fi.IsNuked = false;
                // Do not reset reason or NukedAt; they are historical.
                if (fi.State == ZipscriptFileState.Nuked)
                {
                    // After UNNUKE we don't infer more; the next RESCAN or upload will re-evaluate.
                    fi.State = ZipscriptFileState.Pending;
                }

                fi.LastUpdatedAt = state.LastUpdated;
            }
        }

        PersistSnapshotIfNeeded();
    }

    /// <summary>
    /// Get current status for a release directory (virtual path).
    /// </summary>
    public ZipscriptReleaseStatus? GetStatus(string virtualReleasePath)
    {
        if (string.IsNullOrWhiteSpace(virtualReleasePath))
            return null;

        var norm = NormalizeVirtualPath(virtualReleasePath);

        lock (_lock)
            return !_releases.TryGetValue(norm, out var state) ? null : BuildStatus(state);
    }

    /// <summary>
    /// Clear all in-memory state (e.g. on config reload).
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _releases.Clear();
        }

        PersistSnapshotIfNeeded(force: true);
    }

    // ---------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------

    private static string NormalizeVirtualPath(string virt)
    {
        var p = virt.Replace('\\', '/').Trim();
        if (!p.StartsWith("/"))
            p = "/" + p;
        return p;
    }

    private static string GetReleasePath(string virtFilePath)
    {
        var lastSlash = virtFilePath.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : virtFilePath[..lastSlash];
    }

    private void LoadSfvIntoState(ReleaseState state)
    {
        var path = state.SfvPhysicalPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        try
        {
            var lines = File.ReadAllLines(path);
            state.SfvEntries.Clear();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) ||
                    trimmed.StartsWith(";", StringComparison.Ordinal))
                    continue;

                // Classic SFV line: "filename.ext CRC32"
                var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                var fileName = parts[0];
                var crcStr = parts[^1];

                if (uint.TryParse(crcStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var crc))
                {
                    state.SfvEntries[fileName] = new SfvEntry
                    {
                        FileName = fileName,
                        ExpectedCrc = crc
                    };
                }
            }

            // Mark MISSING entries for all SFV-listed files we haven't seen yet
            foreach (var kvp in state.SfvEntries.Values)
            {
                if (!state.Files.TryGetValue(kvp.FileName, out var info))
                {
                    info = new ZipscriptFileInfo
                    {
                        FileName = kvp.FileName,
                        ExpectedCrc = kvp.ExpectedCrc,
                        State = ZipscriptFileState.Missing
                    };
                    state.Files[kvp.FileName] = info;
                }
                else
                {
                    info.ExpectedCrc = kvp.ExpectedCrc;
                    if (info.ActualCrc is null)
                        info.State = ZipscriptFileState.Missing;
                }
            }

            // Mark EXTRA for any files we have that do not appear in SFV
            foreach (var info in state.Files.Values.Where(fi =>
                         !state.SfvEntries.ContainsKey(fi.FileName) &&
                         fi.State == ZipscriptFileState.Pending))
            {
                info.State = ZipscriptFileState.Extra;
            }
        }
        catch (Exception ex)
        {
            _log?.Log(
                FtpLogLevel.Warn,
                $"Zipscript: failed to parse SFV '{path}': {ex.Message}",
                ex);
        }
    }

    private static void UpdateFileInState(
        ReleaseState state,
        string fileName,
        long sizeBytes,
        uint? crc,
        DateTimeOffset timestamp)
    {
        if (!state.Files.TryGetValue(fileName, out var info))
        {
            info = new ZipscriptFileInfo
            {
                FileName = fileName,
                CreatedAt = timestamp
            };
            state.Files[fileName] = info;
        }

        info.SizeBytes = sizeBytes;
        info.ActualCrc = crc;
        info.LastUpdatedAt = timestamp;

        if (state.SfvEntries.TryGetValue(fileName, out var entry))
        {
            info.ExpectedCrc = entry.ExpectedCrc;
            info.State = crc == entry.ExpectedCrc
                ? ZipscriptFileState.Ok
                : ZipscriptFileState.BadCrc;
        }
        else
        {
            // We don't yet know what SFV says about this file
            if (info.State != ZipscriptFileState.Deleted &&
                info.State != ZipscriptFileState.Nuked)
            {
                info.State = ZipscriptFileState.Pending;
            }
        }

        // Propagate nuke flag from release-level state, if any
        if (state.IsNuked)
        {
            info.IsNuked = true;
            info.NukeReason = state.NukeReason;
            info.NukedBy = state.NukedBy;
            info.NukedAt = state.NukedAt;
            if (info.State == ZipscriptFileState.Ok ||
                info.State == ZipscriptFileState.BadCrc ||
                info.State == ZipscriptFileState.Extra ||
                info.State == ZipscriptFileState.Pending)
            {
                info.State = ZipscriptFileState.Nuked;
            }
        }
    }

    private void RebuildFromDb(IReadOnlyCollection<ZipscriptFileEntity> files)
    {
        lock (_lock)
        {
            _releases.Clear();

            foreach (var group in files.GroupBy(
                         f => NormalizeVirtualPath(f.ReleasePath),
                         StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First();
                var releasePath = NormalizeVirtualPath(first.ReleasePath);

                var state = new ReleaseState
                {
                    ReleasePath = releasePath,
                    SectionName = first.SectionName,
                    Started = group.Min(f => f.CreatedAt),
                    LastUpdated = group.Max(f => f.LastUpdatedAt),
                    IsNuked = group.Any(f => f.IsNuked),
                    WasNuked = group.Any(f => f.IsNuked || f.NukeMultiplier.HasValue),
                    NukeMultiplier = group.Select(f => f.NukeMultiplier)
                                          .FirstOrDefault(m => m.HasValue),
                    NukeReason = group.Select(f => f.NukeReason)
                                      .FirstOrDefault(r => !string.IsNullOrEmpty(r)),
                    NukedBy = group.Select(f => f.NukedBy)
                                   .FirstOrDefault(u => !string.IsNullOrEmpty(u)),
                    NukedAt = group.Select(f => f.NukedAt)
                                   .FirstOrDefault(d => d.HasValue)
                };

                foreach (var row in group)
                {
                    var info = new ZipscriptFileInfo
                    {
                        FileName = row.FileName,
                        ExpectedCrc = row.ExpectedCrc,
                        ActualCrc = row.ActualCrc,
                        SizeBytes = row.SizeBytes,
                        State = row.State,
                        CreatedAt = row.CreatedAt,
                        LastUpdatedAt = row.LastUpdatedAt,
                        IsNuked = row.IsNuked,
                        NukeReason = row.NukeReason,
                        NukedBy = row.NukedBy,
                        NukedAt = row.NukedAt
                    };

                    state.Files[row.FileName] = info;

                    if (row.ExpectedCrc.HasValue &&
                        !state.SfvEntries.ContainsKey(row.FileName))
                    {
                        state.SfvEntries[row.FileName] = new SfvEntry
                        {
                            FileName = row.FileName,
                            ExpectedCrc = row.ExpectedCrc.Value
                        };
                    }
                }

                _releases[releasePath] = state;
            }
        }
    }

    private List<ZipscriptFileEntity> BuildDbSnapshotUnsafe()
    {
        var list = new List<ZipscriptFileEntity>();

        foreach (var state in _releases.Values)
        {
            foreach (var fi in state.Files.Values)
            {
                var entity = new ZipscriptFileEntity(
                    ReleasePath: state.ReleasePath,
                    SectionName: state.SectionName,
                    FileName: fi.FileName,
                    SizeBytes: fi.SizeBytes,
                    ExpectedCrc: fi.ExpectedCrc,
                    ActualCrc: fi.ActualCrc,
                    State: fi.State,
                    IsNuked: fi.IsNuked,
                    NukeReason: fi.NukeReason,
                    NukedBy: fi.NukedBy,
                    CreatedAt: fi.CreatedAt,
                    LastUpdatedAt: fi.LastUpdatedAt,
                    NukedAt: fi.NukedAt,
                    NukeMultiplier: state.NukeMultiplier);

                list.Add(entity);
            }
        }

        return list;
    }

    private void PersistSnapshotIfNeeded(bool force = false)
    {
        if (_db is null)
            return;

        List<ZipscriptFileEntity> snapshot;

        lock (_lock)
        {
            _pendingDbWrites++;

            if (!force && _pendingDbWrites < DbFlushThreshold)
                return;

            _pendingDbWrites = 0;
            snapshot = BuildDbSnapshotUnsafe();
        }

        try
        {
            _db.Save(ZipscriptDbMigrations.CurrentVersion, snapshot);
        }
        catch (Exception ex)
        {
            _log?.Log(
                FtpLogLevel.Warn,
                $"ZipscriptEngine: failed to persist snapshot to '{_db.DbFilePath}': {ex.Message}",
                ex);
        }
    }

    private static ZipscriptReleaseStatus BuildStatus(ReleaseState state)
    {
        var files = state.Files.Values
            .OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var hasSfv = !string.IsNullOrEmpty(state.SfvVirtualPath);

        var allSfvFiles = state.SfvEntries.Keys.ToHashSet(
            StringComparer.OrdinalIgnoreCase);

        var missing = files.Count(f => f.State == ZipscriptFileState.Missing);
        var bad = files.Count(f => f.State == ZipscriptFileState.BadCrc);
        var ok = files.Count(f => f.State == ZipscriptFileState.Ok);
        var extra = files.Count(f => f.State == ZipscriptFileState.Extra);

        var isComplete =
            hasSfv &&
            missing == 0 &&
            bad == 0 &&
            ok + extra > 0;

        return new ZipscriptReleaseStatus
        {
            ReleasePath = state.ReleasePath,
            SectionName = state.SectionName,
            HasSfv = hasSfv,
            IsComplete = isComplete,
            IsNuked = state.IsNuked,
            WasNuked = state.WasNuked,
            NukeReason = state.NukeReason,
            NukedBy = state.NukedBy,
            NukeMultiplier = state.NukeMultiplier,
            NukedAt = state.NukedAt,
            Started = state.Started,
            LastUpdated = state.LastUpdated,
            Files = files
        };
    }
}