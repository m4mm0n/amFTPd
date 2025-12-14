# AMScript Engine README

The AMScript engine is a tiny, hot‑reloaded rules language that lets you change amFTPd behavior without recompiling. This guide explains how to install the rule files, which contexts exist, how to write rules, and how to debug them. The goal is to be GitHub‑friendly and self contained so you can download it directly if you only need scripting docs.

## Quick start

1. Ensure the daemon ships with the default rule set by running amFTPd once; it will create a `config/rules` directory and populate baseline `.msl` files.
2. Optional: add `config/scripts.json` to point to a custom rules directory:

   ```json
   {
     "RulesPath": "config/rules"
   }
   ```

3. Edit any `.msl` file. Saving a file triggers an automatic reload—no restart required.
4. Use the examples below as templates for credit rules, FXP/active mode policies, SITE handling, and login enforcement.

## Where scripts live

- **Config file**: `config/scripts.json` (optional) sets `RulesPath`. Relative paths are resolved from the daemon base directory.
- **Default location**: if `scripts.json` is missing, rules load from `config/rules` next to the daemon.
- **Bootstrap**: on startup, `AMScriptDefaults.EnsureAll` guarantees the folder exists and that every core file is present:
  - `credits.msl`
  - `fxp.msl`
  - `active.msl`
  - `section-routing.msl`
  - `site.msl`
  - `user-rules.msl`
  - `group-rules.msl`
  - placeholders: `section-rules.msl`, `sections.msl`, `speed.msl`, `messages.msl`

You are free to delete or reorder the rules inside a file; only the filename must exist.

## Hot reload behavior

Each `.msl` file is watched with `FileSystemWatcher`. When you save:

- The file is reloaded after a short debounce.
- Syntax errors only disable that file’s rules; the daemon keeps running.
- Optional debug logging writes `[AMScript] ...` lines to the FTP log.

## Language basics

AMScript is deliberately small: every rule is a single‑line `if` that pairs a condition with an action.

```text
if (<condition>) <action>;
```

Comments start with `#`; blank lines are ignored.

### Conditions

- Logical operators: `&&`, `||`, `!`
- Equality: `==`, `!=` (string compare)
- String literals use double quotes (no escaping in v1)

Examples:

```text
if ($user.group == "VIP" && !$freeleech) ...
if ($is_fxp || $section == "ARCHIVE") ...
```

### Variables (shared core)

| Variable          | Type   | Meaning                                   |
|-------------------|--------|-------------------------------------------|
| `$is_fxp`         | bool   | Transfer is FXP                           |
| `$section`        | string | Section name (`"0DAY"`, `"default"`, etc.)|
| `$freeleech`      | bool   | Section is marked free‑leech              |
| `$user.name`      | string | Username                                  |
| `$user.group`     | string | Group name (empty if none)                |
| `$bytes`          | number | Bytes in the payload                      |
| `$kb`             | number | Kilobytes in the payload                  |
| `$cost_download`  | number | Current download cost (credits context)   |
| `$earned_upload`  | number | Current upload earnings (credits context) |
| `$virtual_path`   | string | Virtual path seen by the user             |
| `$physical_path`  | string | Physical path resolved on disk            |
| `$event`          | string | Event name for SITE hooks                 |

### Actions

Return actions short‑circuit evaluation:

- `return allow;`
- `return deny "message";`
- `return output "text";` *(SITE responses)*
- `return override;` *(tell SITE handler you already handled it)*
- `return section "NAME";` *(override detected section)*
- `return set_dl <kb_per_sec>;` / `return set_ul <kb_per_sec>;`
- `return add_credits <kb>;` / `return sub_credits <kb>;`

Numeric assignments are limited to credit math:

- `cost_download = <number>;`
- `cost_download *= <number>;`
- `earned_upload = <number>;`
- `earned_upload *= <number>;`

Logging for ad‑hoc diagnostics:

```text
log "User $user.name in section $section";
```

## Context‑specific guides

### `credits.msl`

Runs during transfer accounting.

- **Download (`RETR`) context**: `$section`, `$freeleech`, `$user.name`, `$user.group`, `$bytes`, `$kb`, `$cost_download`
- **Upload (`STOR`/`APPE`) context**: `$earned_upload` starts as computed earnings

Example rules:

```text
# Freeleech sections: no cost to download
if ($freeleech)
    cost_download = 0;

# Double upload credits for VIP group
if ($user.group == "VIP")
    earned_upload *= 2;

# Deny huge-cost downloads in 0DAY
if ($section == "0DAY" && $cost_download > 500000)
    return deny "550 Not enough credits for 0DAY.";
```

### `fxp.msl`

Gates FXP transfers. Context provides `$is_fxp`, `$user.*`, `$section`.

```text
# Deny FXP for group LEECH
if ($is_fxp && $user.group == "LEECH")
    return deny "504 FXP not allowed for your group.";
```

### `active.msl`

Controls PORT/EPRT (active mode). Context: `$user.*`, `$is_fxp`.

```text
# Disable active mode for anonymous users
if ($user.group == "ANON")
    return deny "504 Active mode is disabled for your account.";
```

### `section-routing.msl`

Overrides which section a path belongs to. Context includes `$section`, `$user.*`, `$virtual_path`, `$physical_path`.

```text
# Force VIP users into VIP-AREA for crediting
if ($user.group == "VIP")
    return section "VIP-AREA";
```

### `user-rules.msl` and `group-rules.msl`

Evaluated at login and per command. Both can deny with a message or adjust credit/limit fields provided by the daemon.

- Login: `group-rules.msl` runs first, then `user-rules.msl`.
- Per command: `group-rules.msl` runs for every FTP command; use `$event` or the command/args if you extend the engine.

Example:

```text
# Block uploads for LEECH group
if ($user.group == "LEECH")
    return deny "550 Uploads are not allowed for your group.";
```

### `site.msl`

Intercepts SITE commands. The engine passes `$event` for the logical SITE action and exposes `$user.*` plus paths when relevant.

```text
# Disable SITE for non-admins
if ($user.group != "ADMIN")
    return deny "550 SITE commands are disabled for your account.";

# Short-circuit your own SITE handler
if ($event == "NUKE")
    return output "200 Custom NUKE handled by script";
```

### Placeholder files

- `section-rules.msl` – extra section checks as features land
- `sections.msl` – complex section logic extensions
- `speed.msl` – dynamic bandwidth rules (`return set_dl`, `return set_ul`)
- `messages.msl` – scripted banners/log tweaks

## Common recipes

- **Freeleech weekend**
  ```text
  if ($section == "MOVIES" || $section == "TV")
      cost_download = 0;
  ```

- **Strict FXP area**
  ```text
  if ($is_fxp && $section != "FXP-AREA")
      return deny "504 FXP only allowed in FXP-AREA.";
  ```

- **Group-specific routing**
  ```text
  if ($user.group == "ARCHIVIST")
      return section "ARCHIVE";
  ```

- **Custom SITE response**
  ```text
  if ($event == "RULES")
      return output "200 Read the rules at /RULES.txt";
  ```

## Debugging tips

- Enable AMScript debug logging in your hosting code by attaching to `AMScriptEngine.DebugLog` to see `[AMScript] ...` messages.
- Keep rules to one action per line; malformed lines are skipped without crashing the daemon.
- When experimenting, place restrictive rules at the top of a file to stop evaluation early via `return deny`.

## Downloading this guide

This README is plain Markdown; you can download `docs/AMScript.md` directly from the repository and drop it alongside your `.msl` files or internal wiki.
