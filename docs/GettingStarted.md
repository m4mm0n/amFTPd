# Getting Started with amFTPd

amFTPd (a Managed FTP Daemon) is a modern, fully-featured Scene FTP server written in .NET 10. It is designed as a drop-in replacement and successor to ioFTPD and glFTPD, with full TLS support, a plugin API, a REST management interface, and hot-reload configuration.

---

## Prerequisites

| Platform | Requirement |
|---|---|
| Linux | .NET 10 runtime (`dotnet-runtime-10.0`) or self-contained build |
| Windows | .NET 10 runtime or self-contained build, Windows 10/Server 2019+ |
| Docker | Any Docker host with compose v2 |

---

## Installation

### Linux (bare metal / VM)

```bash
# 1. Install the .NET 10 runtime (Debian/Ubuntu example)
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update && sudo apt-get install -y dotnet-runtime-10.0

# 2. Extract the release archive
mkdir -p /srv/amftpd
tar -xzf amftpd-linux-x64.tar.gz -C /srv/amftpd

# 3. Make the binary executable
chmod +x /srv/amftpd/amftpd
```

### Windows

1. Download `amftpd-win-x64.zip` and extract it (e.g. `C:\amftpd\`).
2. Install the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) if not already present.
3. Run amFTPd directly from a console, scheduled supervisor, container, or your own process runner. amFTPd intentionally does not install itself as a Windows Service.

### Docker

```bash
git clone https://github.com/zerolinez/amftpd
cd amftpd
docker compose up -d
```

A production-ready `docker-compose.yml` is in the repository root. See [docs/Docker.md](Docker.md) for the full guide.

---

## First run — creating a default configuration

If no `amftpd.json` exists when amFTPd starts, it generates a minimal default configuration and exits. Edit the generated file before starting again.

```bash
/srv/amftpd/amftpd          # generates amftpd.json, then exits
nano amftpd.json             # review and adjust
/srv/amftpd/amftpd           # start the server
```

Use QuickLog verbosity overrides when needed:

```bash
/srv/amftpd/amftpd amftpd.json --log something
/srv/amftpd/amftpd amftpd.json --log everything
/srv/amftpd/amftpd amftpd.json --log quiet
```

---

## Minimal amftpd.json

Below is the smallest viable configuration. Every field is explained in [docs/Configuration.md](Configuration.md).

```json
{
  "Server": {
    "BindAddress": "0.0.0.0",
    "Port": 21,
    "PassivePortStart": 40000,
    "PassivePortEnd":   40100,
    "RootPath":         "/srv/ftproot",
    "WelcomeMessage":   "amFTPd ready.",
    "AllowAnonymous":   false,
    "RequireTlsForAuth": true,
    "DataChannelProtectionDefault": "P",
    "AllowActiveMode":  false,
    "AllowFxp":         false
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
    "Mode": "Disabled"
  },
  "Vfs": {
    "Mounts": [
      { "VirtualPath": "/", "PhysicalPath": "/srv/ftproot" }
    ]
  },
  "Sections": {},
  "DirectoryRules": {},
  "RatioRules": {},
  "Groups": {}
}
```

---

## Creating the FTP root and data directories

```bash
mkdir -p /srv/ftproot
mkdir -p /srv/amftpd/data/sections
mkdir -p /srv/amftpd/certs
```

---

## Generating a TLS certificate

### Self-signed (development / LAN)

```bash
openssl req -x509 -newkey rsa:4096 -keyout key.pem -out cert.pem -days 3650 -nodes \
  -subj "/CN=ftp.example.com"

openssl pkcs12 -export -out certs/server.pfx \
  -inkey key.pem -in cert.pem -passout pass:changeme
```

### Let's Encrypt (production)

Add an `Acme` block to `amftpd.json` and amFTPd handles provisioning and renewal automatically:

```json
"Acme": {
  "Enabled":   true,
  "Domain":    "ftp.example.com",
  "Email":     "admin@example.com",
  "AccountKeyPath": "certs/acme-account.pem"
}
```

Port 80 must be reachable for the HTTP-01 challenge. The daemon renews the certificate automatically 30 days before expiry.

---

## Adding the first user

Start the server, then connect with any FTP client that supports FTPS (AUTH TLS):

```
ftp> open ftp.example.com 21
ftp> AUTH TLS
ftp> USER admin
ftp> PASS yourpassword   ← will fail — no users exist yet
```

Because no users exist, the only way to add the first account is via the REST API (if the Status endpoint is configured) or by using the import tool.

### Quickest path: JSON user store seed file

If `UserStoreBackend` is `"json"`, create a seed file at the path specified by `UsersDbPath`:

```json
[
  {
    "UserName":     "admin",
    "PasswordHash": "",
    "PasswordPlain": "s3cr3t",
    "PrimaryGroup":  "SITEOP",
    "IsAdmin":       true,
    "IsSiteop":      true,
    "Disabled":      false
  }
]
```

Then start the server and log in as `admin`. Use `SITE ADDUSER` to add other accounts.

### Using the binary store (recommended for production)

With `UserStoreBackend: "binary"`, start the server and use the REST API to create the first user:

```bash
# The Status endpoint must have AuthToken configured (see Configuration.md)
curl -s -H "X-Api-Token: <token>" -H "Content-Type: application/json" \
  -X POST http://localhost:8080/api/users \
  -d '{"UserName":"admin","Password":"s3cr3t","IsAdmin":true,"IsSiteop":true}'
```

---

## Adding subsequent users via FTP

Once logged in as an admin or siteop:

```
SITE ADDUSER bob s3cr3t USERS
SITE CHGRP bob USERS
SITE SETLIMITS bob 0 0 3 300    ← no speed limit, 3 simultaneous, 300s idle
SITE SHOWUSER bob
```

---

## Configuring sections

Sections are named upload zones. Each section maps to a virtual path prefix and carries its own rules (ratio, SFV-first enforcement, etc.):

```json
"Sections": {
  "MP3": {
    "VirtualPath":     "/MP3",
    "MaxRatio":         3,
    "RequireSfvFirst":  true,
    "MinFilesInSfv":    1
  },
  "0DAY": {
    "VirtualPath":     "/0DAY",
    "MaxRatio":         0
  }
}
```

---

## Validating the configuration

Before starting (or after editing), run the built-in linter:

```bash
/srv/amftpd/amftpd amftpd.json --validate
```

Exit codes: `0` = clean, `1` = warnings only, `2` = one or more errors.

---

## Native service packaging

amFTPd intentionally does not ship a Windows Service wrapper or service installer. Keeping the daemon out of the Windows Service Control Manager removes a privileged service-management attack surface and is part of the 0.8 security posture.

### Linux — systemd

```bash
# Copy the provided unit file
sudo cp deploy/amftpd.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now amftpd
sudo journalctl -u amftpd -f
```

Send `SIGHUP` to reload the configuration without downtime:
```bash
sudo systemctl reload amftpd   # or: kill -HUP $(pidof amftpd)
```

---

## Next steps

| I want to… | Read… |
|---|---|
| Understand every config key | [Configuration.md](Configuration.md) |
| Set up virtual file system mounts | [VFS.md](VFS.md) |
| Configure IDENT verification | [IDENT.md](IDENT.md) |
| Write custom AMScript rules | [AMScript.md](AMScript.md) |
| Migrate from ioFTPD or glFTPD | [Migration.md](Migration.md) |
| Browse all SITE commands | [SiteCommands.md](SiteCommands.md) |
| Use the REST API | [RestAPI.md](RestAPI.md) |
| Deploy with Docker | [Docker.md](Docker.md) |
| Write a plugin | [Plugins.md](Plugins.md) |
| Find answers to common problems | [FAQ.md](FAQ.md) |
