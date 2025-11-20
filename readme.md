# amFTPd – Modern, Scriptable FTP/FTPS Daemon for .NET

amFTPd is a fully modern FTP/FTPS server written in C# (.NET), designed to resemble the configurability and power of scene‑grade daemons (like glFTPd, ioFTPD, RaidenFTPD) while keeping the codebase clean, small, and highly extendable.

It is built for:
- Home labs
- Automation-heavy systems
- Scene‑style setups (sections, credits, groups, FXP)
- Advanced admins who want full control via scripts

The daemon is **TLS-capable**, **AMScript-driven**, **non-blocking**, and **extremely configurable**.

---

# 1. Features

## 1.1 FTP + FTPS (TLS)
- Full FTP command set including:
  - USER / PASS / AUTH TLS / PBSZ / PROT
  - LIST, NLST, PWD, CWD, CDUP
  - RETR, STOR, APPE, DELE
  - RNFR, RNTO
  - MKD, RMD
  - PASV, EPSV, PORT, EPRT
- Explicit TLS (AUTH TLS) with:
  - TLS 1.2 + 1.3 support
  - Certificate loading from .pfx
  - Secure control + data channels

## 1.2 Scriptable Rule Engine (AMScript)
The most powerful part of amFTPd is the **rule engine**.

Admins can script behaviour in `.msl` files to control:

- FXP (server‑to‑server transfers)
- Active mode restrictions
- Credit system (cost, earnings, overrides)
- Section routing
- Per‑command allow/deny policies
- Per‑group and per‑user rules
- Custom SITE commands
- Dynamic speed limit overrides
- Login acceptance policies

Scripts are **live‑reloaded** when edited.

✓ No recompiling  
✓ No restarting  
✓ Instant effect

See the included file: `amftpd_amscript_readme.md`

## 1.3 User & Group System
Each `FtpUser` includes:

- `UserName`
- `PasswordHash`
- `HomeDir`
- `IsAdmin`
- `AllowFxp`
- `AllowUpload`
- `AllowDownload`
- `AllowActiveMode`
- `AllowedIpMask`
- `MaxConcurrentLogins`
- `IdleTimeout`
- `MaxUploadKbps`
- `MaxDownloadKbps`
- `GroupName`
- `CreditsKb`
- **Ident enforcement:**
  - `RequireIdentMatch`
  - `RequiredIdent`

Groups include:
- Members
- Section‑based credit overrides
- Placeholders for future group mechanics

User stores:
- In-memory (testing)
- Binary (production)
- mmap‑friendly variant for speed

## 1.4 Section & Credit System
amFTPd uses **sections**, similar to scene daemons:

Each section includes:
- `Name`
- `VirtualRoot` (e.g. `/0day`)
- `FreeLeech`
- `RatioUploadUnit`
- `RatioDownloadUnit`

Upload earnings and download costs follow section ratios.  
AMScript may override the calculation.

Credit flow:
- RETR → cost → subtract credits
- STOR/APPE → earning → add credits

All credit logic is applied AFTER scripts finalize their adjusted values.

## 1.5 FXP & Active Mode Control
- FXP detection based on IP mismatch on data channel
- Scripts can allow/deny FXP (`fxp.msl`)
- Per-user AllowFxp flags
- Active mode (PORT/EPRT) policy via script (`active.msl`)
- Per-user AllowActiveMode flags

## 1.6 Logging System
Supports:
- Console logging
- File logging
- Multi-sink logging
- Integration with QuickLog
- Script debug output
- Detailed command tracing
- Data-channel event logging

## 1.7 Virtual File System (VFS)
Security‑focused:
- Recursive path sanitization
- Prohibits escaping above user root
- Virtual → real path mapping
- Provides `LIST`/`NLST` formatting (Unix‑style)

## 1.8 Hot Reloads
- Script rules reload instantly
- Section routing re-evaluated per request
- Binary stores reload when updated (depending on backend)
- Daemon does **not** require restart for most config changes

---

# 2. Project Structure

```
amFTPd/
│
├─ Core/
│   ├─ FtpSession.cs              – session lifecycle & state
│   ├─ FtpCommandRouter.cs        – command dispatcher + rules + logic
│   ├─ FtpDataConnection.cs       – PASV/PORT/FXP data channels
│   ├─ FtpResponses.cs            – constants for protocol replies
│
├─ FileSystem/
│   └─ FtpFileSystem.cs           – VFS, safe pathing, listings
│
├─ Scripts/
│   ├─ AMScriptEngine.cs          – parser + evaluator
│   ├─ AMScriptContext.cs         – data passed into scripts
│   ├─ AMScriptDefaults.cs        – default rule files
│
├─ Db/
│   ├─ BinaryUserStore.cs
│   ├─ BinaryGroupStore.cs
│   ├─ BinarySectionStore.cs
│   ├─ DatabaseManager.cs
│   ├─ FtpGroup.cs
│   ├─ SectionStore.cs
│
├─ Credits/
│   └─ CreditEngine.cs            – admin/tooling calculator
│
├─ Logging/
│   ├─ ConsoleFtpLogger.cs
│   ├─ FileFtpLogger.cs
│   ├─ CombinedFtpLogger.cs
│   └─ QuickLogAdapter.cs
│
├─ Config/
│   ├─ AmFtpdConfigLoader.cs      – loads amftpd.json
│   ├─ AmFtpdRuntimeConfig.cs
│   └─ Ftpd/                      – strongly-typed config objects
│
├─ Utils/
│   ├─ PathUtils.cs
│   ├─ IdentQueryUtils.cs
│   └─ IpMaskUtils.cs
│
├─ Program.cs                     – entry point
└─ amftpd.json                    – main config file
```

---

# 3. Configuration

Main configuration is in **amftpd.json**.

Example:

```json
{
  "Server": {
    "BindAddress": "0.0.0.0",
    "Port": 2121,
    "PassivePorts": "40000-40100",
    "RootPath": "D:/ftp-root",
    "AllowFxp": true,
    "AllowActiveMode": true,
    "RequireTlsForAuth": false
  },
  "Tls": {
    "Enabled": true,
    "CertificateFile": "cert.pfx",
    "CertificatePassword": "mypwd"
  },
  "Storage": {
    "Mode": "Binary",
    "UserDb": "users.db",
    "GroupDb": "groups.db",
    "SectionDb": "sections.db"
  }
}
```

### 3.1 Sections
Sections determine routing, credits, and freeleech flags.

Example (binary store managed):
- `DEFAULT`
- `0DAY`
- `MP3`
- `MOVIES`

### 3.2 Users
Users can restrict:
- Upload
- Download
- FXP
- Active mode
- Idle timeouts
- Concurrent logins
- Speed limits

### 3.3 Scripts
Script engine is configured by:

`config/scripts.json`:
```json
{
  "RulesPath": "config/rules"
}
```

---

# 4. Runtime Overview

### 4.1 Login Flow
1. USER → store lookup
2. PASS → password check
3. IDENT check (if enabled)
4. Group rule scripts
5. User rule scripts
6. Session begins

### 4.2 Command Flow
Every command goes through:

1. Unauthenticated whitelist  
2. Group rules (deny/allow)  
3. Static user permissions  
4. User rules (if applicable)  
5. Section routing  
6. Command handler

### 4.3 Data Transfer Flow

#### Downloads (RETR):
- Get section
- Compute base cost
- Run `credits.msl`
- Enforce credit balance
- Transfer with throttle
- Subtract credits

#### Uploads (STOR/APPE):
- Check upload rights
- Section routing
- Base earnings
- Run `credits.msl`
- Add credits

---

# 5. Ident (RFC 1413)

amFTPd supports Ident-based user verification.

### User options:
- `RequireIdentMatch = true` → login fails unless ident is successful
- `RequiredIdent = "username"` → ident must match specific username

### Ident Flow:
1. After PASS but before login completion
2. Server connects to remote host on port 113
3. Sends: `local-port , remote-port`
4. Parses the returned username
5. Enforces policy

If mismatched or missing (and required), login is rejected.

---

# 6. Roadmap

Upcoming improvements:
- Full scripted SITE command arguments
- Per-user dynamic throttling via script
- MLSD support
- Further VFS enhancements
- Additional Ident modes
- Full integration of CreditEngine into admin tools
- Optional chroot-like virtual filesystem layer

---

# 7. License

This project is released under the MIT License. You are free to use, modify, and distribute the code under the terms of this license.

---

# 8. Included Documentation

- **amscript_readme.md** – detailed script engine documentation

