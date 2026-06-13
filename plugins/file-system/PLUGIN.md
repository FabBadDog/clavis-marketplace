---
name: file-system
pluginId: FileSystem
version: 1.0.117
apiVersion: 1.0.0
description: file:// resource read and write.
projectFile: ./FileSystem.csproj
dependencies:
  - { name: resource-contracts, version: 1 }
---

# FileSystem

## Purpose

The resource handler for the `file://` URI scheme. It registers itself with the ResourceBroker on
activation, then handles routed load and write requests by reading from and writing to the local
filesystem. Reads open a shared read-only `FileStream`; writes create any missing parent directories
and write the supplied bytes.

## Location

`src/plugins/FileSystem/`

## Config (`FileSystemConfig`)

Empty record - no configurable fields.

## Messages published

- `RegisterScheme("file", "FileSystem")` - announces this plugin as the `file` scheme handler on
  activation.
- `ResourceLoaded(IResource)` (as `LoadResourceResult`) - on a successful load; the `IResource` is a
  `FileResource` that opens a read-only `FileStream` on demand.
- `LoadFailed(message)` (as `LoadResourceResult`) - when the file is missing or the read throws.
- `WriteSucceeded(uri)` (as `WriteResourceResult`) - after bytes are written successfully.
- `WriteFailed(message)` (as `WriteResourceResult`) - when the write throws.
- `LogEntry` via `bus.LogInfo`/`LogWarn`/`LogError`.

## Messages subscribed

- `LoadSchemeResource` - ignores anything whose scheme is not `file`; otherwise resolves the local
  path, checks existence, and replies with `ResourceLoaded` or `LoadFailed`.
- `WriteSchemeResource` - ignores non-`file` schemes; otherwise ensures the parent directory exists,
  writes the bytes, and replies with `WriteSucceeded` or `WriteFailed`.

## Notes

- Handles the `file://` scheme only. Both read and write are supported.
- The local path comes from `new Uri(uri).LocalPath`, so callers must pass well-formed `file://` URIs.
- Reads open with `FileShare.Read` (other readers allowed). Writes use `File.WriteAllBytesAsync` and
  auto-create missing parent directories via `Directory.CreateDirectory`.
- It filters routed messages by scheme defensively even though the broker only routes `file` requests
  to it, so it coexists safely with other scheme handlers on the same bus.
