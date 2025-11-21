# amFTPd

amFTPd is a modern, scriptable, TLS-enabled FTP daemon written in C# (.NET 9+).  
It is designed to be lightweight, fast, modular, and highly extensible, with full support for
user/group permissions, FXP, scripting-based policy enforcement, and RFC‑compliant directory
listing commands including MLSD and MLST.

---

## ✨ Features

### ✔ Core FTP Server
- Fully asynchronous architecture
- Supports both **Active** (PORT/EPRT) and **Passive** (PASV/EPSV) data channels
- Supports **AUTH TLS**, **PBSZ**, **PROT**, and TLS on both control and data channels
- Virtual filesystem with root confinement per user
- Login/logout tracking and session state handling
- Clean and extendable command router

### ✔ Directory Listings
amFTPd supports all major listing commands:

| Command | Description | Data Channel | Status |
|--------|-------------|--------------|--------|
| `LIST` | POSIX‑style formatted listing | Yes | ✓ Implemented |
| `NLST` | Name-only listing | Yes | ✓ Implemented |
| `MLSD` | RFC 3659 machine‑readable directory listing | Yes | ✓ Implemented |
| `MLST` | RFC 3659 machine‑readable single‑item listing | No (control only) | ✓ Implemented |

MLSD/MLST generate “facts” such as:
```
type=dir;modify=20240118095530;perm=el; FolderName
type=file;size=12345;modify=20240118095530;perm=rl; SomeFile.bin
```

### ✔ User, Group, and Policy System
- User accounts with:
  - Download permissions
  - Upload permissions
  - Active‑mode permissions
  - Optional administrator flag
- Group rules
- Central authorization engine (`FtpAuthorization`)
- User/group/FXP/ActiveMode/Section routing via AMScript rules

### ✔ Scripting Engine (AMScript)
amFTPd includes a small, embeddable rule engine to enforce policies such as:
- denying specific commands
- redirecting actions
- rejecting FXP attempts
- restricting directories
- altering behavior per user or per group

Rules can apply to:
- Individual users
- Groups
- FXP attempts
- Active‑mode connections
- SITE commands
- File system operations

### ✔ FXP Handling
amFTPd includes:
- FXP detection
- AMScript‑based approval/denial
- Independent policy for Active/Passive during FXP
- Per‑user and per‑group FXP permissions

### ✔ TLS Support
- AUTH TLS
- PBSZ
- PROT C/P
- TLS session reuse on the data channel
- Optional certificate selection hooks

### ✔ Modern Codebase
- Records and nullable reference types
- Modular command handling
- Separate classes for:  
  `FtpServer`, `FtpSession`, `FtpDataConnection`, `FtpAuthorization`, `FtpFileSystem`
- Designed for long‑term extensibility

---

## 📦 Directory Structure (simplified)

```
Core/
  FtpServer.cs
  FtpSession.cs
  FtpCommandRouter.cs
  FtpDataConnection.cs
  FtpResponses.cs
  FtpFileSystem.cs

Security/
  FtpAuthorization.cs

Config/
  FtpUser.cs
  FtpGroup.cs

Scripting/
  AMScriptEngine.cs
  AMRuleResult.cs
  ...

README.md
```

---

## 🧩 MLSD & MLST Implementation Notes

### MLSD
- Machine‑readable listing (RFC 3659)
- Uses data connection
- Reuses `_s.WithDataAsync` for TLS/Active/Passive correctness
- Uses new `FtpFileSystem.ToMlsdLine(FileSystemInfo)` helper

### MLST
- Single‑item listing
- Control‑channel only (no data connection)
- Uses the same MLSD facts generator
- Outputs:
```
250-Listing
 type=file;size=123;modify=20240118095530;perm=rl; Foo.txt
250 End.
```

### FEAT Advertisement
Both MLSD and MLST are advertised via:

```
MLSD
MLST
```

---

## 🔐 Authorization Rules

`FtpAuthorization.IsCommandAllowedForUser` now includes:

```
"LIST", "NLST", "MLSD", "MLST", "RETR" => user.AllowDownload;
```

This keeps listing commands safely tied to user permissions.

---

## 🚀 Getting Started

### 1. Configure Users
Example:
```json
{
  "Users": [
    {
      "Name": "admin",
      "Password": "secret",
      "Root": "/srv/ftp",
      "AllowDownload": true,
      "AllowUpload": true,
      "AllowActiveMode": true,
      "IsAdmin": true
    }
  ]
}
```

### 2. Start the Server
```csharp
var server = new FtpServer(config, logger);
await server.StartAsync();
```

### 3. Connect with any FTP/FTPS client  
Supports FileZilla, WinSCP, lftp, ncftp, etc.

---

## 🧪 Testing

MLSD/MLST can be tested via:

### MLSD
```
MLSD
```

### MLST
```
MLST filename.txt
```

Both should return RFC‑compliant output.

---

## 📄 License
MIT (or your preferred license)

---

## 🤝 Contributing
Feel free to extend:
- FACT sets for MLSD/MLST  
- Section routing  
- Certificate selection logic  
- STAT/LIST unification  
- Custom file system backends  

Pull requests welcome!
