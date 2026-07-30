---
name: workspaces
pluginId: Workspaces
version: 1.4.1
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
`FleetAgents.cs` pairs agent instances to workspaces and shapes the ones belonging to none into tabs;
`SessionPlan.cs` decides which of the three ways a workspace obtains its session. Both are provider-neutral -
they work off what `AgentInstance` carries, so this plugin names no provider.

## Config

- `WorkspacesConfig.DefaultWorkingDirectory` (default empty) - directory for a workspace that names none;
  empty uses the directory Clavis was launched in.
- `WorkspacesConfig.FleetPollSeconds` (default `15`) - how often to ask what agents are running, which is what
  keeps the tabs for agents started outside Clavis current. Each poll costs a provider process, so this is a
  trade between freshness and cost rather than something to set as low as it will go. `0` disables discovery:
  no fleet tabs, and a workspace whose agent was parked reopens its transcript instead of taking it over.
- The **workspace list itself is not activation config**: it is durable user data in the `Workspaces` section
  of `configuration.yaml` (name, accent, working directory, slot, the provider session id of its conversation,
  plus which one was active), read and written over the bus via `GetConfig`/`SaveConfig`. Only the layout stays
  in the disposable `state.yaml`, so the documented contract finally holds: deleting `state.yaml` loses your
  dockings and keeps your workspaces - and, deliberately, keeps your conversations, which is why the session id
  is here and not there. The run's own session correlation id and the activity are still re-derived each launch,
  and fleet tabs are never written down at all.

## Messages published

- `WorkspaceListChanged` (the whole list, on any change), `WorkspaceActivated`, `WorkspaceSessionStarted`,
  `WorkspaceClosed`.
- Sessions: `StartNewSession`, `ResumeSession`, `AdoptAgentInstance`, `ReleaseAgentInstance`, `DisposeSession` -
  this plugin owns session creation, because the working directory is per workspace, and it owns the choice
  between the three ways of obtaining one.
- `AgentInstancesRequested` - polled, to learn what is running outside Clavis.
- `SaveConfig`, `GetConfig`, `LogEntry`.
- `ShutdownParticipant` / `ShutdownPrepared` - it has work to do on the way out (see Notes).
- `PanelKindRegistration` (`workspace-overview`, one-per-application) and `DefaultBindingsDeclared` (F1-F11 ->
  activate-or-create by slot, F12 -> toggle the overview).

## Messages subscribed

- `ConfigResult` (its own section), `RequestWorkspaces` (the late-subscriber pattern).
- `ActivateWorkspace`, `ActivateWorkspaceSlot`, `CreateWorkspace`, `CloseActiveWorkspace`, `CloseWorkspace`,
  `RenameWorkspace`, `ForceTakeOver`.
- `SessionActivityChanged` - activity is a property of a session; the session -> workspace mapping is this
  plugin's job.
- `SessionReady` - carries the provider's own session id, which is the durable half of a workspace's session.
- `AgentInstancesAvailable`, `AgentInstanceAdopted`, `AgentInstanceAdoptionFailed`,
  `AgentInstanceAdoptionWaiting`, `AgentInstanceReleased` - the take-over and hand-back lifecycle.
- `ShutdownPreparing` - the cue to park every agent.

## Notes

- **The slot is a stable address, not an ordinal.** Closing a workspace **frees its slot and renumbers
  nothing**: slot 3 stays slot 3 forever, so the key you learned never moves under you and the bar shows a
  gap. A new workspace takes the lowest free slot. `ActivateWorkspaceSlot` is therefore
  **activate-or-create** - pressing an unused F-key is the primary way a workspace comes into being, so it is
  never a no-op. F1-F11 is the cap by construction; a workspace created when every slot is taken gets slot 0
  and is reachable by click or from the overview.
- **Sessions start lazily and stay alive.** A session is obtained on a workspace's first *activation*, never at
  creation or at load, so restoring eight workspaces does not spawn eight agent processes at launch. Once
  started it keeps running in the background - which is what makes the activity indicator worth having.
- **There are three ways to obtain a session, and the priority between them matters** (`SessionPlan`). If an
  agent is running this workspace's conversation, it is **taken over**; else if the workspace remembers a
  conversation, it is **reopened** from its transcript; else one is **started fresh**. A running agent always
  wins, because resuming a conversation an agent still holds is refused by the provider outright - and if it
  were not, it would fork the conversation and lose whatever the agent has done since.
- **Obtaining the first session waits to learn what is running - the activation does not.** Which of the three
  applies depends on the answer, and deciding before it arrives would always read as "nothing is running": the
  parked agent would be left running while its transcript was reopened separately, giving one conversation two
  lives and orphaning the agent. The wait is bounded (`InitialDiscoveryWaitSeconds`), because a provider that
  never answers must not leave Clavis with no chat, and giving up early only costs a take-over.
  **Only the session is held.** Delaying the activation too was a real regression, found on the first launch:
  consumers bind their per-workspace state to whichever workspace is active when they restore, so with none
  active yet `wpf-host` restored its layout against the empty workspace id and every panel silently vanished
  from every tab. Being the active workspace needs no discovery answer; choosing a session route does.
- **A parked agent is found by name, not by id.** Handing a session back gives the new background agent a *new*
  provider id, and the spawn is fire-and-forget, so that id is never observed. Reclaiming therefore matches on
  the workspace's name plus its working directory (`FleetAgents.ParkedFor`), the two things Clavis controls. An
  ambiguous match reclaims nothing: attaching a workspace to the wrong conversation is worse than attaching it
  to none, and the candidates still appear as fleet tabs so nothing is hidden.
- **Agents running outside Clavis appear as slotless tabs.** Discovery is polled, and every background agent no
  workspace claims becomes a tab with no F-key, no accent and no persistence, drawn distinctly. Activating one
  takes it over and **promotes** it to a real workspace, at which point it gains a slot and is persisted. They
  are slotless on purpose: a short-lived agent somebody spawned must not consume one of eleven keys, and it is
  not a place of yours until you adopt it.
- **Quitting parks every agent, and the quit waits for it.** This plugin declares itself a `ShutdownParticipant`
  and, on `ShutdownPreparing`, hands every live session back with `ReleaseAgentInstance(keep-running)`, answering
  `ShutdownPrepared` once each has confirmed. The wait is not decoration: handing back *spawns* a process, which
  has to happen while Clavis is still alive to spawn it, and `ApplicationShutdown` takes effect immediately.
  A hand-off that never confirms is bounded by the host's grace period rather than a second timeout here.
  Fleet tabs are skipped - their agents were never Clavis's to hand back.
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
