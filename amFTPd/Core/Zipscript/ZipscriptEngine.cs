/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-02
 *  Last Modified:  2025-12-02
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

using System.Globalization;
using amFTPd.Utils;

namespace amFTPd.Core.Zipscript
{
    /// <summary>
    /// Simple in-memory zipscript engine:
    /// - Watches uploads of .sfv and listed files.
    /// - Computes CRC32 for uploaded files.
    /// - Tracks per-release status (OK / BAD / MISSING).
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

            public Dictionary<string, SfvEntry> SfvEntries { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, ZipscriptFileInfo> Files { get; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        private readonly object _lock = new();
        private readonly Dictionary<string, ReleaseState> _releases =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Notify the engine that a file has been uploaded.
        /// Call this from STOR/APPE handlers after successful upload.
        /// </summary>
        public void OnFileUploaded(
            string virtualFilePath,
            string physicalFilePath,
            string sectionName,
            long sizeBytes)
        {
            if (string.IsNullOrWhiteSpace(virtualFilePath))
                return;

            // Normalize and derive release directory + file name
            var normalizedVirt = NormalizeVirtualPath(virtualFilePath);
            var releasePath = GetReleasePath(normalizedVirt);
            var fileName = Path.GetFileName(normalizedVirt);

            // Compute CRC32 for non-SFV files *before* taking the lock to avoid holding it while doing IO.
            uint? crc = null;
            var isSfv = fileName.EndsWith(".sfv", StringComparison.OrdinalIgnoreCase);
            if (!isSfv)
            {
                crc = Crc32.Compute(physicalFilePath);
            }

            lock (_lock)
            {
                if (!_releases.TryGetValue(releasePath, out var state))
                {
                    state = new ReleaseState
                    {
                        ReleasePath = releasePath,
                        SectionName = sectionName,
                        Started = DateTimeOffset.UtcNow,
                        LastUpdated = DateTimeOffset.UtcNow
                    };
                    _releases[releasePath] = state;
                }
                else
                {
                    state.LastUpdated = DateTimeOffset.UtcNow;
                }

                if (isSfv)
                {
                    state.SfvVirtualPath = normalizedVirt;
                    state.SfvPhysicalPath = physicalFilePath;
                    LoadSfvIntoState(state);
                }
                else
                {
                    UpdateFileInState(state, fileName, sizeBytes, crc);
                }
            }
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
        /// Optional: clear all in-memory state (e.g. on config reload).
        /// </summary>
        public void Clear()
        {
            lock (_lock) _releases.Clear();
        }

        #region Internals
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

            state.SfvEntries.Clear();

            using (var sr = new StreamReader(path))
                while (sr.ReadLine() is { } line)
                {
                    line = line.Trim();
                    if (line.Length == 0)
                        continue;
                    if (line.StartsWith(";") || line.StartsWith("#"))
                        continue;

                    // Basic "filename CRC" format (separated by whitespace)
                    var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                        continue;

                    var fileName = parts[0].Trim();
                    var crcStr = parts[1].Trim();

                    if (crcStr.Length != 8)
                        continue;

                    if (!uint.TryParse(crcStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var crc))
                        continue;

                    state.SfvEntries[fileName] = new SfvEntry
                    {
                        FileName = fileName,
                        ExpectedCrc = crc
                    };
                }

            // Now re-evaluate statuses for all files in this release
            foreach (var kvp in state.SfvEntries)
            {
                var fileName = kvp.Key;
                var entry = kvp.Value;

                if (!state.Files.TryGetValue(fileName, out var info))
                {
                    // Seen in SFV, not yet uploaded
                    info = new ZipscriptFileInfo
                    {
                        FileName = fileName,
                        ExpectedCrc = entry.ExpectedCrc,
                        ActualCrc = null,
                        SizeBytes = 0,
                        State = ZipscriptFileState.Missing
                    };
                    state.Files[fileName] = info;
                }
                else
                {
                    info.ExpectedCrc = entry.ExpectedCrc;
                    if (info.ActualCrc is null)
                    {
                        info.State = ZipscriptFileState.Missing;
                    }
                    else
                    {
                        info.State = info.ActualCrc == entry.ExpectedCrc
                            ? ZipscriptFileState.Ok
                            : ZipscriptFileState.BadCrc;
                    }
                }
            }

            // Mark extra files not in SFV
            foreach (var kvp in state.Files.Values.Where(kvp => !state.SfvEntries.ContainsKey(kvp.FileName)))
                kvp.State = ZipscriptFileState.Extra;
        }

        private static void UpdateFileInState(
            ReleaseState state,
            string fileName,
            long sizeBytes,
            uint? crc)
        {
            if (!state.Files.TryGetValue(fileName, out var info))
            {
                info = new ZipscriptFileInfo
                {
                    FileName = fileName
                };
                state.Files[fileName] = info;
            }

            info.SizeBytes = sizeBytes;
            info.ActualCrc = crc;

            if (state.SfvEntries.TryGetValue(fileName, out var entry))
            {
                info.ExpectedCrc = entry.ExpectedCrc;
                info.State = crc == entry.ExpectedCrc
                    ? ZipscriptFileState.Ok
                    : ZipscriptFileState.BadCrc;
            }
            else
                // We don't yet know what SFV says about this file
                info.State = ZipscriptFileState.Pending;
        }

        private static ZipscriptReleaseStatus BuildStatus(ReleaseState state)
        {
            var hasSfv = state.SfvEntries.Count > 0;

            var files = state.Files.Values
                .OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var isComplete = hasSfv &&
                             files.Any() &&
                             files.All(f => f.State == ZipscriptFileState.Ok ||
                                            f.State == ZipscriptFileState.Extra);

            return new ZipscriptReleaseStatus
            {
                ReleasePath = state.ReleasePath,
                SectionName = state.SectionName,
                HasSfv = hasSfv,
                IsComplete = isComplete,
                Files = files
            };
        }
        #endregion
    }
}
