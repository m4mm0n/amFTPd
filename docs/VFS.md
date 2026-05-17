# amFTPd Virtual File System (VFS)

The VFS layer decouples the FTP directory tree seen by clients from the physical layout on disk. Every path a client sees is a *virtual path*; the server maps it to a physical directory through the mount table before performing any file operation.

---

## Concepts

**Virtual path** — the path as seen by FTP clients (always `/`-separated, always starts with `/`).

**Physical path** — the actual directory on the host operating system. May be on any mounted drive or network share accessible to the daemon's OS user.

**Mount** — a mapping from a virtual root prefix to a physical directory.

**Longest-prefix matching** — when resolving a virtual path, the mount with the longest matching prefix wins. `/MP3/incoming` is served by a `/MP3` mount before a `/` mount.

---

## Mount table

Mounts are declared in the `Vfs` block of `amftpd.json`.

### Global mounts

```json
"Vfs": {
  "Mounts": [
    { "VirtualPath": "/",      "PhysicalPath": "/srv/ftproot" },
    { "VirtualPath": "/MP3",   "PhysicalPath": "/data/mp3"    },
    { "VirtualPath": "/0DAY",  "PhysicalPath": "/data/0day"   },
    { "VirtualPath": "/XVID",  "PhysicalPath": "/mnt/nas1/xvid", "IsReadOnly": true }
  ]
}
```

**Fields**

| Field | Required | Description |
|---|---|---|
| `VirtualPath` | Yes | Virtual root prefix, e.g. `"/MP3"`. Must start with `/`. |
| `PhysicalPath` | Yes | Physical directory. Use absolute paths. On Windows, use forward slashes or escape backslashes. |
| `IsReadOnly` | No | When `true`, uploads, deletes, and renames are rejected for paths under this mount. Default: `false`. |

A mount whose `VirtualPath` is `"/"` serves as the fallback for all paths not matched by a more specific mount.

### Per-user mounts

User-specific mounts are resolved before global mounts. They are useful for personal home directories or user-specific overlays.

```json
"Vfs": {
  "Mounts": [
    { "VirtualPath": "/", "PhysicalPath": "/srv/ftproot" }
  ],
  "UserMounts": [
    {
      "Username": "alice",
      "Mount":    { "VirtualPath": "/home/alice", "PhysicalPath": "/srv/homes/alice" }
    },
    {
      "Username": "sysop",
      "Mount":    { "VirtualPath": "/private",    "PhysicalPath": "/srv/sysop-only", "IsReadOnly": false }
    }
  ]
}
```

**Fields**

| Field | Description |
|---|---|
| `Username` | FTP username (case-insensitive). |
| `Mount` | A standard mount object (`VirtualPath`, `PhysicalPath`, `IsReadOnly`). |

---

## Resolution algorithm

1. Look through `UserMounts` for entries matching the authenticated username.
2. Among matching user mounts, find the one with the longest `VirtualPath` prefix that is a prefix of the requested path.
3. If no user mount matches, repeat the search over `Mounts` (global mounts).
4. If no mount matches at all, the path is rejected as inaccessible.

All comparisons are case-insensitive on both virtual and physical sides.

---

## Virtual files

A mount can embed read-only "virtual files" — files that exist in the VFS tree but have no physical counterpart on disk. These are used for injecting text files (e.g. `README` banners) into directory listings without polluting the physical filesystem.

```json
{
  "VirtualPath": "/incoming",
  "PhysicalPath": "/srv/ftproot/incoming",
  "VirtualFiles": [
    {
      "Name":     "README.txt",
      "Contents": "Upload here. SFV first.\r\n"
    }
  ]
}
```

Virtual files appear in `LIST`/`NLST` results and are downloadable by clients. They cannot be overwritten or deleted through FTP.

---

## VFS symlinks

amFTPd maintains an internal symlink table for virtual symlinks that exist independently of OS-level symbolic links. They are managed with SITE commands:

```
SITE LINK   /source/path  /link/path   — create a VFS symlink
SITE UNLINK /link/path                  — remove a VFS symlink
SITE LINKS  [prefix]                    — list registered symlinks
```

VFS symlinks are resolved transparently during path lookups. A symlink pointing to a path that is not covered by any mount resolves to nothing (dangling symlink).

---

## Path normalisation

All client-supplied paths are normalised before mount resolution:

- Relative paths are made absolute relative to the client's current working directory.
- `..` and `.` segments are collapsed.
- Multiple consecutive `/` are reduced to one.
- A trailing `/` is stripped (except for the root `/`).

Normalisation happens before mount resolution, so a client at `/MP3` issuing `CWD ../0DAY` is normalised to `/0DAY` before lookup.

---

## Windows path notes

On Windows, physical paths should use forward slashes or doubled backslashes:

```json
"PhysicalPath": "D:/ftproot"
"PhysicalPath": "D:\\ftproot"
```

Drive letters and UNC paths are both supported:

```json
"PhysicalPath": "\\\\server\\share\\ftproot"
```

---

## Common configurations

### Single-root site

```json
"Vfs": {
  "Mounts": [
    { "VirtualPath": "/", "PhysicalPath": "/srv/ftproot" }
  ]
}
```

### Multi-drive scene site

```json
"Vfs": {
  "Mounts": [
    { "VirtualPath": "/",       "PhysicalPath": "/srv/ftproot" },
    { "VirtualPath": "/MP3",    "PhysicalPath": "/mnt/disk1/mp3" },
    { "VirtualPath": "/0DAY",   "PhysicalPath": "/mnt/disk2/0day" },
    { "VirtualPath": "/XVID",   "PhysicalPath": "/mnt/disk3/xvid" },
    { "VirtualPath": "/incoming","PhysicalPath": "/mnt/fast-ssd/incoming" }
  ]
}
```

### Read-only archive section

```json
{
  "VirtualPath": "/ARCHIVE",
  "PhysicalPath": "/mnt/cold-storage/archive",
  "IsReadOnly": true
}
```

Clients can browse and download from `/ARCHIVE` but upload attempts return `553 Permission denied`.
