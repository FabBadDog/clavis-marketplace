---
name: resource-broker
pluginId: ResourceBroker
version: 1.0.8
apiVersion: 1.0.0
description: Resource scheme registry and load/write dispatch.
dependencies:
  - { name: resource-contracts, version: 1 }
language: csharp
assemblyName: ResourceBroker
rootNamespace: FabioSoft.Nucleus.Plugins.ResourceBroker
globalUsings:
  - FabioSoft.Contracts.Resource
---

# ResourceBroker

## Purpose

The scheme registry and load/write dispatcher for the resource subsystem. It owns no I/O itself: it
tracks which plugin handles which URI scheme (via `RegisterScheme`), and on each `LoadResource` /
`WriteResource` request it parses the URI, looks up the scheme, and routes the work to the registered
handler plugin. If no handler is registered for a scheme, or the URI is malformed, it answers the
caller directly with the appropriate failure message.

## Location

`src/plugins/ResourceBroker/`

## Config (`ResourceBrokerConfig`)

Empty record - no configurable fields.

## Messages published

- `LoadSchemeResource(scheme, uri)` - routing message sent to the registered handler when a
  `LoadResource` request matches a known scheme.
- `WriteSchemeResource(scheme, uri, content)` - routing message sent to the registered handler for a
  matching `WriteResource` request.
- `UnknownScheme(scheme)` (as `LoadResourceResult`) - when no handler is registered for a load scheme.
- `LoadFailed(message)` (as `LoadResourceResult`) - when the load URI is malformed.
- `WriteUnknownScheme(scheme)` (as `WriteResourceResult`) - when no handler is registered for a write
  scheme.
- `WriteFailed(message)` (as `WriteResourceResult`) - when the write URI is malformed.
- `LogEntry` via `bus.LogInfo`/`LogDebug`/`LogWarn`.

## Messages subscribed

- `RegisterScheme` - records `scheme -> handlerPluginId` (scheme lowercased) in an in-memory registry.
- `LoadResource` - parses the URI, dispatches `LoadSchemeResource` to the handler, or replies with
  `UnknownScheme` / `LoadFailed`.
- `WriteResource` - parses the URI, dispatches `WriteSchemeResource` to the handler, or replies with
  `WriteUnknownScheme` / `WriteFailed`.

## Notes

- Scheme-agnostic: it handles no URI scheme itself and reads/writes nothing. The actual load/write is
  performed by handler plugins (e.g. FileSystem for `file://`, Http for `http(s)://`) that register a
  scheme and subscribe to the `LoadSchemeResource` / `WriteSchemeResource` routing messages.
- The registry is last-write-wins per scheme; schemes are compared case-insensitively (lowercased).
- It only verifies that a handler is registered; it does not wait for or correlate the handler's
  result. The handler answers the original caller's `LoadResourceResult` / `WriteResourceResult`
  directly.
