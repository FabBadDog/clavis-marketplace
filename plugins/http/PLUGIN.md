---
name: http
pluginId: Http
version: 1.0.120
apiVersion: 1.0.0
description: http(s):// resource read.
projectFile: ./Http.csproj
dependencies:
  - { name: resource-contracts, version: 1 }
---

# Http

## Purpose

The resource handler for the `http://` and `https://` URI schemes. It is an HTTP *client* for resource
loading (not a server): it registers both schemes with the ResourceBroker on activation, then handles
routed load requests by fetching the URL over a shared `HttpClient`. Writing is not supported - write
requests are answered with a failure.

## Location

`src/plugins/Http/`

## Config (`HttpConfig`)

Empty record - no configurable fields.

## Messages published

- `RegisterScheme("http", "Http")` and `RegisterScheme("https", "Http")` - announces this plugin as the
  handler for both schemes on activation.
- `ResourceLoaded(IResource)` (as `LoadResourceResult`) - on a load; the `IResource` is an
  `HttpResource` that calls `HttpClient.GetStreamAsync` on demand.
- `LoadFailed(message)` (as `LoadResourceResult`) - when constructing the resource throws.
- `WriteFailed("HTTP scheme does not support writing")` (as `WriteResourceResult`) - for any write
  request against an http(s) URI.
- `LogEntry` via `bus.LogInfo`/`LogWarn`/`LogError`.

## Messages subscribed

- `LoadSchemeResource` - ignores schemes other than `http`/`https`; otherwise replies with
  `ResourceLoaded` wrapping an `HttpResource`.
- `WriteSchemeResource` - for `http`/`https` schemes replies with `WriteFailed`; writing is
  unsupported.

## Notes

- Handles `http://` and `https://`. Read-only: writes always fail.
- The fetch is lazy - `ResourceLoaded` returns immediately; the network request happens when the
  consumer calls `IResource.OpenAsync`, which streams the response body via `GetStreamAsync`.
- A single `HttpClient` is shared for the plugin's lifetime and disposed when the plugin unloads.
- It filters routed messages by scheme defensively even though the broker only routes http(s) requests
  to it, so it coexists safely with other scheme handlers on the same bus.
