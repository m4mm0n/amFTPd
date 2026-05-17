# amFTPd Configuration Reference

All configuration lives in a single JSON file, `amftpd.json`, passed as the first positional argument when starting the daemon. The file is hot-reloaded on `SIGHUP` (Linux) or `SITE REHASH` — you do not need to restart the server after editing it.

Run the built-in linter at any time to check for errors before applying:

```bash
amftpd amftpd.json --validate
```

Exit codes: `0` = clean, `1` = warnings only, `2` = one or more errors.

---

## Top-level structure

```json
{
  "Server":         { ... },
  "Tls":            { ... },
  "Storage":        { ... },
  "Ident":          { ... },
  "Vfs":            { ... },
  "Sections":       { "NAME": { ... } },
  "DirectoryRules": { "/path": { ... } },
  "RatioRules":     { "RULENAME": { ... } },
  "Groups":         { "GROUPNAME": { ... } },
  "FxpPolicy":      { ... },
  "Irc":            { ... },
  "Zipscript":      { ... },
  "Status":         { ... },
  "Compatibility":  { ... },
  "Logging":        { ... },
  "Webhooks":       { ... },
  "Acme":           { ... },
  "Plugins":        [ ... ]
}
```

Required blocks: `Server`, `Tls`, `Storage`, `Ident`, `Vfs`, `Sections`, `DirectoryRules`, `RatioRules`, `Groups`. All other blocks are optional — omit the key entirely to disable the feature.

---

## `Server`

Core FTP server parameters.

| Key | Type | Description |
|---|---|---|
| `BindAddress` | string | IP address or hostname to bind the control connection listener to. Use `"0.0.0.0"` for all interfaces. |
| `Port` | int | Control port. Standard FTP is `21`; common alternatives are `990` (implicit TLS) or `2121`. |
| `PassivePortStart` | int | First port in the PASV/EPSV range. |
| `PassivePortEnd` | int | Last port in the PASV/EPSV range. Open this range in your firewall. |
| `RootPath` | string | Physical path used as the default FTP root. Must exist and be readable by the daemon's OS user. Superseded by VFS mounts when configured. |
| `WelcomeMessage` | string | Sent to clients in the `220` greeting. Supports AMScript tokens. |
| `AllowAnonymous` | bool | Enable anonymous login (`USER anonymous`). Default: `false`. |
| `RequireTlsForAuth` | bool | Reject `USER`/`PASS` unless the client has already issued `AUTH TLS`. Default: `true`. |
| `DataChannelProtectionDefault` | string | Default PROT level for data connections: `"C"` (clear) or `"P"` (private/encrypted). Default: `"P"`. |
| `AllowActiveMode` | bool | Allow `PORT`/`EPRT` active-mode transfers. Disable for NAT environments. Default: `false`. |
| `AllowFxp` | bool | Allow FXP (server-to-server) transfers. Requires `FxpPolicy` block for fine-grained control. Default: `false`. |

**Example**

```json
"Server": {
  "BindAddress":                  "0.0.0.0",
  "Port":                         21,
  "PassivePortStart":             40000,
  "PassivePortEnd":               40100,
  "RootPath":                     "/srv/ftproot",
  "WelcomeMessage":               "Welcome to amFTPd.",
  "AllowAnonymous":               false,
  "RequireTlsForAuth":            true,
  "DataChannelProtectionDefault": "P",
  "AllowActiveMode":              false,
  "AllowFxp":                     false
}
```

---

## `Tls`

TLS certificate used for `AUTH TLS` / implicit TLS.

| Key | Type | Description |
|---|---|---|
| `PfxPath` | string | Path to a PFX/PKCS#12 certificate file. Relative paths are resolved from the config directory. |
| `PfxPassword` | string | Password protecting the PFX archive. |
| `SubjectName` | string | Subject (CN) expected in the certificate. Used for SNI matching and log messages. |

When `Acme` is also configured and `Enabled: true`, the daemon ignores `PfxPath` / `PfxPassword` and uses the ACME-managed certificate instead.

**Example**

```json
"Tls": {
  "PfxPath":     "certs/server.pfx",
  "PfxPassword": "changeme",
  "SubjectName": "ftp.example.com"
}
```

---

## `Storage`

Paths and backends for all persistent data.

| Key | Type | Default | Description |
|---|---|---|---|
| `UsersDbPath` | string | — | Path to the user database file. Format depends on `UserStoreBackend`. |
| `SectionsPath` | string | — | Directory where per-section statistics files are written. |
| `UserStoreBackend` | string | — | `"json"` (plain JSON, good for small sites) or `"binary"` (encrypted binary, recommended for production). |
| `MasterPassword` | string | — | Encryption key for `"binary"` store. Required when `UserStoreBackend` is `"binary"`. |
| `GroupsDbPath` | string | `"amftpd-groups.db"` | Path to the group database file. |
| `SectionsDbPath` | string | `"amftpd-sections.db"` | Path to the section statistics database. |
| `UseMmap` | bool | `true` | Use memory-mapped I/O for database reads. Disable on network file systems. |
| `DupeStoreBackend` | string | `"file"` | `"file"` (JSON) or `"binary"` (BinaryDupeStore). |
| `DupeStorePath` | string? | null | Override path for the dupe store. When null, defaults to a path derived from `UsersDbPath`. |

**Example**

```json
"Storage": {
  "UsersDbPath":      "data/users.db",
  "SectionsPath":     "data/sections",
  "UserStoreBackend": "binary",
  "MasterPassword":   "str0ng-master-key",
  "GroupsDbPath":     "data/groups.db",
  "SectionsDbPath":   "data/sections.db",
  "DupeStoreBackend": "binary",
  "DupeStorePath":    "data/dupes"
}
```

---

## `Ident`

Controls RFC 1413 IDENT lookups performed at connection time.

| Key | Type | Default | Description |
|---|---|---|---|
| `Modes` | flags | `Standard,LoggingOnly,Caching` | Combination of: `Disabled`, `Standard`, `LoggingOnly`, `Caching`, `StrictMatch`, `ReverseDns`, `TlsBinding`. |
| `TimeoutMs` | int | `3000` | IDENT query timeout in milliseconds. |
| `CacheTtlSeconds` | int | `300` | How long IDENT results are cached per IP (seconds). |
| `DenyOnStrictMismatch` | bool | `true` | Deny login when `StrictMatch` mode is active and the IDENT username does not match the FTP username. |
| `DenyOnReverseDnsMismatch` | bool | `false` | Deny login when `ReverseDns` mode is active and the PTR record does not match. |
| `DenyOnTlsBindingMismatch` | bool | `false` | Deny login when `TlsBinding` mode is active and the TLS CN does not match the IDENT identity. |
| `GroupMappings` | array | `[]` | List of `{ "IdentUsername": "…", "FtpGroup": "…" }` mappings. Matched users are auto-assigned to the specified group. |

**Example**

```json
"Ident": {
  "Modes":                    "Standard,Caching",
  "TimeoutMs":                2000,
  "CacheTtlSeconds":          600,
  "DenyOnStrictMismatch":     false,
  "GroupMappings": [
    { "IdentUsername": "bob",  "FtpGroup": "USERS" },
    { "IdentUsername": "root", "FtpGroup": "SITEOP" }
  ]
}
```

See [IDENT.md](IDENT.md) for the full reference.

---

## `Vfs`

Virtual file system mount table. Maps virtual FTP paths to physical directories.

| Key | Type | Description |
|---|---|---|
| `Mounts` | array | Global mount entries. Each entry: `{ "VirtualPath": "/...", "PhysicalPath": "/..." }`. |
| `UserMounts` | array | User-specific mounts evaluated before global mounts. Each entry: `{ "Username": "...", "Mount": { ... } }`. |

Mount resolution uses longest-prefix matching. See [VFS.md](VFS.md) for the full reference.

**Example**

```json
"Vfs": {
  "Mounts": [
    { "VirtualPath": "/",      "PhysicalPath": "/srv/ftproot" },
    { "VirtualPath": "/MP3",   "PhysicalPath": "/data/mp3" },
    { "VirtualPath": "/0DAY",  "PhysicalPath": "/data/0day" }
  ],
  "UserMounts": [
    {
      "Username": "admin",
      "Mount": { "VirtualPath": "/private", "PhysicalPath": "/srv/admin-private" }
    }
  ]
}
```

---

## `Sections`

Named upload zones, each with its own rules. The key is the section name as used in `SITE WHO`, stats, and IRC announces.

Each section entry is a `SectionRule` object:

| Key | Type | Default | Description |
|---|---|---|---|
| `SectionName` | string | *(key)* | Logical name of the section, e.g. `"MP3"`. |
| `RatioRuleName` | string | `""` | Name of the `RatioRule` to apply in this section. Must match a key in `RatioRules`. |
| `Enabled` | bool | `true` | Whether the section mapping is active. |

**Example**

```json
"Sections": {
  "MP3":  { "SectionName": "MP3",  "RatioRuleName": "STANDARD", "Enabled": true },
  "0DAY": { "SectionName": "0DAY", "RatioRuleName": "FREE",      "Enabled": true }
}
```

---

## `DirectoryRules`

Per-directory access and behavior overrides. The key is a virtual path prefix.

Typical fields (set any combination):

| Key | Type | Description |
|---|---|---|
| `AllowRead` | bool | Allow downloads from this path. |
| `AllowWrite` | bool | Allow uploads to this path. |
| `AllowDelete` | bool | Allow deletes in this path. |
| `RequiredGroup` | string | Only members of this group may access the path. |
| `RequiredFlag` | string | Users must have this flag to access the path. |

**Example**

```json
"DirectoryRules": {
  "/PRIVATE": { "AllowRead": true, "AllowWrite": false, "RequiredGroup": "SITEOP" },
  "/INCOMING": { "AllowRead": false, "AllowWrite": true }
}
```

---

## `RatioRules`

Named credit / ratio policies referenced from `Sections` and `Groups`.

| Key | Type | Default | Description |
|---|---|---|---|
| `Name` | string | *(key)* | Rule identifier. |
| `Ratio` | double? | null | Download-to-upload ratio (e.g. `3.0` = 1:3). `null` disables ratio enforcement. |
| `IsFree` | bool? | null | When `true`, downloads never cost credits. |
| `CreditsPerKiBUploaded` | int | `0` | Credits awarded per KiB uploaded. |
| `CreditsPerKiBDownloaded` | int | `0` | Credits deducted per KiB downloaded. |
| `MultiplyCost` | double? | `1.0` | Multiplier applied to download cost. |
| `UploadBonus` | double? | `0.0` | Extra upload credit multiplier. |
| `MinimumRatio` | double? | null | Deny downloads if the user's current ratio is below this value. |
| `MinHour` | int | `0` | This rule applies only after this hour (24h clock). |
| `MaxHour` | int | `0` | This rule applies only before this hour. |
| `TimeMultiplier` | double? | null | Credit multiplier applied during `MinHour`–`MaxHour` window. |

**Example**

```json
"RatioRules": {
  "STANDARD": { "Ratio": 3.0, "CreditsPerKiBUploaded": 1, "CreditsPerKiBDownloaded": 1 },
  "FREE":      { "IsFree": true },
  "VIP":       { "Ratio": 1.0, "UploadBonus": 2.0 }
}
```

---

## `Groups`

Group definitions. The key is the group name (uppercase conventional, e.g. `"USERS"`, `"SITEOP"`).

| Key | Type | Default | Description |
|---|---|---|---|
| `Description` | string | `""` | Human-readable group description shown in `SITE GROUPINFO`. |
| `RatioMultiply` | double | `1.0` | Multiplier applied to download ratio cost for all members. |
| `UploadBonus` | double | `1.0` | Multiplier applied to upload credit awards for all members. |
| `IsSiteOp` | bool | `false` | Members of this group have siteop privileges. |
| `MaxUsers` | int | `0` | Recommended maximum member count (informational; not enforced). |
| `DailyUploadLimitMb` | long | `0` | Per-user daily upload quota in MiB. `0` = unlimited. |
| `WeeklyUploadLimitMb` | long | `0` | Per-user weekly upload quota in MiB. `0` = unlimited. |
| `MonthlyUploadLimitMb` | long | `0` | Per-user monthly upload quota in MiB. `0` = unlimited. |
| `Flags` | string | `""` | Character flags (future use). |

**Example**

```json
"Groups": {
  "USERS":  { "Description": "Standard users",  "RatioMultiply": 1.0, "UploadBonus": 1.0 },
  "VIP":    { "Description": "VIP users",        "RatioMultiply": 0.5, "UploadBonus": 2.0 },
  "SITEOP": { "Description": "Site operators",   "IsSiteOp": true }
}
```

---

## `FxpPolicy` *(optional)*

Fine-grained control over FXP (server-to-server) transfers. Only meaningful when `Server.AllowFxp` is `true`.

```json
"FxpPolicy": {
  "AllowFxp": true,
  "RequireTls": true,
  "MinTlsVersion": "Tls12",
  "AllowedRemoteIpRanges": ["10.0.0.0/8", "192.168.1.0/24"]
}
```

---

## `Irc` *(optional)*

Connects the daemon to an IRC network and announces FTP events.

| Key | Type | Default | Description |
|---|---|---|---|
| `Enabled` | bool | `false` | Master switch. |
| `Server` | string | `"irc.example.net"` | IRC server hostname. |
| `Port` | int | `7000` | IRC server port. |
| `UseTls` | bool | `true` | Connect with TLS. |
| `UseStartTls` | bool | `false` | Upgrade with STARTTLS instead of implicit TLS. |
| `TlsServerName` | string? | null | SNI name override for TLS handshake. |
| `TlsAllowInvalidCerts` | bool | `true` | Accept self-signed IRC server certificates. |
| `ServerPassword` | string? | null | IRC server password (PASS command). |
| `Nick` | string | `"amFTPd-bot"` | Bot nickname. |
| `User` | string | `"amftpd-bot"` | IRC username. |
| `RealName` | string | `"amFTPd IRC announcer"` | IRC realname field. |
| `Channels` | string | `"#amftpd"` | Space-, comma-, or semicolon-separated channel list. |
| `FishEnabled` | bool | `false` | Enable FiSH (Blowfish CBC) per-channel encryption. |
| `FishKeys` | object | `{}` | Map of `"#channel"` → Blowfish key strings. |
| `PreFormat` | string? | null | Template for PRE announces. Tokens: `{release}`, `{section}`, `{user}`, `{mb}`. |
| `NukeFormat` | string? | null | Template for NUKE announces. Tokens: `{release}`, `{section}`, `{user}`, `{reason}`, `{mult}`. |
| `UnnukeFormat` | string? | null | Template for UNNUKE announces. |
| `RaceCompleteFormat` | string? | null | Template for race-complete announces. |
| `UploadFormat` | string? | null | Template for individual upload announces. |
| `ZipscriptFormat` | string? | null | Template for zipscript status messages. |

**Example**

```json
"Irc": {
  "Enabled":    true,
  "Server":     "irc.your-network.net",
  "Port":       6697,
  "UseTls":     true,
  "Nick":       "site-bot",
  "Channels":   "#site,#pre",
  "PreFormat":  "[\x0303PRE\x03] {release} [{section}] by {user} ({mb} MiB)",
  "NukeFormat": "[\x0304NUKE\x03] {release} [{section}] x{mult} — {reason}"
}
```

---

## `Zipscript` *(optional)*

Controls the built-in zipscript engine that processes uploaded archives and SFV files.

```json
"Zipscript": {
  "Enabled":          true,
  "RequireSfvFirst":  true,
  "MinFilesInSfv":    1,
  "AutoNukeOnBadCrc": true,
  "AutoNukeReason":   "BAD CRC",
  "AutoNukeMultiplier": 3,
  "ScriptFile":       "scripts/zipscript.ams"
}
```

---

## `Status` *(optional)*

HTTP endpoint for monitoring, the REST API, and the web admin dashboard.

| Key | Type | Default | Description |
|---|---|---|---|
| `Enabled` | bool | — | Master switch. |
| `BindAddress` | string | — | IP/hostname for the HTTP listener. Use `"127.0.0.1"` to restrict to localhost. |
| `Port` | int | — | HTTP port, e.g. `8080`. |
| `Path` | string | — | URL path prefix, e.g. `"/amftpd-status/"`. |
| `AuthToken` | string? | null | When set, all requests must supply `Authorization: Bearer <token>` or `X-AmFTPd-Token: <token>`. |
| `MetricsEnabled` | bool | `true` | Expose a Prometheus `/metrics` endpoint. |
| `MetricsPort` | int? | null | Separate port for `/metrics`. Defaults to `Port + 1`. |
| `RestApiEnabled` | bool | `true` | Enable REST API routes at `/api/*`. |
| `AdminDashboardEnabled` | bool | `true` | Serve the web admin dashboard SPA at `/admin`. |
| `IncludeIpStatsByDefault` | bool | `true` | Include per-IP traffic breakdown in `/api/stats` by default. |
| `MaxIpEntries` | int | `10` | Top-N IP entries returned; remainder is rolled into `_other`. |

**Example**

```json
"Status": {
  "Enabled":       true,
  "BindAddress":   "127.0.0.1",
  "Port":          8080,
  "Path":          "/",
  "AuthToken":     "super-secret-api-token",
  "RestApiEnabled": true,
  "AdminDashboardEnabled": true
}
```

See [RestAPI.md](RestAPI.md) for the full API reference.

---

## `Compatibility` *(optional)*

Adjusts output formats and SITE command aliases for clients built against ioFTPD or glFTPD.

| Key | Type | Default | Description |
|---|---|---|---|
| `Profile` | string | `"none"` | Preset: `"none"`, `"gl"`, `"io"`, or `"raiden"`. |
| `EnableCommandAliases` | bool | `true` | Resolve legacy SITE command aliases (e.g. `SITE ADDIP` → `SITE ADDHOST`). |
| `GlStyleSiteStat` | bool | `false` | Use glFTPD-style layout in `SITE STATS`. |
| `IoStyleSiteWho` | bool | `false` | Use ioFTPD-style layout in `SITE WHO`. |
| `IrcGlStyleMessages` | bool | `false` | Default IRC announce formats to gl/io-style templates. |
| `SiteCommandAliases` | object | `{}` | Custom alias map: `{ "ALIAS": "CANONICAL" }`. Evaluated only when `EnableCommandAliases` is `true`. |

**Example**

```json
"Compatibility": {
  "Profile":              "gl",
  "EnableCommandAliases": true,
  "GlStyleSiteStat":      true
}
```

---

## `Webhooks` *(optional)*

Fires HTTP POST JSON payloads on FTP events to external services (bots, logging pipelines, CI hooks, etc.).

| Key | Type | Default | Description |
|---|---|---|---|
| `Enabled` | bool | `true` | Master switch. |
| `SigningSecret` | string? | null | HMAC-SHA256 secret. When set, every request carries `X-AmFTPd-Signature: sha256=<hex>`. |
| `TimeoutSeconds` | int | `5` | Per-attempt HTTP timeout. |
| `MaxRetries` | int | `1` | Additional retry attempts on failure (`0` = fire-and-forget). |
| `UploadUrl` | string? | null | URL for successful `STOR` completions. |
| `DownloadUrl` | string? | null | URL for successful `RETR` completions. |
| `NukeUrl` | string? | null | URL for `SITE NUKE` and auto-nukes. |
| `UnnukeUrl` | string? | null | URL for `SITE UNNUKE`. |
| `PreUrl` | string? | null | URL for approved `SITE PRE` events. |
| `RaceCompleteUrl` | string? | null | URL fired when a race reaches completion. |
| `LoginUrl` | string? | null | URL for successful logins. |
| `LogoutUrl` | string? | null | URL for disconnects and kicks. |
| `RequestUrl` | string? | null | URL for `SITE REQUEST` submissions. |
| `DeleteUrl` | string? | null | URL for file deletes and `SITE WIPE`. |
| `DefaultUrl` | string? | null | Catch-all URL for any event that has no specific URL configured. |

**Payload shape**

```json
{
  "event":     "Upload",
  "timestamp": "2026-04-25T12:00:00Z",
  "data": {
    "user":        "bob",
    "group":       "USERS",
    "section":     "0DAY",
    "virtualPath": "/0DAY/Release-GRP",
    "releaseName": "Release-GRP",
    "bytes":       12345678,
    "reason":      null,
    "remoteHost":  "1.2.3.4",
    "extra":       null
  }
}
```

**Example**

```json
"Webhooks": {
  "Enabled":       true,
  "SigningSecret": "my-hmac-secret",
  "UploadUrl":    "https://my-bot.example.com/hooks/upload",
  "NukeUrl":      "https://my-bot.example.com/hooks/nuke",
  "DefaultUrl":   "https://my-bot.example.com/hooks/ftp"
}
```

---

## `Acme` *(optional)*

Automatic TLS certificate provisioning and renewal via ACME v2 (Let's Encrypt, ZeroSSL, etc.).

| Key | Type | Default | Description |
|---|---|---|---|
| `Enabled` | bool | — | Set `true` to activate. When `false`, falls back to `Tls.PfxPath`. |
| `Domain` | string | — | FQDN the certificate should be issued for. Must resolve to this server's public IP. |
| `Email` | string | — | Contact email sent to the CA for expiry notifications. |
| `AccountKeyPath` | string | `"acme-account.pem"` | PEM file used to persist the EC P-256 ACME account key. |
| `DirectoryUrl` | string | Let's Encrypt production | ACME v2 directory URL. Use the staging URL (`acme-staging-v02.api.letsencrypt.org`) for testing. |
| `RenewalThresholdDays` | int | `30` | Start renewal when fewer than this many days remain. |
| `ChallengePort` | int | `80` | Port the HTTP-01 challenge listener binds to. Must be reachable as port 80 from the CA. |

**Example**

```json
"Acme": {
  "Enabled":              true,
  "Domain":               "ftp.example.com",
  "Email":                "admin@example.com",
  "AccountKeyPath":       "certs/acme-account.pem",
  "RenewalThresholdDays": 30
}
```

---

## `Logging` *(optional)*

amFTPd uses QuickLog as its only daemon logger. The default mode is `something`, which records operationally useful info, warnings, errors, and critical events without flooding the disk.

| Key | Type | Default | Description |
|---|---|---|---|
| `Mode` | string | `"something"` | `everything`, `something`, or `quiet`. Can be overridden at startup with `--log <mode>` or changed live with `SITE LOG <mode>`. |
| `TextLogPath` | string | `"logs/amftpd.log"` | Human-readable QuickLog text output. Relative paths resolve from the config file directory. |
| `BinaryLogPath` | string | `"logs/amftpd.qlbin"` | QuickLog binary event stream for post-mortem querying and export. |
| `Console` | bool | `true` | Mirror accepted log entries to stdout/stderr. |
| `Binary` | bool | `true` | Enable the QuickLog binary sink. |
| `QueueCapacity` | int | `8192` | Bounded async queue size. Values below 128 are raised to 128 at runtime. |

Mode behavior:

| Mode | Records |
|---|---|
| `everything` | Trace, debug, info, warnings, errors, and critical records. Use for deep debugging and scene incident review. |
| `something` | Info, warnings, errors, and critical records. This is the production default. |
| `quiet` | Errors, critical records, and core lifecycle/crash messages. Use for very low-noise operation. |

**Example**

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

---

## `Plugins` *(optional)*

Array of plugin DLLs to load at startup.

| Key | Type | Default | Description |
|---|---|---|---|
| `Path` | string | — | Path to the plugin DLL. Relative paths are resolved from the config directory. The directory must also contain a `.deps.json` file produced by `dotnet publish`. |
| `Enabled` | bool | `true` | Set `false` to skip loading without removing the entry. |
| `Settings` | object | `{}` | Arbitrary key-value pairs passed to the plugin via `IPluginContext.Settings`. |

**Example**

```json
"Plugins": [
  {
    "Path":    "plugins/LdapAuth/LdapAuth.dll",
    "Enabled": true,
    "Settings": {
      "LdapServer": "ldap://corp.example.com",
      "BaseDn":     "dc=corp,dc=example,dc=com"
    }
  },
  {
    "Path":    "plugins/SlackBot/SlackBot.dll",
    "Enabled": false
  }
]
```

See [Plugins.md](Plugins.md) for the plugin developer guide.

---

## Complete annotated example

```json
{
  "Server": {
    "BindAddress":                  "0.0.0.0",
    "Port":                         21,
    "PassivePortStart":             40000,
    "PassivePortEnd":               40100,
    "RootPath":                     "/srv/ftproot",
    "WelcomeMessage":               "amFTPd — ready.",
    "AllowAnonymous":               false,
    "RequireTlsForAuth":            true,
    "DataChannelProtectionDefault": "P",
    "AllowActiveMode":              false,
    "AllowFxp":                     false
  },
  "Tls": {
    "PfxPath":     "certs/server.pfx",
    "PfxPassword": "changeme",
    "SubjectName": "ftp.example.com"
  },
  "Storage": {
    "UsersDbPath":      "data/users.db",
    "SectionsPath":     "data/sections",
    "UserStoreBackend": "binary",
    "MasterPassword":   "changeme"
  },
  "Ident": {
    "Modes": "Standard,Caching"
  },
  "Vfs": {
    "Mounts": [
      { "VirtualPath": "/", "PhysicalPath": "/srv/ftproot" }
    ]
  },
  "Sections": {
    "MP3":  { "RatioRuleName": "STANDARD" },
    "0DAY": { "RatioRuleName": "FREE" }
  },
  "DirectoryRules": {},
  "RatioRules": {
    "STANDARD": { "Ratio": 3.0, "CreditsPerKiBUploaded": 1, "CreditsPerKiBDownloaded": 1 },
    "FREE":      { "IsFree": true }
  },
  "Groups": {
    "USERS":  { "Description": "Standard users" },
    "SITEOP": { "Description": "Site operators", "IsSiteOp": true }
  },
  "Status": {
    "Enabled":     true,
    "BindAddress": "127.0.0.1",
    "Port":        8080,
    "Path":        "/",
    "AuthToken":   "change-this-token"
  }
}
```
