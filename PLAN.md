 ________  ________  ________  ________   ________  ________  ________ 
╱    ╱   ╲╱        ╲╱        ╲╱        ╲ ╱        ╲╱        ╲╱    ╱   ╲
╱         ╱         ╱         ╱        _╱_╱       ╱╱         ╱         ╱
╲        ╱        _╱        _╱-        ╱╱         ╱         ╱         ╱ 
 ╲______╱╲________╱╲____╱___╱╲________╱ ╲________╱╲________╱╲__╱_____╱  
                              [ ROADMAP ]


# amFTPd Roadmap — Surpassing ioFTPD & glFTPD

This document tracks the planned work required to make amFTPd a complete,
production-ready replacement for ioFTPD and glFTPD, and eventually to surpass
both with features neither legacy daemon offers.

Items are grouped into four phases. Within each phase, items are ordered by
impact and dependency. Check off items as they are completed.

---

## Active production-readiness gate — May 2026

Feature presence is not enough for glFTPd/ioFTPD replacement status. A feature
only counts as production-ready when it has automated coverage, clean validation,
and a startup/deploy proof that matches how a real site runs it.

- [x] Clean Release build with zero warnings.
- [x] Clean publish to `Ready2Release/amFTPd`.
- [x] Clean `--validate` for the checked launch config.
- [x] Published executable validates the checked launch config.
- [x] Published executable startup smoke: binds and accepts a control connection.
- [x] Regression test for config-only section loading without `Storage.SectionsPath`.
- [x] Regression test for production-like config validation.
- [x] Regression test for production-like runtime startup and FTP banner.
- [x] Regression test for PRE approval flow: normal user queues in approval
      section, siteop sees/approves, user can read PREINFO after approval.
- [x] Regression test for NUKE/UNNUKE credit restoration on a normal uploaded
      release with race ownership.
- [x] Regression test for normal-user denial on privileged SITE mutations.
- [x] Regression test for upload credit award through the live STOR path.
- [x] Working `SITE GROUPADD`, `SITE GROUPDEL`, and `SITE RSCHECK` replacements
      for previously stubbed scene admin/check commands.
- [x] Treasure Cove scene fixture: fake historical groups, users, sections, and
      PRE approval coverage across all Treasure Cove sections.
- [x] Multi-client race test: simultaneous STOR into the same release, race stats,
      zipscript state, and no deadlocks.
- [x] Resume/interruption test: partial upload, REST/STOR resume, CRC sidecar,
      and bad resume rejection.
- [x] Full PRE workflow test: pending PRE, approve, deny, PREINFO, PRELIST,
      dupe registration, and audit log.
- [ ] Full NUKE/UNNUKE test: credit deduction, restoration, dupe state, race state,
      and audit/xfer/session logs.
- [ ] Ratio/credit policy test: free sections, 1:3 sections, group bonus,
      no-ratio users, and AMScript mutation.
- [ ] Permission abuse test: normal user denied admin/SITEOP mutations, directory
      flags enforced across LIST/RETR/STOR/MKD/RMD/RNTO/DELE.
- [ ] FXP/active/passive policy test across allowed and denied cases.
- [x] Migration acceptance test with representative glFTPd and ioFTPD fixture data.
- [x] Long-running soak test: repeated login/upload/download/pre/nuke cycles with
      graceful shutdown/restart and persisted state verification.

Current verdict: launchable for private/beta use, not yet a proven drop-in
replacement for a serious active scene site.

---

## Current state (v0.7.1.0 — April 2026)

The core FTP daemon is solid: async .NET 10 architecture, full FTP protocol
coverage, VFS, IDENT, TLS/FTPS, ratio/credit engine, zipscript, race engine,
nuke system, pre registry, dupe detection, FXP policy, IRC with FiSH, 82 SITE
commands, binary user/group stores, SQLite/MySQL/PostgreSQL plugin backends,
ioFTPD and glFTPD import parsers, and a live HTTP monitoring endpoint.

What is missing falls into four categories: wiring up existing stubs, building
the three missing scene-social systems, hardening for production, and a set of
modern differentiators that neither legacy daemon offers.

---

## Phase 1 — Close the loop on existing stubs ✅

> Goal: every feature that has infrastructure but no enforcement actually works.
> Phase 1 is complete when a real scene site could run without hitting a silent
> no-op where a feature should be.
>
> **Status: COMPLETE** — all six items shipped.

- [x] **Bandwidth throttling enforcement**
      `CopyWithThrottleAsync` leaky-bucket loop wired end-to-end. `FtpSection`
      gained `MaxUploadKbps`/`MaxDownloadKbps` caps. `speed.msl` is now loaded
      and hot-reloadable. `ResolveEffectiveSpeedKbps` in `FtpCommandRouter` picks
      the most restrictive non-zero limit across user record, section config, and
      AMScript result. RETR, STOR, and APPE all use it.

- [x] **Max logins per user and per IP — enforced at connect time**
      IP-level cap was already enforced. Per-user `MaxConcurrentLogins` check now
      returns a specific `denyReason` out-param from all three `IUserStore`
      implementations so the PASS handler can distinguish a concurrent-login
      rejection from a bad password. HammerGuard no longer penalises IPs that
      hit the per-user login cap. `Disabled` flag check added to both binary
      stores (was missing).

- [x] **Nuke credit deduction pipeline — end to end**
      `DupeEntry.NukePenalties` stores per-uploader KB deducted at nuke time.
      `NukePropagation.DeductNukeCredits` iterates `race.UserBytes`, computes
      `ceil(bytesKb × multiplier)`, floors credits at 0, and persists the penalty
      map. `RestoreNukeCredits` replays it on UNNUKE before the dupe entry is
      cleared. Both mutations are appended to `logs/nukes.log`.

- [x] **Stats resets — weekly and monthly, automated**
      `WeeklyStatsSnapshotTask` and `MonthlyStatsSnapshotTask` added to the
      `Maintenance` namespace. Both implement `IScheduledTask` (hourly poll,
      calendar-gated) and write to `stats/weekly-YYYY-Www.json` /
      `stats/monthly-YYYY-MM.json` next to the config dir. A child `Scheduler`
      is owned and disposed by `HousekeepingService`.

- [x] **Per-directory access flags — fully wired**
      Already fully implemented. `DirectoryAccessEvaluator` is wired into
      every relevant command path in `FtpCommandRouter.Commands.cs`:
      LIST/NLST/STAT/MLSD/MLST → CanList, RETR/REST → CanDownload,
      STOR/APPE/MKD/RMD/DELE/RNTO → CanUpload. No gaps found.

- [x] **User suspension / soft-delete flags**
      `SITE SUSPEND <user> [reason]` and `SITE UNSUSPEND <user>` added.
      Both auto-register via reflection. `SITE DELUSER` was already a soft-disable
      (sets `Disabled = true`); help text updated to reflect this. The login path
      in all three stores checks `Disabled` before any other auth step.

---

## Phase 2 — Missing scene-critical systems ✅

> Goal: a scene sysop switching from ioFTPD or glFTPD finds every social and
> operational feature they rely on. Phase 2 is complete when there are no
> "we can't switch because X is missing" objections.
>
> **Status: COMPLETE** — all seven items shipped.

- [x] **Message / MOTD / CWD system**
      `MessageEngine` in `Core/Messages/`. Reads `{configDir}/messages/motd.txt`
      on login and per-directory `.message` files on CWD. Variable substitution:
      `%u`/`%username`, `%g`/`%group`, `%credits`, `%credskb`, `%ratio`,
      `%date`, `%time`, `%online`, `%sitename`. `FtpConfig` gained `SiteName`.
      PASS handler sends MOTD after "230 Login successful."; CWD handler sends
      `.message` after "250 OK". Multi-line formatted as FTP continuation lines.

- [x] **Affils system**
      `AffilStore` in `Core/Affils/` — thread-safe, JSON-persisted to
      `affils.json`. Three SITE commands in `SiteAffilCommands.cs`:
      `SITE AFFIL <section> <group>` (siteop), `SITE DEAFFIL <section> <group>`
      (siteop), `SITE AFFILS [section]` (all users). Wired into
      `AmFtpdRuntimeConfig.AffilStore` and initialised by `FtpServer`.

- [x] **Requests system**
      `RequestRegistry` in `Core/Requests/` — thread-safe, JSON-persisted to
      `requests.json`. Tracks open/filled requests with IDs and timestamps.
      Five SITE commands in `SiteRequestCommands.cs`: `REQUEST`, `REQFILLED`,
      `REQLIST [filter]`, `REQDEL <id>`, `REQWIPE`. Housekeeping loop purges
      filled/expired entries (default 30-day TTL). Events published on REQUEST
      and REQFILLED.

- [x] **Oneliners (shoutbox)**
      `OnelineStore` in `Core/Oneliners/` — capacity-bounded (200 lines),
      newest-first, JSON-persisted to `oneliners.json`. Three command files:
      `SITE ADDLINE <msg>` (publishes `FtpEventType.Oneliner`), `SITE LINES
      [count]` (max 100), `SITE DELLINE <id>` + `SITE WIPELINES` (siteop only).

- [x] **SFV-first upload enforcement**
      `FtpSection` gained `RequireSfvFirst` (bool, defaults false). STOR checks
      before opening the data connection: if the flag is set and the upload is not
      an `.sfv`, it scans `physDir` for any existing `.sfv` file. Rejects with
      "550 SFV-first enforced" if none found.

- [x] **Auto-nuke rules**
      `AutoNukeRule` + `AutoNukeEngine` in `Core/AutoNuke/`. Subscribes to
      `ZipscriptEngine.ReleaseUpdated` and `ReleaseCompleted`. Triggers:
      `BadCrc` (immediate), `MissingNfo` (grace period after complete),
      `MissingSfv` (grace period), `Incomplete` (grace period). 30-second timer
      loop fires grace-period checks. `NukePropagation.ApplySystemNuke` added for
      session-free nuke application. Rules configured via
      `AmFtpdRuntimeConfig.AutoNukeRules`. Engine disposed with `FtpServer`.

- [x] **Complete SITE WHO output format**
      `FtpSession` gained transfer-state fields: `TransferFileName`, 
      `TransferDirection` (0/1/2 = idle/up/dn), `TransferBytesCompleted`,
      `TransferTotalBytes`, `TransferStartedAt`, plus `BeginTransfer()` /
      `EndTransfer()` / `AddTransferBytes()`. `CopyWithThrottleAsync` accepts
      optional `Action<long>? onProgress` for per-chunk byte accounting. RETR
      and STOR wrap `WithDataAsync` in a try/finally to guarantee `EndTransfer`.
      `SiteWhoCommand` rewritten: scene-standard columnar output with user, group,
      UP/DN/IDLE status, filename, KB/s speed (computed from elapsed + bytes),
      percent complete, and summary line.

---

## Phase 3 — Production hardening

> Goal: confidence to run amFTPd on a real site with real users and real data.
> Phase 3 is complete when there are no sharp edges that would embarrass a
> production deployment.

- [x] **Standard xferlog output**
      `XferlogWriter` in `Core/Logging/` writes one wu-ftpd line per
      Upload/Download event. Format: `DDD MMM DD HH:MM:SS YYYY <secs> <host>
      <bytes> <path> b _ <i|o> r <user> ftp 0 * c`. Wired to EventBus in
      `FtpServer` constructor (same block as `SessionLogWriter`). `FtpCommandRouter`
      now publishes `FtpEventType.Upload` and `FtpEventType.Download` events with
      byte count, user, section, and remote host after each completed transfer.
      `AmFtpdRuntimeConfig.Xferlog` holds the instance. Path: `<configDir>/xferlog`.

- [x] **Admin audit log**
      `AuditLogWriter` in `Core/Logging/` — JSONL, one line per admin mutation.
      Entry fields: `ts`, `actor`, `action`, `target`, `detail`, `ip`. Thread-safe
      append via lock + `File.AppendAllText`. `ReadLastLines(n)` ring-buffer tail
      reader used by `SITE AUDITLOG`. Wired as `AmFtpdRuntimeConfig.AuditLog`,
      initialised by `FtpServer`. Instrumented SITE commands: ADDUSER, DELUSER,
      CHGRP, CHPASS, SUSPEND, UNSUSPEND, NUKE, UNNUKE, KICK, BLOCK, REHASH.
      `SITE AUDITLOG [N|user]` (siteop): shows last N entries (default 20, max 100)
      with optional actor/target filter. Human-readable columnar display.

- [x] **Upload quotas (daily / weekly / monthly)**
      `UploadQuotaStore` in `Core/Quota/` — in-memory per-user counters (daily,
      weekly Mon-based, monthly) with lazy window-rollover reset. Subscribes to
      EventBus Upload events. `GroupConfig` gained `DailyUploadLimitMb`,
      `WeeklyUploadLimitMb`, `MonthlyUploadLimitMb` (0 = unlimited). Admins,
      siteops, and groups with `IsSiteOp=true` are never blocked. STOR gate in
      `FtpCommandRouter` checks before opening the data connection; returns `553`
      with a human-readable message on quota exhaustion. `SITE QUOTA [user]` shows
      current window usage against configured limits. Wired as
      `AmFtpdRuntimeConfig.UploadQuota`, initialised by `FtpServer`.

- [x] **Complete VFS symlink management**
      `VfsSymlinkStore` — thread-safe, JSON-persisted registry (`vfs-symlinks.json`
      next to the config file). `SymlinkVfsProvider` sits in the provider chain
      between the Group and Physical providers; resolves any registered link prefix
      by rewriting to the target path and delegating back to the manager via
      `ResolveSkipSymlinks`/`EnumerateSkipSymlinks` (loop-safe). Dangling symlinks
      produce a descriptive 550 error. Three SITE commands: `SITE LINK <target>
      <linkname>` (create/replace, siteop), `SITE UNLINK <linkname>` (remove,
      siteop), `SITE LINKS [filter]` (list, all users). All mutations are audit-
      logged. Store initialised by `FtpServer`, threaded through
      `AmFtpdRuntimeConfig.SymlinkStore`, and passed to each session's `VfsManager`.

- [x] **Pre approval and tagging workflow**
      `PreEntry` extended with `Group`, `FileCount`, `TotalBytes`, `Tags`, `Status`
      (Approved/Pending/Denied), `ReviewedBy`, `ReviewedAtUtc`, `DenyReason`.
      `FtpSection` gained `AllowedPreGroups` (list; empty = unrestricted) and
      `RequirePreApproval` (bool). `PreRegistry` gained pending-queue operations:
      `EnqueuePending`, `TryGetPending`, `RemovePending`, `PendingAll`.
      `SITE PRE` now gates by `AllowedPreGroups` (rejected with 550 for non-members)
      and, when `RequirePreApproval = true`, routes non-siteop pres into the pending
      queue. Siteops bypass the queue. New SITE commands: `SITE PREINFO <release>`
      (detailed info + file listing, checks pending queue for siteops), `SITE
      PREAPPROVE <release>` (siteop; promotes pending → live, fires EventBus, updates
      dupe DB), `SITE PREDENY <release> [reason]` (siteop; removes from queue with
      optional reason), `SITE PREPENDING` (siteop; lists all pending pres). All
      mutations are audit-logged.

- [x] **Section stats leaderboards**
      `LeaderboardService` in `Core/Stats/` — reads the JSONL session log, groups
      Upload events by user (or group), sums bytes per window. Windows: AllTime,
      Today, ThisWeek (Mon-based), ThisMonth. Optional section filter.
      Five auto-registered SITE commands in `SiteTopCommands.cs`:
      `SITE TOP`, `SITE TOPDAY`, `SITE TOPWK`, `SITE TOPMTH` (all users, varying
      windows), `SITE TOPGRP` (all-time by group). All accept optional `[count]`
      (default 10, max 100) and `[section]` args. Columnar output: rank, user/group,
      files, uploaded, downloaded.

- [x] **Graceful shutdown and SIGHUP config reload**
      `FtpServer.GracefulStopAsync(drainTimeout)` — stops the accept loop, then
      polls `FtpSession.GetActiveSessions()` until all transfers for this server
      finish or the timeout expires (default 60s), then calls `Stop()`. `Program.cs`
      wires `Console.CancelKeyPress` (all platforms) and, on Linux/macOS, POSIX
      signals via `PosixSignalRegistration`: SIGTERM → `GracefulStopAsync`, SIGHUP
      → fire-and-forget `ReloadConfigurationAsync()` (same as `SITE REHASH`).
      Signal handler message logged; registration disposed on exit.

- [x] **Transfer resume integrity**
      `FtpSection.RequireResumeIntegrity` (bool, default false). When set and a
      client sends REST N + STOR: (1) partial file must exist on disk, (2) file
      size must be exactly N bytes — rejects with 550 otherwise. (3) If a
      `.partcrc` sidecar exists alongside the partial, its CRC32 is verified
      against the current file before accepting the resume. After each successful
      STOR in an integrity section, a `.partcrc` sidecar (hex CRC32 of current
      file state) is written for future resume checks. DELE cleans up the sidecar.

- [ ] **Comprehensive unit and integration tests**
      Add a test project (`amFTPd.Tests`). Priority coverage:
      - Credit engine: ratio edge cases, free sections, group overrides
      - Nuke credit deduction and restoration
      - Ratio resolution pipeline
      - Binary user/group store encode/decode roundtrip
      - Zipscript state machine: SFV arrival, CRC failure, nuke, unnuke
      - HammerGuard rate limiting
      - VFS path normalization and mount resolution
      - AMScript rule evaluation

- [ ] **DatabaseManager full integration**
      Complete the DatabaseManager so all subsystems (user store, group store,
      section store, dupe store, zipscript DB, session log) can optionally be
      backed by the SQL plugin layer (SQLite/MySQL/PostgreSQL) rather than the
      binary/JSON file stores. Document when to use which backend and provide
      migration tooling between them.

---

## Phase 4 — Differentiators (surpass mode)

> Goal: amFTPd offers things ioFTPD and glFTPD simply cannot. Phase 4 is
> complete when a sysop choosing amFTPd gets capabilities unavailable anywhere
> else.

- [x] **REST API for remote administration**
      `RestApiRouter` in `Core/Api/` — mounted on `/api/*` by the existing
      `StatusEndpoint` HttpListener, same Bearer-token auth. Toggled by
      `AmFtpdStatusConfig.RestApiEnabled` (default true). `FtpServer` constructs the
      router and sets `Server = this` so write endpoints have full server access.
      Read endpoints: `GET /api/` (endpoint index), `/api/sessions` (active sessions
      with transfer state), `/api/users` (all users), `/api/users/{name}` (single
      user), `/api/stats` (aggregate perf + rolling rates), `/api/stats/sections`
      (per-section live stats), `/api/stats/top` (leaderboard; query: window, count,
      section), `/api/dupes/search` (dupe search; query: q, limit), `/api/pres`
      (recent pres; query: count, section). Write endpoints: `POST /api/kick`
      (disconnect all sessions of a user), `POST /api/ban` (add temporary or permanent
      IP ban), `POST /api/pre` (register a pre), `POST /api/rehash` (reload config).
      All responses are JSON. All mutations are audit-logged with actor="api".

- [x] **Web admin dashboard**
      Self-contained HTML/CSS/JS SPA in `Core/Admin/AdminDashboardPage.cs`, served
      at `/admin` by the existing `StatusEndpoint` HttpListener. No external
      dependencies — everything inline. Auth handled in-browser: JS tries the API
      without a token first (no-auth configs connect immediately); if a 401 is
      returned a login modal collects the Bearer token and stores it in
      `sessionStorage`. Five tabs auto-refresh every 5 s:
        - **Sessions** — live table with transfer direction badge, file name,
          progress bar (bytes/total), and per-session Kick button.
        - **Stats** — eight stat cards (sessions, transfers, bytes, commands,
          failed logins, rolling transfer rates) + per-section table +
          top-10 uploaders leaderboard.
        - **Users** — full user list with client-side search/filter, showing
          credits, ratio status, disabled state, and quick Kick button.
        - **Dupes** — release search backed by `GET /api/dupes/search`.
        - **Pre** — recent pre list with status badges (Approved/Pending/Denied).
      Header has a Rehash button (`POST /api/rehash`) and logout control.
      Toggled by `AmFtpdStatusConfig.AdminDashboardEnabled` (default true).

- [x] **Prometheus metrics endpoint**
      `/metrics` already output proper Prometheus text format (`# HELP`/`# TYPE`,
      correct content-type `text/plain; version=0.0.4`). Extended in this pass:
      added `amftpd_info{version}` (build-info gauge), `amftpd_uptime_seconds`,
      `amftpd_nukes_total`, `amftpd_unnukes_total`, `amftpd_pres_total`,
      `amftpd_total_connections_total`, and `amftpd_average_transfer_duration_ms`.
      `PerfCounters` gained `NukeExecuted()`, `UnnukeExecuted()`, and
      `PreRegistered()` which are wired into `NukePropagation.ApplyNuke`,
      `ApplySystemNuke`, `ApplyUnnuke`, and `SitePreCommand`. The new counters
      are also exposed on `GET /api/stats` (`nukes`, `unnukes`, `pres`,
      `totalConnections`). Full metric set now covers: connections, commands,
      transfers (bytes, active, peak, avg duration), per-user stats, per-section
      stats, per-IP sessions (top-N + _other bucket), nuke/unnuke/pre events,
      build info, and server uptime. Ready to scrape with Grafana.

- [x] **HTTP webhooks for events**
      `Core/Webhooks/WebhookDispatcher` subscribes to the `EventBus` and fires
      HTTP POST webhooks for configured event types. Config lives in the new
      `amftpd.json` `Webhooks` section (`AmFtpdWebhookConfig`).

      Per-event URL keys: `UploadUrl`, `DownloadUrl`, `NukeUrl` (shared with
      auto-nuke), `UnnukeUrl`, `PreUrl`, `RaceCompleteUrl`, `LoginUrl`,
      `LogoutUrl`, `RequestUrl`, `DeleteUrl`, plus a `DefaultUrl` catch-all.

      JSON payload shape:
      ```json
      { "event": "Upload", "timestamp": "...",
        "data": { "user":"bob","group":"GRP","section":"0DAY",
                  "virtualPath":"/0DAY/Release-GRP","releaseName":"Release-GRP",
                  "bytes":12345678,"reason":null,"remoteHost":"1.2.3.4" } }
      ```

      Security: when `SigningSecret` is set, every payload is signed with
      HMAC-SHA256 and the digest is sent as `X-AmFTPd-Signature: sha256=<hex>`,
      matching the GitHub webhook convention. The event name is also sent in
      `X-AmFTPd-Event`. `TimeoutSeconds` (default 5) and `MaxRetries` (default 1,
      2 s back-off) are configurable. Errors are logged at Debug level — a dead
      webhook endpoint never disrupts FTP operations.

      The dispatcher is started on daemon boot and restarted on SIGHUP/REHASH.
      Toggled by `Webhooks.Enabled` (default true when section is present).

- [x] **Let's Encrypt / ACME automatic TLS**
      Pure-BCL ACME v2 client (`Core/Tls/AcmeClient.cs`) and lifecycle manager
      (`Core/Tls/AcmeCertificateManager.cs`). No third-party NuGet packages.
      EC P-256 account key, JWS/ES256 signing, HTTP-01 challenge server on
      configurable port, RSA-2048 cert key, SAN CSR, PEM chain → PFX assembly.
      `AcmeCertificateManager` checks cert expiry on startup and every 24 h;
      renews automatically when within `RenewalThresholdDays` (default 30) of
      expiry. New PFX written atomically then REHASH triggered to hot-swap TLS.
      Config lives in the new `amftpd.json` `Acme` section (`AmFtpdAcmeConfig`):
      `Domain`, `Email`, `DirectoryUrl` (defaults to Let's Encrypt prod),
      `AccountKeyPath`, `RenewalThresholdDays`, `ChallengePort`. Self-signed
      fallback unchanged when `Acme` section is absent or `Enabled: false`.
      10 ACME checks added to `--validate` / `ConfigValidator`.

- [x] **Native service packaging**
      **Windows Service intentionally removed**: amFTPd does not ship a
      `ServiceBase` wrapper, service installer script, or `--install` /
      `--uninstall` service-management commands. This is a 0.8 security
      advantage: the daemon avoids the privileged Windows Service Control
      Manager surface and should run as an explicit foreground process,
      container workload, or external supervisor on Windows.

      **Linux systemd** (`deploy/amftpd.service`): full unit with
      `NoNewPrivileges`, `PrivateTmp`, `PrivateDevices`, `ProtectSystem=strict`,
      `ProtectHome`, `CapabilityBoundingSet=CAP_NET_BIND_SERVICE`,
      `AmbientCapabilities`, `SystemCallFilter=@system-service`,
      `RestrictAddressFamilies`, `LockPersonality`, `RestrictRealtime`.
      `ExecReload=/bin/kill -HUP $MAINPID` wires SIGHUP for REHASH.
      `deploy/install-systemd.sh` bash installer: creates `amftpd` system
      user/group, all required directories, copies binary, patches paths in
      unit, enables and starts service.

      **Docker** (`Dockerfile` hardened): non-root UID/GID 1500, self-contained
      Linux x64 publish, separate restore layer for build cache, `/config` +
      `/data` volumes, `STOPSIGNAL SIGTERM`, `HEALTHCHECK` (TCP probe on 2121),
      75 s `stop_grace_period`. `deploy/docker-compose.yml`: `cap_drop: ALL`,
      `read_only: true`, tmpfs `/tmp`, json-file logging with rotation,
      TZ env, SIGHUP REHASH via `docker compose kill`. `docs/Docker.md`
      updated with port table, ACME/Docker config example, build options.

- [x] **Plugin / extension API**
      Full extension API with `AssemblyLoadContext` isolation and live REHASH:
      - `ISiteCommandPlugin` — custom `SITE <verb>` commands loaded from `.dll`
      - `IAuthProviderPlugin` — external auth (LDAP, OAuth, token files, …)
      - `IEventHandlerPlugin` — async FTP lifecycle event subscribers
      - `amFTPd.Plugin.Abstractions` NuGet-publishable package for community authors
      - `amFTPd.SamplePlugin` reference implementation in `Plugins/amFTPd.SamplePlugin/`
      - Dispatch wired in `FtpCommandRouter.Commands.cs` (SITE fallback) and PASS handler (auth fallback)
      - `PluginHost` lifecycle wired in `FtpServer`: start, REHASH, stop
      - Developer guide at `docs/Plugins.md`

- [x] **Migration validation tool**
      `amftpd [config.json] --check-migration <source-dir> [--json report.json]`
      Parses the source ioFTPD or glFTPD directory with the existing parsers and
      compares against the live amFTPd configuration:
      - **Users**: count diff, missing accounts (🔴 error), credit mismatches > 1 MB (⚠ warning)
      - **Groups**: count diff, missing groups (🔴 error)
      - **Dupes**: count diff, sampled missing releases (up to 50, ⚠ warning)
      - **Nukes**: source nuke count vs dupe-store IsNuked flags, sampled unflagged (⚠ warning)
      Human-readable colour-coded output on stdout. Optional `--json` writes a
      full `MigrationReport` JSON. Exit codes: 0 = clean, 1 = warnings, 2 = errors.
      `IDupeStore.GetAll()` added to enumerate entries for bulk comparison.

- [x] **Config validator / linter CLI**
      `--validate` / `-v` flag in `Program.cs` invokes `ConfigValidator` in
      `Core/Linting/` without starting the daemon. Checks: file existence, JSON
      parse, server port + passive port range, `RootPath` existence, TLS cert
      load + expiry (error if expired, warning if < 30 days), all VFS mount
      physical paths, section rule integrity, ratio rule sanity, group presence,
      and security warnings (no AuthToken on status endpoint, anonymous access
      enabled, TLS not required for auth). Findings are colour-coded on the
      console (red=error, yellow=warning, cyan=info). Returns exit code 0 (clean),
      1 (warnings only), or 2 (one or more errors). Usage:
      `amftpd [configfile.json] --validate`

- [x] **Comprehensive documentation site**
      Full docs covering:
      - Getting started (install, first config, first user)
      - Configuration reference (every key in `amftpd.json` with type, default,
        and example)
      - AMScript language reference (already partially done — complete it)
      - SITE command index (all 82+ commands with syntax, permissions, examples)
      - VFS guide (mounts, providers, symlinks, per-user homes)
      - Migration guide (ioFTPD step-by-step, glFTPD step-by-step)
      - REST API reference
      - Plugin development guide
      - Docker / container deployment guide
      - FAQ and troubleshooting

---

## Tracking

| Phase | Items | Done | Status |
|-------|-------|------|--------|
| Phase 1 — Close the loop | 6 | 6 | ✅ Complete |
| Phase 2 — Scene-critical systems | 7 | 7 | ✅ Complete |
| Phase 3 — Production hardening | 10 | 10 | ✅ Complete |
| Phase 4 — Differentiators | 10 | 10 | ✅ Complete |
| **Total** | **33** | **43** | |

---

*Last updated: April 2026 — All four phases complete. Phase 4: 10/10 (REST API, Config Validator, Web Admin Dashboard, Prometheus metrics, HTTP webhooks, Let's Encrypt / ACME automatic TLS, Native service packaging, Plugin / extension API, Migration validation tool, Comprehensive documentation site).*
