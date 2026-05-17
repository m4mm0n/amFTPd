# amFTPd Scene FTPD Replacement Gap Report

Date: 2026-05-02

This report compares the current amFTPd codebase against the practical replacement surface expected from glFTPd and ioFTPD style scene sites.

## Current Replacement Coverage

- Core FTP protocol: USER/PASS, TLS, PASV/EPSV, PORT/EPRT, LIST/MLSD/MLST, RETR, STOR, APPE, REST, RNFR/RNTO, DELE, MKD/RMD, SITE command routing.
- Scene administration: users, flags, limits, credits, groups, sections, nukes, unnukes, wipes, move, request/oneliner/affil systems.
- Release workflow: PRE, pending PRE approval, dupe store, race engine, zipscript/SFV status, auto-nuke hooks, rescan commands.
- Compatibility/migration: glFTPd and ioFTPD import parsers, migration validator, dupe import/export, command alias support.
- Operations: config validation, Docker/systemd deployment assets, HTTP status/admin endpoint, audit log, xferlog, session log. Windows Service mode is intentionally absent to avoid a privileged service-management attack surface.

## Gaps Closed In This Pass

- `SITE GROUPADD` and `SITE GROUPDEL` no longer return placeholder 502 responses.
- `SITE RSCHECK` now reports release health from zipscript state.
- Missing/disabled IDENT config no longer breaks control-session startup.
- Published EXE startup now sends an FTP `220` banner immediately.
- Release logging now includes warnings/errors in Release builds.
- Treasure Cove integration coverage exercises multi-section PRE flow using fake scene groups.

## Remaining Drop-In Risks

- Multi-client same-release racing needs a dedicated concurrent STOR test with race stats and zipscript consistency assertions.
- REST resume needs a corruption/retry test that proves bad partial state is rejected and good partial state resumes.
- FXP, active mode, passive mode, TLS data protection, and rule denial need matrix coverage.
- Migration tests need representative glFTPd and ioFTPD fixture data, not just parser unit coverage.
- IRC announce integration still needs a real IRC server smoke test.
- Long-running soak/restart persistence still needs repeated login/upload/download/PRE/NUKE cycles.

## Treasure Cove Test Site

The automated test site is named `The Treasure Cove`. It currently includes these fake scene groups and PRE sections:

- `Razor1911` -> `TCV0DAY`
- `FairLight` -> `TCVGAMES`
- `CLASS` -> `TCVAPPS`
- `HOODLUM` -> `TCVMP3`
- `DEViANCE` -> `TCVTV`

Each group has a matching test user and can queue PRE into its assigned section. The admin account approves the pending PREs and verifies `PREINFO`.

## Verdict

amFTPd is no longer just a feature list. It now has executable coverage for more of the scene workflow. It is still not ready to replace a serious glFTPd/ioFTPD production site until the remaining drop-in risks above are tested and fixed.
