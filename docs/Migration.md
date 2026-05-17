# Migrating to amFTPd

amFTPd includes a first-class import pipeline for ioFTPD and glFTPD. It migrates users, groups, dupe databases, nuke records, and pre history — and provides a post-migration validation tool to confirm data integrity before you decommission the old server.

---

## Supported sources

| Source | Detected by |
|---|---|
| **ioFTPD** | Presence of `ioFTPD.ini` or `ioFTPD.conf` in the source directory |
| **glFTPD** | Presence of `glftpd.conf` or an `ftp-data/` subdirectory |

Auto-detection runs before every import and validation operation. You can override with `--flavor io` or `--flavor gl` when the heuristic fails (e.g. a partial archive copy).

---

## Migration checklist

1. Install and configure amFTPd with an empty user store (see [GettingStarted.md](GettingStarted.md)).
2. Run a dry-run import to preview what will be migrated.
3. Shut down the legacy server (or take it offline).
4. Run the live import.
5. Run `--check-migration` to validate data integrity.
6. Start amFTPd in production and verify logins.

---

## Step 1 — Install and configure amFTPd

Follow [GettingStarted.md](GettingStarted.md) to create a minimal `amftpd.json`. You do **not** need to create any users manually — the import will create them.

For the binary user store, set a `MasterPassword` in `Storage` before importing. Changing it after import requires re-exporting and re-importing.

---

## Step 2 — Dry run (preview)

The import command supports `--dry-run` which parses everything, prints a summary, and writes nothing:

```bash
# ioFTPD example
amftpd amftpd.json --import /opt/ioftpd --dry-run

# glFTPD example
amftpd amftpd.json --import /glftpd --dry-run

# Force flavor detection
amftpd amftpd.json --import /backup/site --flavor io --dry-run
```

Output shows the number of users, groups, dupes, nukes, and pres that would be imported, along with any warnings (duplicate names, unknown groups, missing files).

You can also restrict which subsystems are imported:

```bash
amftpd amftpd.json --import /opt/ioftpd --users --groups --dry-run
amftpd amftpd.json --import /opt/ioftpd --dupes --dry-run
```

Available subset flags: `--users`, `--groups`, `--dupes`, `--nukes`.

---

## Step 3 — Shut down the legacy server

Stop ioFTPD or glFTPD cleanly before importing to avoid partial writes in the source data files. A running server may flush user records after you've already read them.

For glFTPD on Linux:
```bash
sudo systemctl stop glftpd
# or: kill $(cat /var/run/glftpd.pid)
```

For ioFTPD on Windows:
```powershell
sc stop ioFTPD
```

---

## Step 4 — Live import

Run the import without `--dry-run`:

```bash
amftpd amftpd.json --import /opt/ioftpd
```

The import engine:

1. Detects the source flavor automatically.
2. Parses user records and maps them to amFTPd `FtpUser` objects. Password hashes are preserved where the algorithm matches; plain-text passwords in gl format are bcrypt-rehashed.
3. Derives group membership from user records and creates any groups not already present in the target store.
4. Imports dupe database entries. Duplicate releases (same section + name) are skipped.
5. Imports nuke records, setting the `IsNuked` flag on matching dupe entries.
6. Imports pre history where available.

Progress is written to stdout. A structured JSON progress report is also available via `SITE IMPORTSTATUS` or `SITE IMPORTSTATUSJSON` from any open FTP session while the import is running.

### Resuming / re-running

The import is **idempotent** for users and groups — re-running will not create duplicate accounts. For dupes, existing records with the same key are skipped. You can safely re-run to pick up records that failed the first time.

To cancel a running import:
```bash
SITE IMPORTCANCEL   # from an FTP session
```

---

## Step 5 — Post-migration validation

The `--check-migration` CLI mode compares the source data against the running amFTPd stores and produces a structured report:

```bash
# Human-readable to stdout
amftpd amftpd.json --check-migration /opt/ioftpd

# JSON report to file
amftpd amftpd.json --check-migration /opt/ioftpd --json migration-report.json
```

### Exit codes

| Code | Meaning |
|---|---|
| `0` | All counts match — migration is clean |
| `1` | Warnings only (credit mismatches, missing dupe entries) |
| `2` | Errors (missing users or groups — data loss) |

### Report sections

**Users** — source count vs. target count, list of any usernames missing in amFTPd, and credit mismatches larger than 1 MiB.

**Groups** — source count vs. target count, list of group names missing in amFTPd. If no dedicated group store is present, groups are derived from user `PrimaryGroup` / `SecondaryGroups` fields.

**Dupes** — source count vs. target count, up to 50 sample missing entries (section + release + group).

**Nukes** — source nuke record count vs. entries with `IsNuked = true` in the dupe store, up to 50 sample entries missing the nuke flag.

### Example output

```
[Migration] Validating IoFtpd import from '/opt/ioftpd'...
[Migration] Validation complete. Status: Ok.
IoFtpd → amFTPd [all counts match] — users: 1247/1247 (missing 0),
groups: 18/18 (missing 0), dupes: 94823/94823 (missing 0),
nukes: 312/312 (unflagged 0)
```

---

## Subsystem import reference

### Users (ioFTPD)

Source files read: `users/<username>` (one per user, INI-style).

Fields migrated: username, password hash (MD5 or bcrypt), primary group, secondary groups, flags, credits, IP masks, IDENT, and ratio settings.

### Users (glFTPD)

Source files read: `ftp-data/users/<username>`.

Fields migrated: username, password (blowfish-encrypted, converted to bcrypt), group, flags, credits, IP masks.

### Groups (ioFTPD / glFTPD)

Groups are derived from user records if no explicit group files exist. When group files are present (`ftp-data/groups/<group>` for gl), description and slot count are also imported.

### Dupe database (ioFTPD)

Source: `dupelog` file in the ioFTPD directory. Format: one record per line, fields separated by spaces — `section release group bytes date`.

### Dupe database (glFTPD)

Source: `ftp-data/logs/dupefile`. Same space-separated format.

### Nuke records (ioFTPD)

Source: `logs/nuke.log`. Each line carries a virtual path, section, reason, multiplier, and user.

### Nuke records (glFTPD)

Source: `ftp-data/logs/nukelog`. Same fields, different delimiter.

---

## Migrating configuration manually

amFTPd does not auto-convert `ioFTPD.ini` or `glftpd.conf` to `amftpd.json` because the configuration models differ significantly. Use [Configuration.md](Configuration.md) to create your `amftpd.json` and refer to the table below for the most common mappings.

### ioFTPD → amFTPd key mapping

| ioFTPD setting | amFTPd equivalent |
|---|---|
| `[FTP_Server] Port` | `Server.Port` |
| `[FTP_Server] PASV_PORTS` | `Server.PassivePortStart` / `PassivePortEnd` |
| `[FTP_Server] AllowFXP` | `Server.AllowFxp` |
| `[SSL_Core] Certificate_File` | `Tls.PfxPath` |
| `[Sections] <name> = <path>` | `Vfs.Mounts` + `Sections` entry |
| `[Ratio] Default_Ratio` | `RatioRules.<name>.Ratio` |
| `[IDENTD] Mode` | `Ident.Modes` |

### glFTPD → amFTPd key mapping

| glFTPD setting | amFTPd equivalent |
|---|---|
| `port_range <start> <end>` | `Server.PassivePortStart` / `PassivePortEnd` |
| `allow_fxp yes` | `Server.AllowFxp = true` |
| `sitepath <name> <path>` | `Vfs.Mounts` + `Sections` entry |
| `ratio <n>` | `RatioRules.<name>.Ratio` |
| `max_users <n>` | `Server.MaxUsers` (session limit) |
| `identcheck yes` | `Ident.Modes: "Standard"` |

---

## Tips and known limitations

**Password hashing** — ioFTPD stores passwords as unsalted MD5. These are imported verbatim but amFTPd will automatically re-hash to bcrypt on the user's next successful login. Users log in normally; the upgrade is transparent.

**glFTPD blowfish passwords** — glFTPD uses a custom blowfish encryption scheme. The importer converts these to bcrypt during import, requiring you to provide the site key (`--gl-sitekey <key>`) if the source installation used one.

**Credits** — Credits are stored in KiB in both ioFTPD and amFTPd. No conversion is needed.

**Homedir / VFS paths** — ioFTPD uses `[Directories]` to map sections to physical paths. amFTPd uses `Vfs.Mounts`. You must replicate the path mappings manually in `amftpd.json` before importing — otherwise uploads and downloads will resolve to the wrong physical locations.

**IRC scripts and TCL** — ioFTPD TCL scripts and glFTPD scripts are not imported. Implement equivalent functionality using amFTPd plugins or AMScript.

**XferLog** — The wu-ftpd-format xferlog is not imported (it is a write-only audit trail). Only the dupe database is used for structured release history.
