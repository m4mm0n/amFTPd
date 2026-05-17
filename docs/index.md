# amFTPd Documentation

**amFTPd** (a Managed FTP Daemon) is a modern, fully-featured Scene FTP server written in .NET 10. It is designed as a drop-in replacement and successor to ioFTPD and glFTPD, with full TLS support, a plugin API, a REST management interface, and hot-reload configuration.

---

## Getting started

| Document | What you'll find |
|---|---|
| [GettingStarted.md](GettingStarted.md) | Install on Linux, Windows, or Docker; create your first user; start the server |
| [Configuration.md](Configuration.md) | Every configuration key in `amftpd.json` with types, defaults, and examples |

---

## Core features

| Document | What you'll find |
|---|---|
| [VFS.md](VFS.md) | Virtual file system mounts, per-user mounts, virtual files, and symlinks |
| [IDENT.md](IDENT.md) | RFC 1413 IDENT lookup modes, enforcement flags, and group mapping |
| [SiteCommands.md](SiteCommands.md) | Full reference for all 100+ SITE commands with syntax and permission levels |

---

## Administration

| Document | What you'll find |
|---|---|
| [RestAPI.md](RestAPI.md) | REST API endpoints — sessions, users, stats, dupes, pres, kick, ban, rehash |
| [Plugins.md](Plugins.md) | Plugin developer guide — SITE command plugins, auth providers, event handlers |

---

## Migration

| Document | What you'll find |
|---|---|
| [Migration.md](Migration.md) | Step-by-step import from ioFTPD or glFTPD, post-migration validation |

---

## Reference & troubleshooting

| Document | What you'll find |
|---|---|
| [FAQ.md](FAQ.md) | Answers to common setup, TLS, login, transfer, and plugin questions |

---

## Quick links

**Starting fresh?** → [GettingStarted.md](GettingStarted.md)

**Migrating from ioFTPD or glFTPD?** → [Migration.md](Migration.md)

**Looking up a SITE command?** → [SiteCommands.md](SiteCommands.md)

**Configuring a specific JSON key?** → [Configuration.md](Configuration.md)

**Building a plugin?** → [Plugins.md](Plugins.md)

**Something broken?** → [FAQ.md](FAQ.md)

---

## Architecture overview

```
                    ┌─────────────────────────────────────────┐
                    │               amFTPd daemon              │
                    │                                          │
  FTP clients  ────►│  FtpServer / FtpCommandRouter           │
  (AUTH TLS)        │    │                                     │
                    │    ├── SITE commands ──► SiteCommandRegistry
                    │    ├── VFS layer     ──► VfsConfig / Mounts
                    │    ├── User store    ──► IUserStore (json|binary)
                    │    ├── Dupe store    ──► IDupeStore (file|binary)
                    │    ├── Plugin host   ──► IAmFtpdPlugin[]
                    │    └── Event bus     ──► IRC / Webhooks
                    │                                          │
  REST clients ────►│  StatusEndpoint / RestApiRouter          │
  Prometheus  ────►│  /metrics                                │
                    └─────────────────────────────────────────┘
```

amFTPd is a single self-contained binary. All subsystems (TLS termination, zipscript, dupe database, IRC announcer, webhook dispatcher, REST API, Prometheus metrics) run in-process. Plugins extend the server via isolated `AssemblyLoadContext` instances loaded at startup.

---

## Configuration at a glance

```json
{
  "Server":    { "BindAddress": "0.0.0.0", "Port": 21, ... },
  "Tls":       { "PfxPath": "certs/server.pfx", ... },
  "Storage":   { "UserStoreBackend": "binary", ... },
  "Ident":     { "Modes": "Standard,Caching" },
  "Vfs":       { "Mounts": [ { "VirtualPath": "/", ... } ] },
  "Sections":  { "MP3": { "RatioRuleName": "STANDARD" } },
  "RatioRules":{ "STANDARD": { "Ratio": 3.0 } },
  "Groups":    { "USERS": { "Description": "Standard users" } },
  "Status":    { "Enabled": true, "Port": 8080, "AuthToken": "..." },
  "Acme":      { "Enabled": true, "Domain": "ftp.example.com", ... }
}
```

See [Configuration.md](Configuration.md) for the complete reference.
