# amFTPd IDENT Subsystem

amFTPd implements RFC 1413 IDENT lookups as a configurable security layer. IDENT can be used for logging only, strict enforcement, or as a basis for automatic group assignment.

---

## What IDENT does

When a client connects, the server optionally queries the client's machine on TCP port 113 asking "which user owns the connection with source port X?". The response (the IDENT username) is then compared against the FTP login credentials and/or used for group mapping.

IDENT is a weak security measure — the client controls the daemon running on port 113 and can return any string. Use it primarily for logging and convenience group assignment rather than as a hard authentication barrier.

---

## Configuration

IDENT is configured in the `Ident` block of `amftpd.json`:

```json
"Ident": {
  "Modes":                    "Standard,Caching",
  "TimeoutMs":                3000,
  "CacheTtlSeconds":          300,
  "DenyOnStrictMismatch":     false,
  "DenyOnReverseDnsMismatch": false,
  "DenyOnTlsBindingMismatch": false,
  "GroupMappings": []
}
```

---

## Modes

`Modes` is a comma-separated combination of flags:

| Flag | Description |
|---|---|
| `Disabled` | No IDENT queries are performed. |
| `Standard` | Perform RFC 1413 lookup and store the result in the session. |
| `LoggingOnly` | Perform the lookup but do not enforce anything — only log the result. Combine with `Standard`. |
| `StrictUserMatch` | The IDENT username returned by the client must exactly match the FTP username. |
| `GroupMapping` | Map the IDENT username to a group using the `GroupMappings` table. |
| `ReverseDnsCheck` | Perform a reverse DNS (PTR) lookup and verify it is consistent with the IDENT result. |
| `TlsBinding` | Verify that the IDENT username matches the CN of the client's TLS certificate (mTLS). |
| `Caching` | Cache IDENT results per IP for `CacheTtlSeconds` seconds. Reduces latency for rapid reconnects from the same host. |

**Default**: `Standard,LoggingOnly,Caching` — queries are performed and logged, but no enforcement occurs.

### Recommended configurations

**Logging only (no enforcement)**
```json
"Modes": "Standard,LoggingOnly,Caching"
```

**Strict enforcement for paranoid sites**
```json
"Modes": "Standard,StrictUserMatch,Caching",
"DenyOnStrictMismatch": true
```

**Group auto-assignment without enforcement**
```json
"Modes": "Standard,GroupMapping,Caching,LoggingOnly"
```

**Full enforcement with PTR verification**
```json
"Modes": "Standard,StrictUserMatch,ReverseDnsCheck,Caching",
"DenyOnStrictMismatch":     true,
"DenyOnReverseDnsMismatch": true
```

---

## Timeout and caching

| Key | Default | Description |
|---|---|---|
| `TimeoutMs` | `3000` | How long to wait for an IDENT response (ms). Connections that time out proceed normally (the IDENT result is simply absent). |
| `CacheTtlSeconds` | `300` | Cache lifetime per IP in seconds. Set to `0` to disable caching. |

A failed or timed-out IDENT query is never treated as a denial — the IDENT result is simply absent. Enforcement keys (below) only fire when a result *is* present and *fails* the check.

---

## Enforcement flags

| Key | Default | When it matters |
|---|---|---|
| `DenyOnStrictMismatch` | `true` | Active when `StrictUserMatch` is in `Modes`. Denies login if the IDENT username ≠ FTP username. |
| `DenyOnReverseDnsMismatch` | `false` | Active when `ReverseDnsCheck` is in `Modes`. Denies login if the PTR record is inconsistent. |
| `DenyOnTlsBindingMismatch` | `false` | Active when `TlsBinding` is in `Modes`. Denies login if the IDENT result ≠ TLS certificate CN. |

Setting an enforcement flag to `false` while the corresponding mode is active causes the mismatch to be logged but not acted upon — useful for testing before enabling enforcement.

---

## Group mappings

`GroupMappings` allows automatic group assignment based on the IDENT username returned by the client. This is useful in environments where IDENT reflects the OS username on a shared machine.

```json
"GroupMappings": [
  { "IdentUsername": "bob",      "FtpGroup": "USERS"  },
  { "IdentUsername": "alice",    "FtpGroup": "VIP"    },
  { "IdentUsername": "root",     "FtpGroup": "SITEOP" },
  { "IdentUsername": "ftpuser*", "FtpGroup": "USERS"  }
]
```

Fields:

| Field | Description |
|---|---|
| `IdentUsername` | IDENT username to match. Case-insensitive. Supports `*` glob. |
| `FtpGroup` | FTP group name to assign when the IDENT matches. |

When a match is found, the FTP user's primary group is overridden for the duration of the session. The user record itself is not modified.

---

## SITE IDENT and SITE REQIDENT

Siteops can set per-user IDENT requirements over FTP:

```
SITE IDENT <user> <ident-string>       # require this exact IDENT for the user
SITE REQIDENT <user> on [ident-string] # enable/disable mandatory IDENT for user
SITE REQIDENT <user> off
```

Per-user requirements layer on top of the global `Ident` configuration. If a user has `RequireIdent = true` and a specific string set, that string must match regardless of the global mode.

---

## Interaction with TLS

The `TlsBinding` mode requires that:
1. The client presents a client certificate during the TLS handshake (`AUTH TLS` + client cert).
2. The IDENT query result matches the CN of the presented certificate.

This combination provides a stronger (though still client-controlled) two-factor check. It is not a substitute for mutual TLS with a trusted CA.

---

## Logging

When `Standard` and `LoggingOnly` are both set, every IDENT result is written to the structured FTP log with the session ID, remote IP, FTP username, and the returned IDENT string. Failed queries (timeout or connection refused) are also logged.

Example log line:
```
[IDENT] session=a1b2c3d4 ip=1.2.3.4 ftpUser=bob identResult=bob match=true
```
