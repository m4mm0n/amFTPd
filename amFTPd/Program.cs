/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           Program.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-14 21:55:50
 *  CRC32:          0x68995DB6
 *  
 *  Description:
 *      Represents the entry point of the amFTPd application, a managed FTP daemon.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using amFTPd.Config.Daemon;
using amFTPd.Core.Linting;
using amFTPd.Core.Migration;
using amFTPd.Logging;
using amFTPd.Properties;
using amFTPd.Utils;

namespace amFTPd
{
    /// <summary>
    /// Represents the entry point of the amFTPd application, a managed FTP daemon.
    /// </summary>
    /// <remarks>This class initializes the FTP server, loads the configuration, sets up logging, and handles
    /// the server lifecycle, including graceful shutdown on cancellation requests.</remarks>
    internal class Program
    {
        private static bool ShouldPrintBanner(
            bool validateMode,
            bool checkMigration)
        {
            if (validateMode || checkMigration)
                return false;

            if (Console.IsOutputRedirected || !Environment.UserInteractive)
                return false;

            return true;
        }

        static async Task Main(string[] args)
        {
            // ------------------------------------------------------------------
            // Argument parsing
            //   Supported flags:
            //     --validate / -v                    — lint config, no start
            //     --check-migration <source-dir>     — validate migration completeness
            //       --json <output.json>             — (optional) write JSON report
            //     --log <everything|something|quiet> — override QuickLog verbosity
            //   First non-flag argument is the config file path (default: amftpd.json).
            // ------------------------------------------------------------------
            bool validateMode = args.Any(a => a is "--validate" or "-v");
            bool checkMigration = args.Any(a => a is "--check-migration");
            var configFile = GetConfigFileArgument(args);
            var logMode = GetLogModeArgument(args);

            if (ShouldPrintBanner(validateMode, checkMigration))
                PrintBanner();

            // --check-migration expects the source directory as its next argument.
            string? migrationSource = null;
            string? migrationJson = null;
            if (checkMigration)
            {
                var idx = Array.IndexOf(args, "--check-migration");
                if (idx >= 0 && idx + 1 < args.Length && !args[idx + 1].StartsWith('-'))
                    migrationSource = args[idx + 1];

                var jsonIdx = Array.IndexOf(args, "--json");
                if (jsonIdx >= 0 && jsonIdx + 1 < args.Length)
                    migrationJson = args[jsonIdx + 1];
            }

            // ------------------------------------------------------------------
            // --validate mode: lint the config, print findings, exit w/o starting
            // ------------------------------------------------------------------
            if (validateMode)
            {
                RunValidation(configFile);
                return; // Environment.Exit is called inside RunValidation
            }

            // ------------------------------------------------------------------
            // --check-migration mode: validate import completeness, then exit
            // ------------------------------------------------------------------
            if (checkMigration)
            {
                if (string.IsNullOrWhiteSpace(migrationSource))
                {
                    Console.Error.WriteLine(
                        "Usage: amftpd [config.json] --check-migration <source-dir> [--json report.json]");
                    Environment.Exit(2);
                    return;
                }

                using var commandLogger = QuickLogFactory.CreateCommandLogger(logMode);
                await RunMigrationCheckAsync(configFile, migrationSource, migrationJson, commandLogger);
                return; // Environment.Exit called inside
            }

            using var logger = QuickLogFactory.Create(configFile, logMode);

            // ------------------------------------------------------------------
            // Daemon mode: configure file-backed QuickLog and start the server
            // ------------------------------------------------------------------
            var runtime = await AmFtpdConfigLoader.LoadAsync(configFile, logger);

            var server = new FtpServer(runtime, logger);
            var serverTask = server.StartAsync();

            // Shared shutdown coordination
            var shutdownTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // ------------------------------------------------------------------
            // Ctrl-C / SIGINT (all platforms) — graceful drain
            // ------------------------------------------------------------------
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                logger.Log(FtpLogLevel.Info, "[amFTPd] Ctrl-C received — initiating graceful shutdown...");
                shutdownTcs.TrySetResult();
            };

            // ------------------------------------------------------------------
            // POSIX signal handlers (Linux / macOS only)
            // ------------------------------------------------------------------
            PosixSignalRegistration? sigtermReg = null;
            PosixSignalRegistration? sighupReg = null;

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                sigtermReg = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
                {
                    ctx.Cancel = true; // suppress default termination
                    logger.Log(FtpLogLevel.Info, "[amFTPd] SIGTERM received — initiating graceful shutdown...");
                    shutdownTcs.TrySetResult();
                });

                sighupReg = PosixSignalRegistration.Create(PosixSignal.SIGHUP, ctx =>
                {
                    ctx.Cancel = true; // suppress default HUP behaviour
                    logger.Log(FtpLogLevel.Info, "[amFTPd] SIGHUP received — reloading configuration...");

                    // Reload is async; fire-and-forget from the signal handler.
                    _ = Task.Run(async () =>
                    {
                        var (success, msg, _) = await server
                            .ReloadConfigurationAsync()
                            .ConfigureAwait(false);

                        logger.Log(
                            success ? FtpLogLevel.Info : FtpLogLevel.Error,
                            $"[amFTPd] REHASH {(success ? "OK" : "FAILED")}: {msg}");
                    });
                });

                logger.Log(FtpLogLevel.Info,
                    "[amFTPd] POSIX signal handlers registered (SIGTERM=graceful shutdown, SIGHUP=rehash).");
            }

            try
            {
                // Run until shutdown is requested OR the server exits on its own
                var done = await Task.WhenAny(serverTask, shutdownTcs.Task);

                if (done == shutdownTcs.Task)
                {
                    logger.Log(FtpLogLevel.Info, "[amFTPd] Draining active transfers (up to 60s)...");
                    await server.GracefulStopAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                }
                else
                {
                    // serverTask completed (possibly with exception)
                    await serverTask; // re-throw if faulted
                }
            }
            catch (Exception ex)
            {
                logger.Log(FtpLogLevel.Error, "Server crashed", ex);
            }
            finally
            {
                sigtermReg?.Dispose();
                sighupReg?.Dispose();
            }

            logger.Log(FtpLogLevel.Info, "[amFTPd] Server stopped.");
        }

        private static string GetConfigFileArgument(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg is "--check-migration" or "--json" or "--log")
                {
                    i++;
                    continue;
                }

                if (arg.StartsWith('-'))
                    continue;

                return arg;
            }

            return "amftpd.json";
        }

        private static QuickLogMode? GetLogModeArgument(string[] args)
        {
            var idx = Array.IndexOf(args, "--log");
            if (idx < 0)
                return null;

            if (idx + 1 >= args.Length || args[idx + 1].StartsWith('-'))
            {
                Console.Error.WriteLine("Usage: amftpd [config.json] --log <everything|something|quiet>");
                Environment.Exit(2);
                return null;
            }

            try
            {
                return QuickLogOptions.ParseMode(args[idx + 1]);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.Exit(2);
                return null;
            }
        }

        // ------------------------------------------------------------------
        // Config validation / lint mode
        // ------------------------------------------------------------------
        static async Task RunMigrationCheckAsync(
            string configFile,
            string sourceDir,
            string? jsonOutput,
            IFtpLogger logger)
        {
            Console.WriteLine();
            Console.WriteLine($"  Migration check");
            Console.WriteLine($"  Source  : {Path.GetFullPath(sourceDir)}");
            Console.WriteLine($"  Config  : {Path.GetFullPath(configFile)}");
            Console.WriteLine(new string('─', 72));

            // Load the amFTPd runtime (read-only — no listener, no plugins needed).
            AmFtpdRuntimeConfig runtime;
            try
            {
                runtime = await AmFtpdConfigLoader.LoadAsync(configFile, logger);
            }
            catch (Exception ex)
            {
                WriteColoured($"  [ERR] Failed to load config: {ex.Message}", ConsoleColor.Red);
                Environment.Exit(2);
                return;
            }

            // Run the validator.
            MigrationReport report;
            try
            {
                report = await MigrationValidator.ValidateAsync(
                    sourceDir, runtime, logger);
            }
            catch (Exception ex)
            {
                WriteColoured($"  [ERR] Validation failed: {ex.Message}", ConsoleColor.Red);
                Environment.Exit(2);
                return;
            }

            // ── Human-readable output ──────────────────────────────────────────

            PrintMigrationSection("Users",
                $"source={report.Users.SourceCount}  target={report.Users.TargetCount}",
                report.Users.HasIssues);

            if (report.Users.MissingInTarget.Count > 0)
            {
                WriteColoured($"    Missing ({report.Users.MissingInTarget.Count}):", ConsoleColor.Red);
                foreach (var u in report.Users.MissingInTarget.Take(20))
                    WriteColoured($"      - {u}", ConsoleColor.Red);
                if (report.Users.MissingInTarget.Count > 20)
                    WriteColoured($"      … and {report.Users.MissingInTarget.Count - 20} more", ConsoleColor.Red);
            }

            if (report.Users.CreditMismatches.Count > 0)
            {
                WriteColoured($"    Credit mismatches ({report.Users.CreditMismatches.Count}):", ConsoleColor.Yellow);
                foreach (var m in report.Users.CreditMismatches.Take(10))
                    WriteColoured(
                        $"      {m.Username}: src={m.SourceKb:N0} KB  tgt={m.TargetKb:N0} KB  Δ={m.DeltaKb:+#;-#;0} KB",
                        ConsoleColor.Yellow);
            }

            PrintMigrationSection("Groups",
                $"source={report.Groups.SourceCount}  target={report.Groups.TargetCount}",
                report.Groups.HasIssues);

            if (report.Groups.MissingInTarget.Count > 0)
            {
                WriteColoured($"    Missing ({report.Groups.MissingInTarget.Count}):", ConsoleColor.Red);
                foreach (var g in report.Groups.MissingInTarget.Take(20))
                    WriteColoured($"      - {g}", ConsoleColor.Red);
            }

            PrintMigrationSection("Dupes",
                $"source={report.Dupes.SourceCount}  target={report.Dupes.TargetCount}  missing={report.Dupes.MissingCount}",
                report.Dupes.HasIssues);
            if (report.Dupes.UnknownSectionRecordCount > 0)
            {
                WriteColoured(
                    $"    Unresolved section rows: {report.Dupes.UnknownSectionRecordCount} " +
                    $"across sections [{string.Join(", ", report.Dupes.UnknownSections)}]",
                    ConsoleColor.Yellow);
                WriteColoured(
                    $"    Warning: {report.Dupes.UnknownSectionRecordCount} dupe rows were skipped because section aliases are missing",
                    ConsoleColor.Yellow);
            }

            if (report.Dupes.MissingInTarget.Count > 0)
            {
                WriteColoured($"    Sample of missing ({report.Dupes.MissingInTarget.Count} shown):", ConsoleColor.Yellow);
                foreach (var d in report.Dupes.MissingInTarget)
                    WriteColoured($"      [{d.Section}] {d.ReleaseName} ({d.Group})", ConsoleColor.Yellow);
                if (report.Dupes.MissingCount > report.Dupes.MissingInTarget.Count)
                    WriteColoured($"      … and {report.Dupes.MissingCount - report.Dupes.MissingInTarget.Count} more", ConsoleColor.Yellow);
            }

            PrintMigrationSection("Nukes",
                $"source={report.Nukes.SourceCount}  targetNuked={report.Nukes.TargetNukedCount}  unflagged={report.Nukes.MissingNukeFlagCount}",
                report.Nukes.HasIssues);
            if (report.Nukes.UnknownSectionRecordCount > 0)
            {
                WriteColoured(
                    $"    Unresolved section rows: {report.Nukes.UnknownSectionRecordCount} " +
                    $"across sections [{string.Join(", ", report.Nukes.UnknownSections)}]",
                    ConsoleColor.Yellow);
                WriteColoured(
                    $"    Warning: {report.Nukes.UnknownSectionRecordCount} nuke rows were skipped because section aliases are missing",
                    ConsoleColor.Yellow);
            }

            if (report.Nukes.MissingNukeFlag.Count > 0)
            {
                WriteColoured($"    Sample of unflagged ({report.Nukes.MissingNukeFlag.Count} shown):", ConsoleColor.Yellow);
                foreach (var n in report.Nukes.MissingNukeFlag)
                    WriteColoured($"      [{n.Section}] {n.Path}  x{n.Multiplier}  {n.Reason}", ConsoleColor.Yellow);
            }

            Console.WriteLine(new string('─', 72));

            var statusColour = report.Status switch
            {
                "Errors" => ConsoleColor.Red,
                "Warnings" => ConsoleColor.Yellow,
                _ => ConsoleColor.Green
            };
            WriteColoured($"  {report.Status}: {report.Summary}", statusColour);
            Console.WriteLine();

            // ── Optional JSON output ───────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(jsonOutput))
            {
                try
                {
                    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition =
                            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    });
                    await File.WriteAllTextAsync(jsonOutput!, json);
                    WriteColoured($"  Report written to: {Path.GetFullPath(jsonOutput!)}", ConsoleColor.Cyan);
                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    WriteColoured($"  [WRN] Could not write JSON report: {ex.Message}", ConsoleColor.Yellow);
                }
            }

            // ── Exit code ──────────────────────────────────────────────────────
            // 0 = all good  1 = warnings  2 = errors / data loss detected
            var exitCode = report.Status switch
            {
                "Errors" => 2,
                "Warnings" => 1,
                _ => 0
            };

            Environment.Exit(exitCode);
        }

        static void PrintMigrationSection(string name, string stats, bool hasIssues)
        {
            var colour = hasIssues ? ConsoleColor.Yellow : ConsoleColor.Green;
            var icon = hasIssues ? "⚠" : "✔";
            WriteColoured($"  {icon} {name,-10} {stats}", colour);
        }

        static void RunValidation(string configFile)
        {
            Console.WriteLine();
            Console.WriteLine($"  Validating: {Path.GetFullPath(configFile)}");
            Console.WriteLine(new string('─', 72));

            var result = ConfigValidator.Validate(configFile);

            if (result.Findings.Count == 0)
            {
                WriteColoured("  ✔ No findings — config looks clean.", ConsoleColor.Green);
                Console.WriteLine();
                Environment.Exit(0);
                return;
            }

            foreach (var f in result.Findings)
            {
                var (colour, prefix) = f.Severity switch
                {
                    LintSeverity.Error => (ConsoleColor.Red, "  [ERR] "),
                    LintSeverity.Warning => (ConsoleColor.Yellow, "  [WRN] "),
                    _ => (ConsoleColor.Cyan, "  [INF] ")
                };
                WriteColoured($"{prefix}{f.Code}  {f.Message}", colour);
            }

            Console.WriteLine(new string('─', 72));

            var errors = result.Findings.Count(f => f.Severity == LintSeverity.Error);
            var warnings = result.Findings.Count(f => f.Severity == LintSeverity.Warning);
            var infos = result.Findings.Count(f => f.Severity == LintSeverity.Info);

            var summaryColour = result.HasErrors ? ConsoleColor.Red
                              : result.HasWarnings ? ConsoleColor.Yellow
                                                   : ConsoleColor.Green;

            WriteColoured(
                $"  Result: {errors} error(s), {warnings} warning(s), {infos} info(s)  " +
                $"[exit {result.ExitCode}]",
                summaryColour);
            Console.WriteLine();

            Environment.Exit(result.ExitCode);
        }

        static void WriteColoured(string text, ConsoleColor colour)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = colour;
            Console.WriteLine(text);
            Console.ForegroundColor = prev;
        }

        static void PrintBanner()
        {
            AnsiConsoleImage.WriteImage(Resources.amftpd_logo);
            var ver = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            $"amFTPd - a managed FTP daemon v{ver}".WriteBoxedBanner();
            Console.Title = $"amFTPd - a managed FTP daemon v{ver}";
            "Press Ctrl+C to stop.\n".WriteStyledLogLine();
        }
    }
}
