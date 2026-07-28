---
name: workspaces
pluginId: Workspaces
version: 1.3.0
essential: true
apiVersion: 1.0.0
description: The single authority for workspace identity, activation, and the workspace list.
dependencies:
  - { name: workspace-contracts, version: 2 }
  - { name: session-contracts, version: 3 }
  - { name: services-contracts, version: 1 }
  - { name: keymap-contracts, version: 1 }
  - { name: layout-contracts, version: 2 }
  - { name: host-contracts, version: 3 }
  - { name: clavis-rendering, version: 3 }
  - { name: yamldotnet, version: 1 }
language: csharp
assemblyName: Workspaces
rootNamespace: FabioSoft.Nucleus.Plugins.Workspaces
useWpf: true
globalUsings:
  - FabioSoft.Contracts.Workspace
  - FabioSoft.Contracts.Session
  - FabioSoft.Contracts.Services
  - FabioSoft.Contracts.Keymap
  - FabioSoft.Contracts.Layout
  - FabioSoft.Contracts.Host
---

# Workspaces

## Purpose

A **workspace** is one AI chat plus its own panel set: its own agent session, working directory, docking
layout and agent axes. This plugin is the single authority for that identity - which workspaces exist, what
each is called, its accent, its working directory, its slot, its live session, and its derived activity - and
broadcasts the whole list as `WorkspaceListChanged`. It owns **no window and no chat state**; every consumer
reacts to the list.

Rejected homes for this: the kernel (core knows nothing - this is application policy) and `wpf-host`, whose
own comments hold the invariant "the host knows no session vocabulary".

## Location

`plugins/workspaces/` - a **UI plugin** (`UseWPF`), because it owns the F12 overview panel; everything else
about it is headless. Pure core in `WorkspaceSet.cs` (state, slot
arithmetic) and `WorkspaceUpdate.cs` (every operation, returning the new set plus effects);
`WorkspacesPlugin.cs` is the impure shell that turns effects into bus messages and persistence.
`WorkspaceFile.cs` owns the YAML, `AccentPalette.cs` the accent assignment, `WorkspaceBindings.cs` the declared
F-key defaults, and `WorkspaceOverviewRows.cs` the pure row projection behind `Views/WorkspaceOverviewView.cs`.

## Config

- `WorkspacesConfig.DefaultWorkingDirectory` (default empty) - directory for a workspace that names none;
  empty uses the directory Clavis was launched in.
- The **workspace list itself is not activation config**: it is durable user data in the `Workspaces` section
  of `configuration.yaml` (name, accent, working directory, slot, plus which one was active), read and written
  over the bus via `GetConfig`/`SaveConfig`. Only the layout stays in the disposable `state.yaml`, so the
  documented contract finally holds: deleting `state.yaml` loses your dockings and keeps your workspaces.
  Nothing live is persisted - sessions and activity are re-derived each launch.

## Messages published

- `WorkspaceListChanged` (the whole list, on any change), `WorkspaceActivated`, `WorkspaceSessionStarted`,
  `WorkspaceClosed`.
- Sessions: `StartNewSession`, `DisposeSession` - this plugin owns session creation, because the working
  directory is per workspace.
- `SaveConfig`, `GetConfig`, `LogEntry`.
- `PanelKindRegistration` (`workspace-overview`, one-per-application) and `DefaultBindingsDeclared` (F1-F11 ->
  activate-or-create by slot, F12 -> toggle the overview).

## Messages subscribed

- `ConfigResult` (its own section), `RequestWorkspaces` (the late-subscriber pattern).
- `ActivateWorkspace`, `ActivateWorkspaceSlot`, `CreateWorkspace`, `CloseActiveWorkspace`, `CloseWorkspace`,
  `RenameWorkspace`.
- `SessionActivityChanged` - activity is a property of a session; the session -> workspace mapping is this
  plugin's job.

## Notes

- **The slot is a stable address, not an ordinal.** Closing a workspace **frees its slot and renumbers
  nothing**: slot 3 stays slot 3 forever, so the key you learned never moves under you and the bar shows a
  gap. A new workspace takes the lowest free slot. `ActivateWorkspaceSlot` is therefore
  **activate-or-create** - pressing an unused F-key is the primary way a workspace comes into being, so it is
  never a no-op. F1-F11 is the cap by construction; a workspace created when every slot is taken gets slot 0
  and is reachable by click or from the overview.
- **Sessions start lazily and stay alive.** A session is spawned on a workspace's first *activation*, never at
  creation or at load, so restoring eight workspaces does not spawn eight agent processes at launch. Once
  started it keeps running in the background - which is what makes the activity indicator worth having.
- **Closing the last workspace leaves an empty set**, not a refusal. That is the honest consequence of
  separating "close this workspace" (`exit`) from "quit the application" (`ExitApplication`), and the bar is
  still a way back in.
- **Accents are theme resource keys, never baked colours** (`AccentPalette`), so an assignment survives a
  restart and stays re-themable. The palette is drawn from the design language's **identity** family, never
  the signal colours - green/yellow/red mean something. Correspondingly the accent and the activity dot are
  separate marks: the dot carries activity, the accent carries identity, and tinting the dot with the accent
  would destroy the activity signal.
- **The overview is an ordinary panel kind** (`workspace-overview`, one-per-**application** - it is a view *of*
  all the workspaces). It therefore inherits open/toggle/close/restore/persist/tear-off/Esc and a palette
  command for free, and F12 is literally `TogglePanel workspace-overview`. A bespoke chromeless overlay was
  rejected: it would be the third overlay mechanism after the shortcut-help overlay and slide-ins. Rows come
  from the pure `WorkspaceOverviewRows` (slot order with gaps preserved, coarse elapsed time, an idle workspace
  showing none). Model/effort and context fill are **not** shown yet - this plugin does not hold session
  detail, and plumbing it here would duplicate what the placeholders already carry.
- **Shortcuts are declared, not hardcoded elsewhere.** `WorkspaceBindings` declares F1-F11 and F12 to the
  keymap via `DefaultBindingsDeclared`, so the shortcuts ship with the feature. Application scope, never
  system: a system binding registers an OS global hotkey, which would take F1-F12 from every application on the
  machine.
- **Persistence is skipped for activity.** An activity change re-announces the list but never writes the file,
  so a streaming turn does not rewrite `configuration.yaml` four times a second.
