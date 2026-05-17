# amFTPd FAQ & Troubleshooting

---

## Installation

### The daemon starts and exits immediately

If no `amftpd.json` is found, amFTPd generates a minimal default configuration and exits deliberately. Edit the generated file and start again:

```bash
nano amftpd.json   # review and adjust all "changeme" values
amftpd amftpd.json
```

### The config validator reports errors

Run the linter to see which keys are invalid:

```bash
amftpd amftpd.json --validate
```

Common issues: missing required fields (`BindAddress`, `Port`, `RootPath`), `PassivePortStart > PassivePortEnd`, or a `PfxPath` that does not exist.

### Port 21 is already in use

Another process is listening on port 21 (another FTP server, or the OS FTP service). Find and stop it:

```bash
# Linux
sudo ss -tlnp | grep :21
# Windows
netstat -ano | findstr :21
```

Or change `Server.Port` in `amftpd.json` to a non-privileged port like `2121`.

### Permission denied binding port 21

On Linux, ports below 1024 require elevated privileges. Three options:

```bash
# Option 1 — run as root (not recommended)
sudo amftpd amftpd.json

# Option 2 — grant the binary CAP_NET_BIND_SERVICE
sudo setcap cap_net_bind_service=+ep /srv/amftpd/amftpd

# Option 3 — change to port 2121 and use a firewall redirect
Server.Port = 2121
iptables -t nat -A PREROUTING -p tcp --dport 21 -j REDIRECT --to-port 2121
```

---

## TLS / Certificates

### Clients reject the TLS certificate

1. Verify the PFX file exists at the configured `Tls.PfxPath`.
2. Verify the `PfxPassword` is correct.
3. For self-signed certificates: the client must be configured to trust the certificate (or trust validation must be disabled in the client for testing).
4. Check that `Tls.SubjectName` matches the hostname clients use to connect.

### Let's Encrypt / ACME challenges fail

The HTTP-01 challenge requires port 80 to be publicly reachable from Let's Encrypt's servers. Check:

1. Port 80 is open in your firewall / router (port-forward if behind NAT).
2. The `Acme.Domain` DNS A record points to your server's public IP.
3. No other service is binding port 80 (use `Acme.ChallengePort` to use a non-standard port if you port-forward 80 → something else).
4. Use the staging ACME URL (`acme-staging-v02.api.letsencrypt.org`) for testing to avoid rate limits.

### FTPS AUTH TLS fails with "SSL handshake failed"

- Confirm `RequireTlsForAuth` is `true` on the server and the client is sending `AUTH TLS` before `USER`.
- Check that the TLS minimum version matches your client. Most modern clients negotiate TLS 1.2 or 1.3 — older clients may need a `Compatibility` profile.
- If the PFX was generated on Windows with SHA-1, regenerate it with SHA-256.

---

## Logins

### "530 Login incorrect" on every attempt

1. Verify the user exists: `SITE SHOWUSER <username>` from an admin session.
2. Confirm the account is not suspended: look for `Disabled: true` in `SITE SHOWUSER` output.
3. If `RequireTlsForAuth` is `true`, the client must issue `AUTH TLS` before `USER`/`PASS`.
4. IP mask restrictions — the connecting IP may not match any entry in the user's `AllowedIps` list.

### No users exist — how do I create the first admin?

See [GettingStarted.md — Adding the first user](GettingStarted.md#adding-the-first-user). The two supported paths are the JSON seed file (for `UserStoreBackend: "json"`) and the REST API (`POST /api/users`).

### Login works but immediately disconnects

Check for concurrent-login limits: `SITE SETLIMITS <user> 0 0 <max> <idle>`. A `max` of `1` and an already-active session will reject new connections.

---

## Transfers

### Passive mode (PASV) times out / data connection refused

1. Confirm `PassivePortStart` – `PassivePortEnd` are open in your firewall.
2. If the server is behind NAT, clients may connect to the server's private IP. Configure a masquerade address (future config key) or use `AllowActiveMode = true` as a workaround for LAN setups.
3. On Docker: the passive port range must be published: `ports: "40000-40100:40000-40100/tcp"`.

### Active mode (PORT) does not work

Active mode is disabled by default (`AllowActiveMode: false`) because it is incompatible with NAT. Enable only on private networks where the server can initiate connections back to the client.

### Transfer aborts with "CRC32 mismatch"

amFTPd verifies the CRC32 of resumed transfers before accepting the REST offset. The partial file on the server is corrupt. Delete the partial file and re-upload from the beginning.

### Upload speed is capped lower than expected

Check `SITE SHOWUSER <user>` for `MaxUploadKbps`. `0` means no per-user limit. Also check group-level quotas if applicable, and host network throughput.

---

## Nukes

### SITE NUKE succeeds but credits are not deducted

Confirm the section has a valid `RatioRuleName` pointing to a rule in `RatioRules`, and that the rule has `CreditsPerKiBUploaded > 0`. If `IsFree: true` is set on the rule, no credits are ever tracked.

### Nuked release is not marked in the dupe DB

Nuke deduction requires the dupe store to be enabled (`Storage.DupeStoreBackend` set to `"file"` or `"binary"`, and `Storage.DupeStorePath` accessible). Check daemon startup logs for dupe store initialization errors.

---

## IRC

### IRC bot connects but never announces

1. Confirm `Irc.Enabled: true`.
2. Check that `Irc.Channels` includes the target channel and the bot has joined it (check the IRC server).
3. Confirm the relevant format string is set — e.g. `PreFormat` must be non-null for pre announces.
4. If using FiSH encryption, confirm `FishEnabled: true` and the key for the target channel is in `FishKeys`.

### IRC bot is disconnected repeatedly

- `TlsAllowInvalidCerts: true` may be needed for IRC servers with self-signed certs.
- Check that `Nick` is not already in use on the network. Append a suffix.
- Some IRC servers require a `ServerPassword`; set `Irc.ServerPassword`.

---

## Webhooks

### Webhooks are not being received

1. Check `Webhooks.Enabled: true`.
2. Verify the URL is reachable from the server (firewall, DNS).
3. Check the `TimeoutSeconds` — the receiving endpoint must respond within this window.
4. Look at daemon logs for `[Webhook]` entries indicating delivery failures.

### Signature verification fails on the receiving end

The payload is signed with HMAC-SHA256 over the raw request body, with the key being `Webhooks.SigningSecret`. Verify the receiving end uses the same key and hashes the raw bytes (not the parsed JSON).

---

## Migration

### `--check-migration` reports missing users

Users in the source that were already disabled or had no-login flags may have been skipped during import. Re-run `SITE IMPORT` with explicit `--users` to import all accounts regardless of status. Review the import log for skipped entries.

### Credits are slightly different (< 1 MiB discrepancy)

This is within the tolerance window (`CreditToleranceKb = 1024`). It is typically caused by rounding in the legacy server's credit storage (fractional KiB). No action is required.

### `--check-migration` exits with code 2 (Errors)

At least one user or group from the source is absent in amFTPd. The JSON report (`--json <file>`) lists the missing names. Run `SITE IMPORT --users --groups` to import them, then re-run the check.

---

## REST API

### All API requests return 401

Verify the `Status.AuthToken` in `amftpd.json` matches the token you are sending. Check for leading/trailing whitespace in the config value.

### The REST API returns 503 for dupe endpoints

The dupe store is not configured or failed to initialize. Check that `Storage.DupeStoreBackend` is set and the path is accessible.

### `POST /api/rehash` returns 503

The REST API router does not have a reference to the running server instance. This indicates an internal wiring issue — check daemon startup logs for errors.

---

## Plugins

### Plugin DLL fails to load

1. Confirm the `Path` in `amftpd.json` is correct and the file exists.
2. The plugin directory must also contain a `.deps.json` file produced by `dotnet publish`. This is required for dependency resolution.
3. The plugin must target .NET 10 and reference `amFTPd.Plugin.Abstractions`.
4. Check the daemon log for `[PluginHost]` entries with load errors.

### Plugin SITE commands are not recognised

`ISiteCommandPlugin.RegisteredVerbs` must return the verb exactly as the client sends it (e.g. `"HELLO"`, not `"hello"`). The router uses a case-insensitive lookup, but the verb returned by the plugin must be non-empty.

---

## Performance

### High CPU on busy sites

- Enable `Storage.UseMmap: true` (default) for the user and group stores.
- Use the `"binary"` dupe store backend (`DupeStoreBackend: "binary"`) instead of JSON for large dupe databases — the binary store is O(log n) for lookups vs. O(n) for the JSON file store.
- Reduce the IRC announce rate if `ZipscriptFormat` or `UploadFormat` generates very high message volumes.

### Memory growth over time

amFTPd caches IDENT results and some VFS metadata in-process. The caches are bounded — if you see unbounded growth, check for a plugin memory leak. Running `dotnet-counters monitor` against the daemon process can identify the source.

---

## Logging

### Where are the logs?

By default, logs are written to stdout. When running as a systemd service, use `journalctl -u amftpd -f`. The xferlog (wu-ftpd format) is written to `xferlog` in the daemon's working directory.

### How do I increase log verbosity?

Set the environment variable `AMFTPD_LOG_LEVEL=Debug` before starting the daemon. Available levels: `Trace`, `Debug`, `Info`, `Warn`, `Error`.

---

## Getting help

- Run `SITE HELP [cmd]` from any FTP session for inline command reference.
- Review [Configuration.md](Configuration.md) for all config keys.
- File issues at `https://github.com/zerolinez/amftpd`.
