---
name: placeholders-contracts
assemblyName: FabioSoft.Contracts.Placeholders
version: 1.0.0
apiVersion: 1.0.0
description: Placeholder provider registration and value-snapshot contracts for the templating engine.
produces: [ FabioSoft.Contracts.Placeholders.dll ]
language: fsharp
rootNamespace: FabioSoft.Contracts.Placeholders
sources:
  - PlaceholderMessages.fs
---

# placeholders-contracts

Cross-plugin contracts for the namespaced placeholder system. A plugin contributes its namespace's tokens
(e.g. `git.*`, `agent.*`, `sys.*`) so the status line and markdown panels can template against them, and the
editor's IntelliSense lists them.

- `PlaceholderDescriptor(key, kind, sample, description)` - one discoverable token, e.g. `git.branch`.
- `RegisterPlaceholderProvider(providerId, descriptors)` - a plugin announces its tokens (catalog +
  IntelliSense). Broadcast on activation and in response to `PlaceholdersRequested`.
- `PlaceholderSnapshot(providerId, values)` - current values keyed by fully-qualified name; broadcast when
  they change. Consumers merge the latest snapshot per provider and re-render.
- `PlaceholdersRequested()` - an aggregating consumer asks providers to re-announce + re-publish, so
  activation order is irrelevant (mirrors `PanelKindsRequested`).

The resolution/parsing grammar lives in the `clavis-placeholders` engine module, not here; this assembly
only carries the bus message types so they share identity across plugin load contexts.
