# amFTPd Dockerfile — hardened multi-stage build
#
# Usage:
#   docker build -t amftpd .
#   docker run -d --name amftpd \
#     -p 21:2121 -p 50000-50100:50000-50100 \
#     -v $(pwd)/config:/config:ro \
#     -v $(pwd)/data:/data \
#     amftpd
#
# See deploy/docker-compose.yml for a full production-ready example.

# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore dependencies in a separate layer to maximise cache reuse
COPY global.json Directory.Build.props ./
COPY amFTPd/amFTPd.csproj amFTPd/
COPY amFTPd.Scripting.Tcl/amFTPd.Scripting.Tcl.csproj amFTPd.Scripting.Tcl/
COPY Plugins/              Plugins/
RUN dotnet restore ./amFTPd/amFTPd.csproj --runtime linux-x64

# Copy remaining sources and publish a self-contained, trimmed binary
COPY . .
RUN dotnet publish ./amFTPd/amFTPd.csproj \
        -c Release \
        --runtime linux-x64 \
        --self-contained true \
        /p:PublishTrimmed=false \
        /p:UseAppHost=true \
        -o /app/publish

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final

# Create a non-root user and group for the daemon process
RUN groupadd --system --gid 1500 amftpd \
 && useradd  --system --uid 1500 --gid amftpd \
             --no-create-home --shell /usr/sbin/nologin amftpd

WORKDIR /app

# Copy published artefacts
COPY --from=build --chown=root:amftpd /app/publish .

# ── Directory layout ──────────────────────────────────────────────────────────
# /config  — read-only mount: amftpd.json + TLS certificates
# /data    — writable mount : user DB, dupe DB, logs, acme-account.pem, etc.
# Both are created here so they exist even without a host mount.
RUN install -d -m 750 -o amftpd -g amftpd /config /data

# ── Signal and stop ───────────────────────────────────────────────────────────
# SIGTERM triggers a graceful shutdown (70 s drain budget in GracefulStopAsync).
STOPSIGNAL SIGTERM

# ── Health check ──────────────────────────────────────────────────────────────
# Probe the FTP control port; requires the container has a working shell.
# If you map a different port, adjust accordingly.
HEALTHCHECK --interval=30s --timeout=10s --start-period=20s --retries=3 \
    CMD bash -c 'echo > /dev/tcp/127.0.0.1/2121' 2>/dev/null || exit 1

# ── Volumes ───────────────────────────────────────────────────────────────────
VOLUME ["/config", "/data"]

# ── Ports ─────────────────────────────────────────────────────────────────────
# FTP control channel (map to 21 on the host with -p 21:2121)
EXPOSE 2121/tcp
# FTPS implicit (if enabled in config)
EXPOSE 990/tcp
# Passive data range (match Server.PassivePortMin/Max in amftpd.json)
EXPOSE 50000-50100/tcp

# ── Run as the non-root daemon user ───────────────────────────────────────────
USER amftpd

# ── Entry point ───────────────────────────────────────────────────────────────
# The runtime image does not include the full SDK, so we use the self-contained binary.
# Config file defaults to /config/amftpd.json; override via CMD or docker run args.
ENTRYPOINT ["/app/amFTPd"]
CMD ["/config/amftpd.json"]
