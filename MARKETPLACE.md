---
name: clavis-marketplace
owner: FabioSoft
---

# clavis-marketplace

The Clavis plugin + module marketplace. Every item is built from source on launch by the
Clavis compiler - there are no prebuilt binaries committed here.

Each item is a single C# or F# project described entirely by its `PLUGIN.md` frontmatter (the single
source of truth - there is no `plugin.json` or generated `marketplace.json`). The catalog is the
filesystem: every immediate child of `plugins/` and `modules/` that carries a `PLUGIN.md` is an item,
and **which of the two folders it sits in determines its kind** - frontmatter carries no `kind` field.

- `plugins/<name>` - a **plugin**: compiled into a collectible load context, reloadable at runtime.
- `modules/<name>` - a **module**: compiled into the Default ALC and produced into `~/.clavis/modules`;
  a change takes effect only on restart. Its `produces` / `privateAssemblies` name the DLLs it promotes.
- `tests/` and each item's co-located `tests/` are the quality gate, run on demand; they are never
  loaded as plugins (they carry no `PLUGIN.md`).

A plugin may declare `essential: true`: it is brought up in startup phase 1, before the host reveals any
UI - everything else compiles and activates in the background after the window is up. Reserve the flag
for plugins the first usable window cannot exist without (currently the window host, the conversation,
the configuration store, and the agent bridge - so the agent layer is live the moment the window
appears, not seconds later). `essential` is a plugin concept; declaring it on a module is a metadata
error (a module's essentiality is derived from the essential plugins' dependencies).

## Versioning and dependencies

There are two version expressions, and they are not the same thing:

- **An item's own `version:`** is a full semver (`2.2.0`) and is the single source of truth for that item's
  version - the project `<Version>` is derived from it by the lifecycle/bump tooling. It bumps Major on a
  breaking surface change, Minor on new public surface, Build otherwise.
- **A dependency's `version:`** is a **bare major** (`clavis-rendering: 2`), meaning "the 2.x line". That is
  the only granularity worth declaring, because that is the granularity the runtime actually binds on: a
  dependency is resolved by loading whatever assembly is on disk and matching its **major**. A `^1.2.0`-style
  range is still accepted, but it implies a minor/patch precision nothing honours - and it silently drifts
  (a producer's major bump never updated its consumers' ranges). Always write the bare major.

Why a wrong dependency major is easy to miss: it is read only by the install resolver (`DependencyResolution`)
and, for ordering, by name - the launch/compile path never reads it, and the runtime binds the on-disk major
regardless. So a stale major causes no symptom at launch, only a failed catalog install later.

`tools/Validate-Dependencies.ps1` is the guard. It builds "item -> the major it ships" from the manifests and
checks every in-marketplace dependency declares that major in bare form; `-Fix` rewrites stale/non-bare ones.
`tools/run-tests.ps1` runs it first, so drift fails the test run. Dependencies on things the marketplace does
not produce (NuGet packages, the core-shipped `fabiosoft-common`/`-process`) are left alone - their major is
not ours to track.
