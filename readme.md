# amFTPd

amFTPd is a modern .NET 10 FTP/FTPS daemon built to replace legacy scene FTP
daemons such as ioFTPD and glFTPd while keeping the workflows that made those
servers useful: SITE commands, sections, credits, ratio rules, dupe databases,
nuke/unnuke flows, zipscript state, FXP policy, migration tooling, and operator
automation.

Version `0.8.0.0` is the polish and hardening release. It moves the project to
.NET 10, removes the Windows Service execution path, and standardizes daemon
logging on QuickLog only.

## Highlights

- FTP/FTPS daemon targeting `.NET 10`.
- Explicit foreground process model. Windows Service install/run support is
  intentionally removed to avoid a privileged Windows SCM attack surface.
- QuickLog-only logging through `ZLS.QuickLog`, with runtime modes for
  `everything`, `something`, and `quiet`.
- Rich SITE command set for users, groups, credits, ratios, sections, dupes,
  pres, nukes, stats, quota, requests, symlinks, and administration.
- ioFTPD/glFTPd migration validation with human output, JSON reports, and
  representative fixture coverage.
- VFS support with physical mounts, per-user mounts, release/pre virtual
  directories, shortcuts, and symlink support.
- TLS-first authentication, IDENT policy support, IP masks, bans, hammer guard,
  and transfer policy enforcement.
- FXP policy engine with active/passive and secure-data checks.
- Zipscript and scene-state tracking for upload, rescan, delete, pre, nuke, and
  unnuke workflows.
- Plugin surface for custom SITE commands, auth providers, and event handlers.
- REST/status/metrics endpoints for administration and observability.
- Docker and GitHub Actions release packaging.

## Repository Layout

| Path | Purpose |
|---|---|
| `amFTPd/` | Main daemon source |
| `amFTPd.Tests/` | xUnit regression, readiness, migration, and workflow tests |
| `amFTPd.Scripting.Tcl/` | Tcl scripting integration |
| `Plugins/amFTPd.Plugin.Abstractions/` | Plugin contracts |
| `Plugins/amFTPd.SamplePlugin/` | Example plugin |
| `Plugins/amFTPd.Db.Abstractions/` | SQL provider contracts |
| `Plugins/amFTPd.Sqlite/` | SQLite provider |
| `Plugins/amFTPd.MySql/` | MySQL provider |
| `Plugins/amFTPd.Postgres/` | PostgreSQL provider |
| `docs/` | Operator, migration, API, plugin, and command documentation |
| `.github/workflows/main.yml` | Tag-triggered release workflow |

Local runtime folders such as `config/`, `data/`, `logs/`, `site-root/`, and
`Ready2Release/` are ignored and are not part of the source release.

## Requirements

- .NET SDK `10.0.203` or newer compatible .NET 10 SDK.
- Windows, Linux, or Docker.
- A TLS certificate in PFX format for production FTPS.
- Open firewall ports for the control socket and passive data range.

The repo includes `global.json`, `Directory.Build.props`, and `.editorconfig`
to keep the SDK, language, analysis, and formatting expectations consistent.

## Quick Start

Build and test:

```powershell
dotnet restore amFTPd.sln
dotnet build amFTPd.sln -c Release -warnaserror
dotnet test amFTPd.Tests\amFTPd.Tests.csproj -c Release --no-build
```

Validate a config without starting the daemon:

```powershell
dotnet .\amFTPd\bin\Release\net10.0\amFTPd.dll amftpd.json --validate
```

Start the daemon in foreground mode:

```powershell
dotnet .\amFTPd\bin\Release\net10.0\amFTPd.dll amftpd.json
```

Override QuickLog verbosity:

```powershell
dotnet .\amFTPd\bin\Release\net10.0\amFTPd.dll amftpd.json --log everything
dotnet .\amFTPd\bin\Release\net10.0\amFTPd.dll amftpd.json --log something
dotnet .\amFTPd\bin\Release\net10.0\amFTPd.dll amftpd.json --log quiet
```

## Configuration

The daemon reads one JSON config file. The primary blocks are:

- `Server`: bind address, port, passive range, TLS/auth defaults, FXP and active
  mode defaults.
- `Tls`: PFX certificate path, password, and subject.
- `Storage`: user, group, section, dupe, rule, script, and runtime database
  paths.
- `Vfs`: mounts, per-user mounts, virtual files, shortcuts, and symlinks.
- `Sections`, `DirectoryRules`, `RatioRules`, `Groups`: scene-style section and
  account model.
- `FxpPolicy`: server-to-server transfer policy.
- `Ident`: RFC 1413 lookup and enforcement.
- `Logging`: QuickLog mode, text log path, binary log path, console output, and
  queue capacity.
- `Status`: status endpoint, REST API, metrics, and auth token.
- `Plugins`, `Irc`, `Webhooks`, `Acme`, `Compatibility`, and `Zipscript`.

Run the validator before deployment:

```powershell
amftpd amftpd.json --validate
```

See [docs/Configuration.md](docs/Configuration.md) for the full reference.

## QuickLog

QuickLog is the default and only daemon logger. The old console/file/combined
logger stack was removed. Configuration lives under the top-level `Logging`
block:

```json
"Logging": {
  "Mode": "something",
  "TextLogPath": "logs/amftpd.log",
  "BinaryLogPath": "logs/amftpd.qlbin",
  "Console": true,
  "Binary": true,
  "QueueCapacity": 8192
}
```

Admins can inspect or change verbosity at runtime:

```text
SITE LOG STATUS
SITE LOG EVERYTHING
SITE LOG SOMETHING
SITE LOG QUIET
```

If the configured log file is locked, amFTPd stays on QuickLog and falls back to
a process-specific log file instead of crashing on startup.

## Running Without Windows Service Mode

amFTPd deliberately does not install or run as a Windows Service in `0.8.0.0`.
Run it explicitly in a terminal, a supervised shell, Docker, a process manager,
or systemd on Linux. This keeps the daemon away from the privileged Windows SCM
execution path and makes startup/shutdown behavior easier to audit.

## Migration From ioFTPD and glFTPd

The migration checker compares imported source data against the amFTPd runtime
state and reports missing users, groups, dupes, nukes, credits, and unresolved
legacy section aliases.

```powershell
amftpd amftpd.json --check-migration C:\legacy-site --json migration-report.json
```

Exit codes:

- `0`: migration check is clean.
- `1`: warnings, such as unresolved legacy sections.
- `2`: errors or likely data loss.

See [docs/Migration.md](docs/Migration.md) for the full import flow.

## Docker

Build locally:

```powershell
docker build -t amftpd:0.8.0.0 .
```

Run with mounted config and data:

```powershell
docker run --rm -p 2121:2121 -p 50000-50100:50000-50100 `
  -v ${PWD}\config:/config:ro `
  -v ${PWD}\data:/data `
  amftpd:0.8.0.0 /config/amftpd.json
```

Or use:

```powershell
docker compose up --build
```

## Release Workflow

GitHub Actions creates release assets when a `v*` tag is pushed:

```powershell
git tag v0.8.0.0
git push origin v0.8.0.0
```

The workflow builds Windows and Linux binaries, publishes a Docker image to
GHCR, and creates a GitHub Release with the generated archives.

## Documentation

- [Getting Started](docs/GettingStarted.md)
- [Configuration](docs/Configuration.md)
- [SITE Commands](docs/SiteCommands.md)
- [Migration](docs/Migration.md)
- [Plugins](docs/Plugins.md)
- [REST API](docs/RestAPI.md)
- [Docker](docs/Docker.md)
- [Scene Replacement Gap Report](docs/SceneReplacementGapReport.md)

## Validation Used For 0.8.0.0

The local release pass for `0.8.0.0` used:

```powershell
dotnet format amFTPd.sln --verify-no-changes --verbosity minimal
dotnet build amFTPd.sln -c Release -warnaserror
dotnet test amFTPd.Tests\amFTPd.Tests.csproj -c Release --no-build
dotnet .\amFTPd\bin\Release\net10.0\amFTPd.dll amftpd.json --validate
dotnet publish amFTPd\amFTPd.csproj -c Release -o Ready2Release\amFTPd
.\Ready2Release\amFTPd\amFTPd.exe amftpd.json --validate
```

The published daemon was also smoke-tested by accepting an FTP control
connection and returning the configured `220` greeting.

## License

MIT. See the project license file for details.
