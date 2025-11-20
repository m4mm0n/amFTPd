# amFTPd Scripting Engine (AMScript)

amFTPd includes a small embedded rule engine called **AMScript**.

It lets you change behaviour without recompiling the daemon:

- Allow / deny commands per user, group, or section
- Control FXP and active mode
- Tune credits & ratios per section or group
- Influence `SITE` commands
- Override section routing
- (Future) Adjust speed limits and other settings

All rules live in plain-text `.msl` files and are hot-reloaded on save.

---

## 1. Script location & loading

### 1.1 Global script config

Global script config is read from:

- `config/scripts.json`

Example:

```json
{
  "RulesPath": "config/rules"
}
```

If `scripts.json` is missing, amFTPd defaults to `config/rules` inside the daemon folder.

If `RulesPath` is relative, it is resolved from the daemon’s base directory.

### 1.2 Rule files

On startup, amFTPd ensures that the rule directory exists and that all core `.msl` files are present. By default you get:

- `credits.msl`
- `fxp.msl`
- `active.msl`
- `section-routing.msl`
- `site.msl`
- `user-rules.msl`
- `group-rules.msl`
- `section-rules.msl` *(placeholder for future rules)*
- `sections.msl` *(placeholder for future rules)*
- `speed.msl` *(placeholder for future rules)*
- `messages.msl` *(placeholder for banners/logging)*

You can edit or delete rules inside the files; amFTPd only cares that the file itself exists.

### 1.3 Hot reload

Each `.msl` file is monitored with `FileSystemWatcher`:

- When you save the file, the rules are reloaded automatically.
- Syntax errors in a file keep the daemon running; that file’s rules are simply ignored until fixed.
- Script debug messages (if enabled) are written to the FTP log.

---

## 2. Language overview

AMScript is intentionally simple: “if this, then that”.

### 2.1 Basic rule shape

A rule is a single line starting with `if`:

```text
if (<condition>) <action>;
```

Examples:

```text
# Freeleech for a specific section
if ($section == "0DAY") cost_download = 0;

# Deny FXP for a group
if ($is_fxp && $user.group == "ANON")
    return deny "504 FXP not allowed for this group";
```

Everything after `#` on a line is treated as a comment.

Blank lines are ignored.

### 2.2 Conditions

Conditions support:

- `&&`  logical AND
- `||`  logical OR
- `!`   logical NOT
- `==` and `!=` for equality/inequality

Examples:

```text
if ($user.group == "VIP" && !$freeleech) ...
if ($is_fxp || $section == "ARCHIVE") ...
```

Atomic conditions are comparisons of a variable to a string:

```text
$user.group == "VIP"
$user.group != "ANON"
```

String literals use double quotes, e.g. `"0DAY"`, `"VIP"`. There is no escaping in v1 – avoid embedded quotes.

### 2.3 Variables

The engine exposes a limited, well-defined set of variables depending on context. Common ones:

| Variable          | Type    | Meaning                                      |
|-------------------|---------|----------------------------------------------|
| `$is_fxp`         | bool    | True if this transfer is considered FXP      |
| `$section`        | string  | Section name (`"APPS"`,`"MP3"`,`"default"`)  |
| `$freeleech`      | bool    | True if section is configured as free-leech  |
| `$user.name`      | string  | Username                                     |
| `$user.group`     | string  | Group name (empty if none)                   |
| `$bytes`          | number  | Payload size in bytes                        |
| `$kb`             | number  | Payload size in kilobytes                    |
| `$cost_download`  | number  | Current credit cost for a download           |
| `$earned_upload`  | number  | Current credit earnings for an upload        |

In some rule types (like section routing, SITE), additional context fields like `VirtualPath` and `PhysicalPath` are available internally in C#, but not yet exposed as `$...` variables. That can be extended in code later.

### 2.4 Actions

The action part decides what happens when the condition matches. There are three main categories:

1. **Return actions**
2. **Numeric assignments**
3. **Logging**

#### 2.4.1 Return actions

These stop evaluation and return a result to the server:

```text
return allow;
return deny "message";
return output "text";
return override;
return section "NAME";
return set_dl 10240;
return set_ul 5120;
return add_credits 1000;
return sub_credits 500;
```

Semantics:

- `return allow;`  
  Let the operation continue normally. This is mostly useful when you want to *force* allowing something even if later static checks might deny it (depending on how you wire results in code).

- `return deny "message";`  
  Deny the operation. For most contexts, the message becomes the FTP reply.  
  Example: `return deny "550 Uploads disabled for your account";`

- `return output "text";`  
  Used mainly in `site.msl`. Sends `200 text` as response and skips built-in handling for that SITE command.

- `return override;`  
  Used in `site.msl` to say: “the script has handled everything; just send generic 200 OK”.

- `return section "NAME";`  
  Used in `section-routing.msl`. Asks the daemon to route the path as if it belonged to section `NAME` instead of the auto-detected one.

- `return set_dl <kb_per_sec>;`  
  Intended for dynamic download speed limits (kb/s). Current daemon versions don’t fully apply this yet; it’s reserved for future extensions.

- `return set_ul <kb_per_sec>;`  
  Same as above, but for upload speed limits.

- `return add_credits <kb>;` / `return sub_credits <kb>;`  
  Adjust credits by a fixed kb amount. Again, wiring into the daemon is partial in v1, but the fields exist for future automation.

#### 2.4.2 Numeric assignment

There are two special numeric variables you can modify directly in `credits.msl`:

- `cost_download`
- `earned_upload`

Supported forms:

```text
cost_download = 0;
cost_download *= 2;

earned_upload = 0;
earned_upload *= 2;
```

Example:

```text
# Double upload earnings in section 0DAY for group UPLOADER
if ($user.group == "UPLOADER" && $section == "0DAY")
    earned_upload *= 2;

# Free download in ARCHIVE regardless of configured ratios
if ($section == "ARCHIVE")
    cost_download = 0;
```

#### 2.4.3 Logging

You can log arbitrary messages via:

```text
log "User $user.name in section $section";
```

This sends the message through amFTPd’s logging system (useful for debugging rules).

---

## 3. How each script is used

### 3.1 `credits.msl`

Used when calculating credits for transfers.

**Download (`RETR`) path:**

1. Base cost is computed:
   - `kb = bytes / 1024`
   - If section has ratio (UploadUnit / DownloadUnit), cost is scaled accordingly.
   - If section is free-leech, base cost is 0.
2. `credits.msl` runs with:
   - `$section`, `$freeleech`, `$user.name`, `$user.group`, `$bytes`, `$kb`, `$cost_download`.
3. The final `$cost_download` is used to decide whether the user has enough credits, and how much to subtract afterwards.

**Upload (`STOR` / `APPE`) path:**

1. Base earnings are computed:
   - `kb = bytes / 1024`
   - If section has ratio, earnings are scaled; otherwise default 1:1.
2. `credits.msl` runs with `$earned_upload` set to the base earnings.
3. The final `$earned_upload` is added to the user’s credits.

Typical examples:

```text
# Freeleech sections: no cost to download
if ($freeleech)
    cost_download = 0;

# Double upload credits for VIP group
if ($user.group == "VIP")
    earned_upload *= 2;

# Deny downloads in 0DAY if the computed cost is too high
if ($section == "0DAY" && $cost_download > 500000)
    return deny "550 Not enough credits for 0DAY.";
```

### 3.2 `fxp.msl`

Controls FXP (server-to-server) behaviour.

Context:

- `$is_fxp` is true when the transfer is considered FXP.
- `$user.name`, `$user.group`, `$section` are also available.

Example:

```text
# Deny FXP for group LEECH
if ($is_fxp && $user.group == "LEECH")
    return deny "504 FXP not allowed for your group.";

# Allow FXP only in section FXP-AREA
if ($is_fxp && $section != "FXP-AREA")
    return deny "504 FXP only allowed in FXP-AREA.";
```

### 3.3 `active.msl`

Controls active mode (PORT/EPRT).

Context:

- `$user.name`, `$user.group`, `$is_fxp`

Example:

```text
# Disable active mode for anonymous group
if ($user.group == "ANON")
    return deny "504 Active mode is disabled for your account.";
```

### 3.4 `section-routing.msl`

Lets you override which section a given virtual path belongs to.

amFTPd first guesses a section based on `SectionManager` mappings; then `section-routing.msl` can override that via `return section "NAME";`.

Example:

```text
# Route all content for VIP users into VIP-AREA section for credit purposes
if ($user.group == "VIP")
    return section "VIP-AREA";
```

If no rule matches, the automatically-detected section is used.

### 3.5 `user-rules.msl` and `group-rules.msl`

These are used for *login* and *per-command* policy enforcement.

**Login phase:**

- `group-rules.msl` is evaluated first, with a context representing the user.
- `user-rules.msl` is evaluated after group rules.

Both can:

- `return deny "530 Some reason"` to reject login.
- Adjust credits or limits via `CreditDelta`, `NewUploadLimit`, `NewDownloadLimit` (as exposed in the context/result class).

**Per-command phase:**

- `group-rules.msl` is evaluated for **every command**, using a context built from:
  - user, group, command name, arguments, section, etc.
- If a rule returns `deny`, the command is rejected with the rule’s message.

Example (group rules):

```text
# Block uploads for LEECH group
if ($user.group == "LEECH")
    return deny "550 Uploads are not allowed for your group.";
```

### 3.6 `site.msl`

Controls behaviour of `SITE` commands.

Current engine wiring allows you to:

- Deny SITE entirely for some users/groups.
- Intercept certain SITE commands and replace them with your own responses if you extend the variable set in code.

Minimal example that disables SITE for non-admins:

```text
if ($user.group != "ADMIN")
    return deny "550 SITE commands are disabled for your account.";
```

Future extensions can expose `$site.command`, `$site.args`, `$is_admin` variables so you can implement custom SITE subcommands purely in script.

### 3.7 Placeholders – `section-rules.msl`, `sections.msl`, `speed.msl`, `messages.msl`

These files exist so you have a place to add rules as the daemon grows:

- `section-rules.msl` – extra section-level checks.
- `sections.msl` – more complex section logic if needed.
- `speed.msl` – scripted speed limits (download/upload kb/s) per user/group/section.
- `messages.msl` – bans, welcome messages, logging tweaks.

---

## 4. Quick reference

### 4.1 Variables

- `$is_fxp` – `true` if FXP transfer
- `$section` – section name (`"default"`, `"0DAY"`, etc.)
- `$freeleech` – free-leech flag for section
- `$user.name` – username
- `$user.group` – group name
- `$bytes` – size in bytes (credits context)
- `$kb` – size in kilobytes (credits context)
- `$cost_download` – current download cost (credits)
- `$earned_upload` – current upload earnings (credits)

### 4.2 Return actions

- `return allow;`
- `return deny "message";`
- `return output "text";`
- `return override;`
- `return section "NAME";`
- `return set_dl <kb_per_sec>;`
- `return set_ul <kb_per_sec>;`
- `return add_credits <kb>;`
- `return sub_credits <kb>;`

### 4.3 Assignments

- `cost_download = <number>;`
- `cost_download *= <number>;`
- `earned_upload = <number>;`
- `earned_upload *= <number>;`

### 4.4 Logging

- `log "message";`

---

## 5. Notes on Ident integration

amFTPd’s user model supports Ident-based binding:

- `RequireIdentMatch` (bool) — if true, the daemon should require a valid Ident response.
- `RequiredIdent` (string) — if non-empty, the ident username must match this value.

The actual Ident handshake is performed server-side by connecting to the client’s port 113 and sending:

```text
<server-port> , <client-port>\r\n
```

According to RFC 1413, `<server-port>` is the FTP server’s port (usually 21/2121) and `<client-port>` is the client-side ephemeral port. If `RequireIdentMatch` is enabled and the returned username does not match `RequiredIdent`, the login is rejected.

This README focuses on the scripting engine; Ident is configured per-user and enforced in the login path on the daemon side.
