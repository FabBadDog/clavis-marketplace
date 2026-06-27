---
name: services-contracts
assemblyName: FabioSoft.Contracts.Services
version: 1.0.1
apiVersion: 1.0.0
description: Per-plugin config and state service contracts.
produces: [ FabioSoft.Contracts.Services.dll ]
language: fsharp
rootNamespace: FabioSoft.Contracts.Services
sources:
  - ConfigMessages.fs
  - StateMessages.fs
---

# services-contracts

Per-plugin service messages backed by the Configuration plugin.

- **Config** (`ConfigMessages.fs`) - durable per-plugin configuration: `GetConfig` ->
  `ConfigFound`/`ConfigNotFound` (`ConfigResult`); `SaveConfig` -> `ConfigSaved` + `ConfigChanged`.
- **State** (`StateMessages.fs`) - disposable per-plugin runtime state (window bounds, docking layout,
  panel state): `GetState` -> `StateFound`/`StateNotFound` (`StateResult`); `SaveState` (fire-and-forget).
  Same request/reply shape as config, but the store behind it can be deleted without losing configuration,
  so a plugin must start cleanly on `StateNotFound`.
