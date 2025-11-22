# Virtual Filesystem (VFS)

The VFS layer in amFTPd lets you map, overlay, and virtualize filesystem paths.

## Features
- Global mounts
- Per-user mounts
- Virtual files
- Path normalization
- Hidden/extension policies
- Metadata caching

## Example
```json
"Vfs": {
  "Mounts": [
    { "VirtualPath": "/pub", "PhysicalPath": "D:/ftproot/pub" }
  ]
}
```
