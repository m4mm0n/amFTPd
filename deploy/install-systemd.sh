#!/usr/bin/env bash
# ============================================================================
# amFTPd systemd installer
# ============================================================================
# Usage:  sudo bash deploy/install-systemd.sh [--prefix /opt/amftpd]
#
# What this script does:
#   1. Creates a dedicated 'amftpd' system user and group
#   2. Creates the required directories
#   3. Copies the binary from the current directory to $PREFIX
#   4. Copies the unit file to /etc/systemd/system/
#   5. Enables and starts the service
#
# After installation:
#   sudo systemctl status amftpd
#   sudo systemctl reload amftpd   # reload config (SIGHUP / REHASH)
#   sudo journalctl -u amftpd -f   # follow logs
# ============================================================================

set -euo pipefail

PREFIX="/opt/amftpd"
CONFIG_DIR="/etc/amftpd"
DATA_DIR="/var/lib/amftpd"
LOG_DIR="/var/log/amftpd"
SERVICE_USER="amftpd"
SERVICE_GROUP="amftpd"

# ── parse arguments ──────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
  case "$1" in
    --prefix) PREFIX="$2"; shift 2 ;;
    *) echo "Unknown argument: $1"; exit 1 ;;
  esac
done

# ── must run as root ─────────────────────────────────────────────────────────
if [[ $EUID -ne 0 ]]; then
  echo "ERROR: This script must be run as root (sudo)." >&2
  exit 1
fi

echo "=== amFTPd systemd installer ==="
echo "  Install prefix : $PREFIX"
echo "  Config dir     : $CONFIG_DIR"
echo "  Data dir       : $DATA_DIR"
echo ""

# ── 1. Create user/group ─────────────────────────────────────────────────────
if ! getent group "$SERVICE_GROUP" &>/dev/null; then
  echo "[1/6] Creating group '$SERVICE_GROUP'..."
  groupadd --system "$SERVICE_GROUP"
else
  echo "[1/6] Group '$SERVICE_GROUP' already exists."
fi

if ! getent passwd "$SERVICE_USER" &>/dev/null; then
  echo "[2/6] Creating user '$SERVICE_USER'..."
  useradd --system --gid "$SERVICE_GROUP" \
          --no-create-home --shell /usr/sbin/nologin \
          --comment "amFTPd daemon account" "$SERVICE_USER"
else
  echo "[2/6] User '$SERVICE_USER' already exists."
fi

# ── 2. Create directories ────────────────────────────────────────────────────
echo "[3/6] Creating directories..."
install -d -m 755 -o root     -g root          "$PREFIX"
install -d -m 750 -o root     -g "$SERVICE_GROUP" "$CONFIG_DIR"
install -d -m 750 -o "$SERVICE_USER" -g "$SERVICE_GROUP" "$DATA_DIR"
install -d -m 750 -o "$SERVICE_USER" -g "$SERVICE_GROUP" "$LOG_DIR"

# ── 3. Copy binary ───────────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BINARY_DIR="$(dirname "$SCRIPT_DIR")"
BINARY="$BINARY_DIR/amFTPd"

if [[ ! -f "$BINARY" ]]; then
  # Try the publish output next to the deploy folder
  BINARY="$BINARY_DIR/publish/amFTPd"
fi

if [[ ! -f "$BINARY" ]]; then
  echo "ERROR: Cannot find amFTPd binary. Build the project first:" >&2
  echo "  dotnet publish amFTPd/amFTPd.csproj -c Release -r linux-x64 --self-contained -o publish/" >&2
  exit 1
fi

echo "[4/6] Copying binary to $PREFIX..."
cp -a "$BINARY" "$PREFIX/"
chmod 755 "$PREFIX/amFTPd"
chown root:root "$PREFIX/amFTPd"

# Copy default config if not already present
if [[ ! -f "$CONFIG_DIR/amftpd.json" ]] && [[ -f "$BINARY_DIR/amftpd.json" ]]; then
  echo "      Copying default config to $CONFIG_DIR/amftpd.json..."
  cp "$BINARY_DIR/amftpd.json" "$CONFIG_DIR/"
  chown root:"$SERVICE_GROUP" "$CONFIG_DIR/amftpd.json"
  chmod 640 "$CONFIG_DIR/amftpd.json"
fi

# ── 4. Install systemd unit ──────────────────────────────────────────────────
echo "[5/6] Installing systemd unit..."
UNIT_SRC="$SCRIPT_DIR/amftpd.service"

# Patch ExecStart, WorkingDirectory, and ReadWritePaths to match chosen prefix
TMP_UNIT="$(mktemp)"
sed \
  -e "s|/opt/amftpd|$PREFIX|g" \
  -e "s|/etc/amftpd|$CONFIG_DIR|g" \
  -e "s|/var/lib/amftpd|$DATA_DIR|g" \
  -e "s|/var/log/amftpd|$LOG_DIR|g" \
  "$UNIT_SRC" > "$TMP_UNIT"

install -m 644 -o root -g root "$TMP_UNIT" /etc/systemd/system/amftpd.service
rm -f "$TMP_UNIT"

systemctl daemon-reload

# ── 5. Enable and start ───────────────────────────────────────────────────────
echo "[6/6] Enabling and starting amFTPd..."
systemctl enable amftpd
systemctl start  amftpd

echo ""
echo "=== Installation complete ==="
echo "  Status    : sudo systemctl status amftpd"
echo "  Logs      : sudo journalctl -u amftpd -f"
echo "  REHASH    : sudo systemctl kill --signal=SIGHUP amftpd"
echo "  Stop      : sudo systemctl stop amftpd"
echo "  Uninstall : sudo systemctl disable --now amftpd && sudo rm /etc/systemd/system/amftpd.service"
echo ""
echo "  Edit config: $CONFIG_DIR/amftpd.json"
