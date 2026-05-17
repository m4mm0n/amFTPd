# Docker Deployment

## Quick start

```bash
# Build
docker build -t amftpd .

# Run (maps host port 21 → container 2121)
docker run -d --name amftpd \
  -p 21:2121 \
  -p 50000-50100:50000-50100 \
  -v $(pwd)/config:/config:ro \
  -v $(pwd)/data:/data \
  amftpd
```

The container expects:
- `/config/amftpd.json` — your configuration file (read-only mount)
- `/data/` — writable directory for user DB, dupe DB, logs, ACME account key

---

## Docker Compose (production)

Use the bundled `deploy/docker-compose.yml` for a full production setup:

```bash
# Start
docker compose -f deploy/docker-compose.yml up -d

# Follow logs
docker compose -f deploy/docker-compose.yml logs -f

# REHASH (reload config without restarting)
docker compose -f deploy/docker-compose.yml kill --signal SIGHUP amftpd

# Validate config
docker compose -f deploy/docker-compose.yml exec amftpd \
  /app/amFTPd /config/amftpd.json --validate
```

---

## Security hardening

The production Dockerfile and Compose file include several hardening measures:

**Non-root user** — the daemon runs as UID/GID 1500 (`amftpd`).
Ensure your host bind-mount directories are readable/writable by UID 1500:

```bash
sudo chown -R 1500:1500 ./data ./site
chmod 750 ./config
```

**Read-only root filesystem** — `read_only: true` in docker-compose.yml prevents
any writes to the container filesystem. Only `/tmp` (tmpfs) and the mounted volumes
are writable.

**Capability restrictions** — `cap_drop: ALL` removes every Linux capability.
Add `CAP_NET_BIND_SERVICE` if you configure amFTPd to listen on port 21 directly
inside the container (rather than mapping host 21 → container 2121).

**SIGTERM / graceful drain** — `stop_signal: SIGTERM` and `stop_grace_period: 75s`
give in-progress transfers up to 60 seconds to complete before Docker sends SIGKILL.

---

## Port mapping

| Service            | Container port | Recommended host mapping |
|--------------------|---------------|--------------------------|
| FTP control        | 2121/tcp      | 21:2121                  |
| FTPS implicit      | 990/tcp       | 990:990                  |
| Passive data range | 50000–50100   | 50000-50100:50000-50100  |
| ACME HTTP-01       | 80/tcp        | 80:80 (only if ACME enabled) |
| Status / metrics   | 8080/tcp      | 8080:8080 (optional)     |

The passive port range must match `Server.PassivePortMin` / `Server.PassivePortMax`
in `amftpd.json`.

---

## Let's Encrypt / ACME inside Docker

To use automatic TLS certificate provisioning, the ACME HTTP-01 challenge server
must be reachable on port 80 from the internet.

```yaml
# docker-compose.yml excerpt
ports:
  - "80:80"    # ACME HTTP-01

environment:
  - TZ=UTC     # recommended for consistent log timestamps
```

```jsonc
// amftpd.json excerpt
{
  "Acme": {
    "Enabled": true,
    "Domain": "ftp.example.com",
    "Email": "admin@example.com",
    "AccountKeyPath": "/data/acme-account.pem",
    "ChallengePort": 80
  },
  "Tls": {
    "PfxPath": "/data/amftpd-cert.pfx",
    "PfxPassword": "",
    "SubjectName": "CN=ftp.example.com"
  }
}
```

The ACME manager writes the renewed PFX to `/data/amftpd-cert.pfx` and triggers
an internal REHASH to hot-swap the certificate without container restart.

---

## Build options

```bash
# Self-contained Linux x64 (no .NET runtime on host needed)
docker build \
  --build-arg PUBLISH_ARGS="--runtime linux-x64 --self-contained true" \
  -t amftpd:standalone .

# Development image (skips trimming, includes debug symbols)
docker build --target build -t amftpd:dev .
```
