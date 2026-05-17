# amFTPd SITE Command Reference

SITE commands are issued as `SITE <verb> [arguments]` over the FTP control connection. All commands are case-insensitive.

Permission levels used in this document:

- **User** — any authenticated non-banned account
- **Siteop** — accounts with `IsSiteop = true` or `IsAdmin = true`
- **Admin** — accounts with `IsAdmin = true` only

Type `SITE HELP` for an inline list, or `SITE HELP <cmd>` for individual usage.

---

## User Management

### ADDUSER
**Siteop** · `ADDUSER <user> <password> [group] [homedir]`

Create a new user account. The optional `group` argument assigns a primary group; if omitted, the user is placed in `USERS`. `homedir` overrides the default VFS home.

### DELUSER
**Siteop** · `DELUSER <user>`

Soft-disable an account (sets `Disabled = true`). The account record is preserved. Use `UNSUSPEND` to re-enable, or `PURGE`-style operations to remove the record permanently.

### GADDUSER
**Siteop** · `GADDUSER <group> <user> <password>`

Create a new user and assign them directly to the specified group in a single operation.

### SHOWUSER
**Siteop** · `SHOWUSER <user>`

Display detailed account information: group membership, credits, flags, IP masks, ident requirements, transfer limits, and quota usage.

### USERS
**Siteop** · `USERS`

List all user accounts with their primary group and credit totals.

### SUSPEND
**Siteop** · `SUSPEND <user> [reason]`

Disable a user account (reversible). Active sessions are not terminated; the account cannot log in again until unsuspended.

### UNSUSPEND
**Siteop** · `UNSUSPEND <user>`

Re-enable a suspended account.

### CHPASS
**Siteop** · `CHPASS <user> <newpassword>`

Change a user's password. Users may also change their own password.

### CHGRP
**Siteop** · `CHGRP <user> <primary-group> [secondary1,secondary2,...]`

Change a user's primary group and optionally set a comma-separated list of secondary groups.

### FLAGS
**Siteop** · `FLAGS <user> [flags]`

Show the current flag string for a user, or replace it. Flags are single characters.

### SETFLAGS
**Siteop** · `SETFLAGS <user> <flags>`

Set the flag string for a user (replaces existing flags).

### SYSOP
**Admin** · `SYSOP <user> <on|off>`

Grant or revoke siteop privileges.

---

## IP / IDENT Management

### ADDIP
**Siteop** · `ADDIP <user> <ip-mask> [required-ident]`

Add an allowed IP mask for a user. Standard wildcards apply: `*` (any segment) and `?` (any single character). Optionally attach a required IDENT string to this mask.

### DELIP
**Siteop** · `DELIP <user> [ip-mask]`

Remove an IP mask from a user. With no mask argument, clears all masks.

### IDENT
**Siteop** · `IDENT <user> <ident>`

Set the global required IDENT string for a user. Supersedes individual mask-level IDENT requirements.

### REQIDENT
**Siteop** · `REQIDENT <user> <on|off> [ident]`

Enable or disable per-user mandatory IDENT verification. When enabling, optionally provide the expected IDENT string.

### BLOCK
**Siteop** · `BLOCK <ip_key> [reason]`

Permanently block a remote address. The `ip_key` is an IP or range key as displayed by `SITE BLOCKLIST`.

### UNBLOCK
**Siteop** · `UNBLOCK <ip_key>`

Remove an existing IP block.

### BLOCKLIST
**Siteop** · `BLOCKLIST`

Show the current IP block list with reasons and timestamps.

---

## Credits

### CREDITS
**User** · `CREDITS [user]`

Show your current credit balance. Siteops may pass a username to query another account.

### GIVECRED
**Siteop** · `GIVECRED <user> <kb>`

Award credits (in KiB) to a user.

### TAKECRED
**Siteop** · `TAKECRED <user> <kb>`

Deduct credits (in KiB) from a user.

### NORATIO
**Siteop** · `NORATIO <user> [ON|OFF]`

Show or toggle the no-ratio flag for a user. When enabled, the user can download freely regardless of their ratio.

---

## Transfer Limits

### SETLIMITS
**Siteop** · `SETLIMITS <user> <up-kbps> <down-kbps> <maxlogins> <idle-seconds>`

Set per-user speed limits (KiB/s), maximum simultaneous connections, and idle timeout. A value of `0` means unlimited.

### LIMITS
**Siteop** · `LIMITS [user]`

Display current limit settings for a user, or server-wide defaults if no user is specified.

### QUOTA
**User** · `QUOTA [user]`

Show daily, weekly, and monthly upload quota usage. Siteops may pass a username.

---

## Sessions

### WHO
**User** · `WHO`

List all active sessions showing username, group, current path, transfer state, speed, and elapsed time. Scene-standard two-column format.

### WHOIP
**Siteop** · `WHOIP [FULL|USERS|ip_xxxx]`

Show sessions grouped by remote IP. `FULL` includes all details; `USERS` shows only authenticated sessions; `ip_xxxx` filters to a specific host key.

### KICK
**Siteop** · `KICK <user>`

Disconnect all active sessions for the named user.

### KILL
**Siteop** · `KILL <session-id|user> [message]`

Terminate a specific session by ID or all sessions for a user, optionally delivering a disconnect message.

### UPTIME
**User** · `UPTIME`

Show how long the daemon has been running.

---

## Statistics

### STATS
**User** · `STATS [FULL|TOP <n>]`

Show aggregate server statistics: upload and download totals, active connections, section breakdown. `FULL` adds per-section detail. `TOP <n>` lists the top-N uploaders.

### STATDAILY
**User** · `STATDAILY [JSON]`

Upload/download totals for the last 24 hours. Append `JSON` for machine-readable output.

### STATWEEKLY
**User** · `STATWEEKLY [JSON]`

Upload/download totals for the last 7 days.

### STATMONTHLY
**User** · `STATMONTHLY [JSON]`

Upload/download totals for the last 30 days.

### STATSJSON
**User** · `STATSJSON`

Return the full statistics object as a JSON document.

### TOP
**User** · `TOP [count] [section]`

Top uploaders all-time. Optional `count` (default 10) and `section` filter.

### TOPDAY
**User** · `TOPDAY [count] [section]`

Top uploaders for today.

### TOPWK
**User** · `TOPWK [count] [section]`

Top uploaders this week (Monday–Sunday).

### TOPMTH
**User** · `TOPMTH [count] [section]`

Top uploaders this calendar month.

### TOPGRP
**User** · `TOPGRP [count] [section]`

Top groups by total uploaded bytes.

---

## Races

### RACE
**User** · `RACE [path]`

Show current race state for the release in the current or specified directory.

### RACELOG
**User** · `RACELOG [path]`

Show the per-file upload log for the current race.

### RACESTATS
**User** · `RACESTATS`

Aggregate per-racer statistics for the current directory's race.

### LASTRACES
**User** · `LASTRACES [count]`

Show the most recently completed races (default 10).

---

## Sections & Groups

### SECTIONS
**User** · `SECTIONS`

List all configured sections with their VFS root paths.

### GROUPS
**User** · `GROUPS`

List all configured groups with their descriptions.

### GROUPINFO
**User** · `GROUPINFO <group>`

Show detailed information about a group: description, ratio settings, upload quotas, and member count.

### GROUPMEMBERS
**User** · `GROUPMEMBERS <group>`

List all users whose primary group matches the given name.

### GROUPADD
**Admin** · `GROUPADD <group>`

Create a new group (only available when using a group store backend that supports mutations, e.g. binary).

### GROUPDEL
**Admin** · `GROUPDEL <group>`

Delete an existing group. Users in the group are not automatically reassigned.

---

## Nukes

### NUKE
**Siteop** · `NUKE <path> <multiplier> <reason>`

Nuke a release at the given virtual path. Credits are deducted from uploaders according to the multiplier. The release directory is renamed `[NukeMultiplier]x-REASON-ReleaseName`.

### UNNUKE
**Siteop** · `UNNUKE <path> [reason]`

Undo a nuke, restore the original directory name, and refund deducted credits.

---

## Pre System

### PRE
**Siteop** · `PRE <section> <release>`

Register a release in the dupe database and broadcast an IRC announce.

### PREINFO
**User** · `PREINFO <release>`

Query the dupe database for information about a specific release: date, section, group, size.

### PRELIST
**User** · `PRELIST [count] [section]`

List recent pre registrations (default 20). Optionally filter by section.

### PREAPPROVE
**Siteop** · `PREAPPROVE <pending-id>`

Approve a pre that was submitted but is awaiting approval (e.g. in sections where pre requires siteop sign-off).

### PREDENY
**Siteop** · `PREDENY <pending-id> [reason]`

Deny a pending pre submission.

### PREPENDING
**Siteop** · `PREPENDING`

List pre submissions currently awaiting approval.

### DELPRE
**Siteop** · `DELPRE <release> [section]`

Remove a pre entry from the dupe database.

---

## Dupe Database

### DUPE
**User** · `DUPE <pattern> [section]`

Search the dupe database for releases matching the given glob or keyword pattern.

### SEARCH
**User** · `SEARCH <pattern> [section]`

Alias for `DUPE`. Returns matching releases in human-readable format.

### DUPEFULL
**Siteop** · `DUPEFULL [section]`

Dump all dupe database records, optionally filtered by section.

### DUPEJSON
**Siteop** · `DUPEJSON <pattern>`

Return dupe search results as a JSON array.

### UNDUPE
**Siteop** · `UNDUPE <release> [section]`

Remove a specific release from the dupe database.

### UNDUPEDIR
**Siteop** · `UNDUPEDIR <path>`

Remove all dupe entries whose virtual path prefix matches the given path.

### DUPEEXPORT
**Admin** · `DUPEEXPORT <output-path>`

Export the entire dupe database to a JSON file at the specified path.

### DUPEIMPORT
**Admin** · `DUPEIMPORT <input-path>`

Import dupe records from a JSON file.

### DUPEMIGRATE
**Admin** · `DUPEMIGRATE <source-dir> [--flavor io|gl]`

Migrate dupe records from an ioFTPD or glFTPD data directory into the amFTPd dupe store.

---

## SFV / Zipscript

### SFV
**User** · `SFV [path]`

Show the zipscript CRC verification status for the release in the current or specified directory: files present, files checked, bad CRC count.

### RESCAN
**Siteop** · `RESCAN <path>`

Trigger a zipscript rescan of the release at the given path.

### RSCHECK
**User** · `RSCHECK <path>`

Check the current rescan / CRC-verification state without triggering a new scan.

### RESCANDUPE
**Admin** · `RESCANDUPE [--dry-run]`

Rescan all dupe database entries against the VFS to remove stale records for releases that no longer exist on disk.

### RESCANNUKES
**Admin** · `RESCANNUKES`

Rescan nuke records in the dupe store to ensure all flagged entries still have corresponding release directories.

### RESCANSTATS
**Admin** · `RESCANSTATS`

Recompute section and user statistics by walking the VFS and summing actual file sizes.

---

## Virtual File System

### MKDIR
**User** · `MKDIR <virt-path>`

Create a directory in the VFS. Equivalent to the standard FTP `MKD` command.

### MOVE
**Siteop** · `MOVE <src> <dst>`

Move or rename a file or directory within the VFS.

### WIPE
**Siteop** · `WIPE <virt-path>`

Delete a file or directory (non-recursive). For directory trees use `PURGE`.

### PURGE
**Siteop** · `PURGE <virt-path>`

Recursively delete a directory tree.

### CHMOD
**Siteop** · `CHMOD <octal-mode> <virt-path>`

Change the Unix permission bits of a VFS path on the underlying file system.

### LINK
**Siteop** · `LINK <target> <link-path>`

Create a VFS symlink at `link-path` pointing to `target`.

### UNLINK
**Siteop** · `UNLINK <link-path>`

Remove a VFS symlink.

### LINKS
**Siteop** · `LINKS [prefix]`

List registered VFS symlinks, optionally filtered by path prefix.

### DIRFLAGS
**User** · `DIRFLAGS [virt-path]`

Show the effective ratio and access flags that apply to the current or specified directory based on `DirectoryRules`.

---

## Oneliners (Shoutbox)

### LINES
**User** · `LINES [count]`

Display the last N oneliners (default 10).

### ADDLINE
**User** · `ADDLINE <message>`

Post a message to the shoutbox.

### DELLINE
**Siteop** · `DELLINE <id>`

Delete a specific oneliner by its ID.

### WIPELINES
**Siteop** · `WIPELINES`

Delete all oneliners.

---

## Requests

### REQUEST
**User** · `REQUEST <releasename>`

Post a release request visible to all users.

### REQLIST
**User** · `REQLIST [filter]`

List open requests. Optional keyword filter narrows results.

### REQFILLED
**Siteop** · `REQFILLED <releasename>`

Mark a matching request as filled.

### REQDEL
**Siteop** · `REQDEL <id>`

Delete a request by its numeric ID.

### REQWIPE
**Admin** · `REQWIPE`

Delete all requests (open and filled).

---

## Affiliates

### AFFIL
**Siteop** · `AFFIL <section> <group>`

Affiliate a release group with a section.

### DEAFFIL
**Siteop** · `DEAFFIL <section> <group>`

Remove a group's affiliation with a section.

### AFFILS
**User** · `AFFILS [section]`

List affiliated groups for all sections, or for a specific section.

---

## Administration

### REHASH
**Admin** · `REHASH`

Reload `amftpd.json` from disk without restarting the server. Active sessions are not interrupted. Equivalent to sending `SIGHUP` on Linux.

### AUDITLOG
**Siteop** · `AUDITLOG [count] [user]`

Show recent administrative mutations (user adds, password changes, nukes, bans, etc.). Optional `count` (default 50) and user filter.

### LOG
**Admin** · `LOG <STATUS|EVERYTHING|SOMETHING|QUIET>`

Inspect or change QuickLog verbosity without restarting the daemon. `EVERYTHING` records trace/debug/info/warn/error/critical messages, `SOMETHING` records info/warn/error/critical messages, and `QUIET` records only error/critical plus lifecycle/crash records.

### DBBACKUP
**Admin** · `DBBACKUP`

Flush all stores to disk and write a point-in-time backup archive.

### DBFSCK
**Admin** · `DBFSCK`

Run integrity checks on all on-disk stores and report any inconsistencies.

### DBSUMMARY
**Admin** · `DBSUMMARY`

Print record counts and size statistics for all active store backends.

---

## Migration / Import

### IMPORT
**Admin** · `IMPORT <source-dir> [--flavor io|gl] [--dry-run] [--users] [--groups] [--dupes] [--nukes]`

Import users, groups, dupes, and nukes from an ioFTPD or glFTPD installation. Pass `--dry-run` to preview without writing.

### IMPORTDUPE
**Admin** · `IMPORTDUPE <source-dir> [--flavor io|gl]`

Import dupe records only (subset of `IMPORT`).

### IMPORTSTATUS
**Admin** · `IMPORTSTATUS`

Show the status and progress of a running import operation.

### IMPORTSTATUSJSON
**Admin** · `IMPORTSTATUSJSON`

Return import status as a JSON document.

### IMPORTCANCEL
**Admin** · `IMPORTCANCEL`

Abort the current import operation.

---

## Diagnostics

### SQLPROVIDERS
**Admin** · `SQLPROVIDERS`

List the registered SQL/database provider backends and their connection states.

### SQLTEST
**Admin** · `SQLTEST`

Run a connectivity test against all configured SQL providers.

---

## Information

### HELP
**User** · `HELP [cmd]`

Show a list of all SITE commands visible to your account, or detailed usage for a specific command.

### VERSION
**User** · `VERSION`

Show the amFTPd version number, build date, and .NET runtime version.

### VERS
**User** · `VERS`

Short version output compatible with ioFTPD-style client scripts.

---

## Compatibility Aliases

When `Compatibility.EnableCommandAliases` is `true`, the following legacy SITE verbs are accepted and silently mapped to their amFTPd equivalents:

| Alias | Maps to |
|---|---|
| `ADDHOST` | `ADDIP` |
| `DELHOST` | `DELIP` |
| `CHOWN` | `CHGRP` |
| `BAN` | `BLOCK` |
| `UNBAN` | `UNBLOCK` |
| `MSG` | `ADDLINE` |
| `NUKECHECK` | `NUKE` (read-only path) |

Custom aliases can be defined in the `Compatibility.SiteCommandAliases` map in `amftpd.json`.
