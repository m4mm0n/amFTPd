# amFTPd REST API Reference

The REST API is served by the same HTTP listener as the status endpoint. Enable it by adding a `Status` block to `amftpd.json` with `RestApiEnabled: true`:

```json
"Status": {
  "Enabled":        true,
  "BindAddress":    "127.0.0.1",
  "Port":           8080,
  "Path":           "/",
  "AuthToken":      "your-api-token",
  "RestApiEnabled": true
}
```

All API routes live under `/api/`.

---

## Authentication

When `AuthToken` is configured, every request must include the token in one of these forms:

```
Authorization: Bearer <token>
X-AmFTPd-Token: <token>
GET /api/sessions?token=<token>
```

Requests without a valid token receive `401 Unauthorized`.

---

## Response format

All responses are `application/json; charset=utf-8`. Successful responses return HTTP `200`. Errors return an appropriate 4xx or 5xx status with a JSON body:

```json
{ "error": "Human-readable error message." }
```

Property names use **camelCase** throughout. `null` fields are omitted.

---

## Endpoints

### GET /api/

Returns a machine-readable list of all available endpoints.

```json
{
  "endpoints": [
    "GET  /api/sessions",
    "GET  /api/users",
    "GET  /api/users/{name}",
    "GET  /api/stats",
    "GET  /api/stats/sections",
    "GET  /api/stats/top?window=all|today|week|month&count=N&section=S",
    "GET  /api/dupes/search?q=term&limit=N",
    "GET  /api/pres?count=N&section=S",
    "POST /api/kick",
    "POST /api/ban",
    "POST /api/pre",
    "POST /api/rehash"
  ]
}
```

---

### GET /api/sessions

Returns all currently active FTP sessions.

**Response**

```json
{
  "count": 2,
  "sessions": [
    {
      "sessionId":       "a1b2c3d4",
      "user":            "bob",
      "group":           "USERS",
      "remoteIp":        "1.2.3.4",
      "transferDir":     "upload",
      "transferFile":    "/0DAY/Release-GRP/file.rar",
      "bytesCompleted":  12345678,
      "bytesTotal":      99999999
    },
    {
      "sessionId":   "e5f6a7b8",
      "user":        "alice",
      "group":       "VIP",
      "remoteIp":    "5.6.7.8",
      "transferDir": "idle"
    }
  ]
}
```

`transferDir` is one of `"idle"`, `"upload"`, or `"download"`.

---

### GET /api/users

Returns all registered user accounts.

**Response**

```json
{
  "count": 3,
  "users": [
    {
      "userName":           "bob",
      "group":              "USERS",
      "secondaryGroups":    [],
      "isAdmin":            false,
      "isSiteop":           false,
      "disabled":           false,
      "isNoRatio":          false,
      "maxConcurrentLogins": 3,
      "creditsKb":          1048576,
      "maxUploadKbps":      0,
      "maxDownloadKbps":    0,
      "allowFxp":           false,
      "allowUpload":        true,
      "allowDownload":      true
    }
  ]
}
```

---

### GET /api/users/{name}

Returns a single user by username (case-insensitive).

**404** if the user does not exist.

**Response** — same shape as a single object from `/api/users`.

---

### GET /api/stats

Returns aggregate server statistics since startup.

**Response**

```json
{
  "nowUtc":                "2026-04-25T12:00:00Z",
  "activeSessions":        4,
  "activeTransfers":       2,
  "totalTransfers":        1847,
  "bytesUploaded":         109951162777,
  "bytesDownloaded":       8589934592,
  "failedLogins":          3,
  "totalCommands":         42918,
  "abortedTransfers":      7,
  "totalConnections":      512,
  "maxConcurrentTransfers": 8,
  "nukes":                 12,
  "unnukes":               2,
  "pres":                  394,
  "rolling": {
    "transfersPerSecond": {
      "s5": 0.4,
      "m1": 0.2,
      "m5": 0.1
    }
  }
}
```

---

### GET /api/stats/sections

Returns live statistics broken down by section.

**Response**

```json
{
  "count": 2,
  "sections": [
    {
      "section":         "MP3",
      "activeUsers":     1,
      "uploads":         247,
      "downloads":       89,
      "bytesUploaded":   10737418240,
      "bytesDownloaded": 1073741824
    },
    {
      "section":         "0DAY",
      "activeUsers":     3,
      "uploads":         1600,
      "downloads":       0,
      "bytesUploaded":   99213849600,
      "bytesDownloaded": 0
    }
  ]
}
```

---

### GET /api/stats/top

Returns the top uploaders leaderboard.

**Query parameters**

| Parameter | Default | Description |
|---|---|---|
| `window` | `all` | Time window: `all`, `today`, `week`, or `month`. |
| `count` | `10` | Number of entries to return (max 100). |
| `section` | *(all)* | Optional section filter. |

**Response**

```json
{
  "window":  "today",
  "count":   3,
  "section": null,
  "top": [
    { "rank": 1, "user": "alice", "group": "VIP",   "files": 42, "bytesUploaded": 5368709120 },
    { "rank": 2, "user": "bob",   "group": "USERS", "files": 18, "bytesUploaded": 2147483648 },
    { "rank": 3, "user": "carol", "group": "USERS", "files": 11, "bytesUploaded": 1073741824 }
  ]
}
```

---

### GET /api/dupes/search

Search the dupe database.

**Query parameters**

| Parameter | Default | Description |
|---|---|---|
| `q` | — | Search term (required). Supports `*` and `?` glob patterns. |
| `limit` | `20` | Maximum results to return (max 200). |

**400** if `q` is missing. **503** if no dupe store is configured.

**Response**

```json
{
  "query": "Release*",
  "count": 2,
  "results": [
    {
      "releaseName":   "Release-GRP",
      "section":       "0DAY",
      "virtualPath":   "/0DAY/Release-GRP",
      "totalBytes":    123456789,
      "firstSeen":     "2026-04-10T08:00:00Z",
      "lastUpdated":   "2026-04-10T08:05:00Z",
      "uploaderUser":  "bob",
      "uploaderGroup": "USERS",
      "isNuked":       false
    }
  ]
}
```

When a release is nuked, `isNuked` is `true` and `nukeReason` is included.

---

### GET /api/pres

Returns recent pre registrations.

**Query parameters**

| Parameter | Default | Description |
|---|---|---|
| `count` | `20` | Number of entries to return (max 200). |
| `section` | *(all)* | Optional section filter. |

**Response**

```json
{
  "count": 2,
  "pres": [
    {
      "releaseName": "Release-GRP",
      "section":     "0DAY",
      "user":        "bob",
      "group":       "USERS",
      "timestamp":   "2026-04-25T11:00:00Z",
      "fileCount":   14,
      "totalBytes":  123456789,
      "status":      "approved"
    }
  ]
}
```

`status` is one of `"pending"`, `"approved"`, or `"denied"`.

---

### POST /api/kick

Kick (disconnect) all active sessions for a user.

**Request body**

```json
{ "user": "bob" }
```

**Response**

```json
{ "kicked": 2, "user": "bob" }
```

Returns `kicked: 0` with a message if the user has no active sessions (not an error).

---

### POST /api/ban

Ban an IP address, either permanently or for a fixed duration.

**Request body**

| Field | Type | Required | Description |
|---|---|---|---|
| `ip` | string | Yes | IP address to ban (IPv4 or IPv6). |
| `reason` | string | No | Human-readable reason, stored in the ban list. |
| `durationMinutes` | int | No | Duration in minutes. Omit or set to `0` for a permanent ban. |

```json
{ "ip": "1.2.3.4", "reason": "spammer", "durationMinutes": 1440 }
```

**Response**

```json
{
  "banned":          "1.2.3.4",
  "reason":          "spammer",
  "durationMinutes": 1440,
  "permanent":       false
}
```

---

### POST /api/pre

Register a release in the pre database. The release is immediately marked as approved and a pre event is published to the event bus (triggering IRC announces and webhooks if configured).

**Request body**

| Field | Type | Required | Description |
|---|---|---|---|
| `section` | string | Yes | Section name, e.g. `"0DAY"`. |
| `releaseName` | string | Yes | Release directory name, e.g. `"Release-GRP"`. |

```json
{ "section": "0DAY", "releaseName": "Release-GRP" }
```

**Response**

```json
{
  "registered":  "Release-GRP",
  "section":     "0DAY",
  "virtualPath": "/0DAY/Release-GRP"
}
```

---

### POST /api/rehash

Reload `amftpd.json` from disk. Equivalent to sending `SIGHUP` on Linux or `SITE REHASH` over FTP. Active sessions are not interrupted.

**Request body** — empty object `{}` or empty body.

**Response (success)**

```json
{ "success": true, "message": "Configuration reloaded." }
```

**Response (failure)** — HTTP 500 with:

```json
{ "success": false, "message": "Validation error: ..." }
```

---

## User management via REST

The current API provides read access to users (`GET /api/users`). To create, update, or delete users programmatically, use the FTP `SITE ADDUSER` / `SITE DELUSER` / `SITE CHPASS` commands over an authenticated FTPS connection, or use the web admin dashboard at `/admin` (when `AdminDashboardEnabled: true`).

---

## Using the API

### curl examples

```bash
BASE="http://127.0.0.1:8080"
TOKEN="your-api-token"

# List active sessions
curl -s -H "X-AmFTPd-Token: $TOKEN" $BASE/api/sessions | jq .

# Get a specific user
curl -s -H "Authorization: Bearer $TOKEN" $BASE/api/users/bob | jq .

# Top uploaders this week (section 0DAY)
curl -s -H "X-AmFTPd-Token: $TOKEN" \
  "$BASE/api/stats/top?window=week&count=5&section=0DAY" | jq .

# Search dupes
curl -s -H "X-AmFTPd-Token: $TOKEN" \
  "$BASE/api/dupes/search?q=Release*&limit=10" | jq .

# Kick a user
curl -s -H "X-AmFTPd-Token: $TOKEN" \
  -X POST -H "Content-Type: application/json" \
  -d '{"user":"bob"}' $BASE/api/kick | jq .

# Temporary ban
curl -s -H "X-AmFTPd-Token: $TOKEN" \
  -X POST -H "Content-Type: application/json" \
  -d '{"ip":"1.2.3.4","reason":"flooder","durationMinutes":60}' \
  $BASE/api/ban | jq .

# Register a pre
curl -s -H "X-AmFTPd-Token: $TOKEN" \
  -X POST -H "Content-Type: application/json" \
  -d '{"section":"0DAY","releaseName":"Release-GRP"}' \
  $BASE/api/pre | jq .

# Rehash config
curl -s -H "X-AmFTPd-Token: $TOKEN" \
  -X POST $BASE/api/rehash | jq .
```

---

## Prometheus metrics

When `MetricsEnabled: true`, a Prometheus-compatible `/metrics` endpoint is available on `MetricsPort` (defaults to `Port + 1`):

```
http://127.0.0.1:8081/metrics
```

The endpoint exposes gauges and counters for active sessions, transfer rates, total uploads/downloads, nuke/unnuke counts, login failures, and more.
