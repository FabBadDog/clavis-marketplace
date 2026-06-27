---
name: environment
pluginId: Environment
version: 1.0.0
apiVersion: 1.0.0
description: Placeholder provider for working directory, git, system metrics, Clavis version and time.
dependencies:
  - { name: placeholders-contracts, version: 1 }
language: csharp
assemblyName: Environment
rootNamespace: FabioSoft.Nucleus.Plugins.Environment
globalUsings:
  - FabioSoft.Nucleus.Contracts
  - FabioSoft.Contracts.Placeholders
---

# Environment

## Purpose

The core placeholder provider. Announces and publishes the `cwd.*`, `git.*`, `sys.*`, `clavis.*` and
`time.*` namespaces so the configurable status line, the contextual window top bar, and markdown panels can
template against them. Pure parsing (path shortening, git output parsing, value assembly) is split from the
impure sampling (git process spawning, system metrics) so the former is unit-tested and the latter is
`[ExcludeFromCodeCoverage]`.

## Config (`EnvironmentConfig`)

- `RefreshSeconds` (default `5`) - seconds between samples; validated to be at least 1.

## Messages published

- `RegisterPlaceholderProvider` - announces the descriptor catalog. Sent on activation and re-sent on every
  `PlaceholdersRequested`.
- `PlaceholderSnapshot` - the current values for every key, broadcast on each timer tick.
- `LogEntry` via `bus.LogInfo`/`LogWarn`.

## Messages subscribed

- `PlaceholdersRequested` - re-announces descriptors and re-publishes a snapshot so aggregating consumers
  catch up regardless of activation order.

## Notes

- `sys.cpu` is the Clavis process CPU averaged over the refresh interval (dependency-free; no
  PerformanceCounter / P/Invoke). `sys.ram` is the system memory load via `GC.GetGCMemoryInfo`. `sys.disk`
  is the working-directory volume.
- Sampling runs on a `System.Threading.Timer` with a re-entrancy guard; a faulted sample is logged and the
  last snapshot stays in place.
- `git.*` is only populated inside a work tree; outside one those keys are absent (and render verbatim).
