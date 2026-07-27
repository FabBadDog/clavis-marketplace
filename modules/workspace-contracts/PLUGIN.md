---
name: workspace-contracts
assemblyName: FabioSoft.Contracts.Workspace
version: 2.1.0
apiVersion: 1.0.0
description: Workspace identity, activation, and the workspace list.
produces: [ FabioSoft.Contracts.Workspace.dll ]
language: fsharp
rootNamespace: FabioSoft.Contracts.Workspace
sources:
  - WorkspaceMessages.fs
---

# workspace-contracts

The cross-plugin messages for **workspaces**: one AI chat plus its own panel set, working directory, agent
session and agent axes. The `Workspaces` plugin is the single authority for that list; every other plugin
reacts to it.

## Why major 2

The name was previously used for the multi-window and dockable-panel protocol, which is now
`layout-contracts` (it was never about a workspace - it is about where things are on screen). Reusing the
freed name at **major 2** is a deliberate safety mechanism: the retired
`~/.clavis/modules/FabioSoft.Contracts.Workspace.dll` v1 may still be on disk, and the kernel's Default-ALC
resolver binds by **major**, so a major-2 reference cannot silently bind to the leftover v1 and resolve old
types that then fail to dispatch. It fails loudly at load instead.

## The slot is an address, not an ordinal

`WorkspaceInfo.Slot` is a stable key, not a position: closing a workspace **frees its slot and renumbers
nothing**, so slot 3 stays slot 3 forever and the shortcut you learned never moves under you (the bar shows
a gap). A new workspace takes the lowest free slot. `ActivateWorkspaceSlot` is therefore
**activate-or-create**: pressing an unused F-key is the primary way a workspace comes into being, so it is
never a no-op.
