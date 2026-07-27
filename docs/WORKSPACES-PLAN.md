# Workspaces

## Context

Clavis today runs exactly one AI chat. The conversation is a hardcoded app-wide singleton
(`WindowHost.ConversationPanelId` is one fixed Guid, `ConversationPanelView` is one field), panel kinds
dedupe app-wide, and there is one docking layout for the whole application. Working on two things at once
means either restarting the session or launching a second Clavis process against a different `CLAVIS_HOME`.

A **Workspace** fixes that: one AI chat plus its own panel instances. Per workspace it owns its own agent
session, its own **working directory**, its own **panel set + docking layout**, and its own **agent axes**
(model / mode / effort). A chromeless bar across the top of the screen lists every workspace with its name,
a randomly assigned accent colour, and an activity indicator (no activity / working / waiting for input).
Switch by clicking an entry or with **F1-F11**; **F12** opens an overview of all sessions.

There is no prior art for this in either repo - it has not been designed before. The word "workspace",
however, is already taken by the multi-window + docking-panel protocol, so that gets renamed first.

Intended outcome: several concurrent pieces of work live side by side in one Clavis instance, each with its
own directory, panels and model, and the bar tells you at a glance which one needs you.

> **This is the live plan for the Workspaces feature - start here to pick the work back up.** It spans this
> repo and the **clavis** core repo. Keep the status table below current as packages land; it is the only
> record of where the work is.

## Where this stands (update as packages land)

Everything below is pushed; both repos are clean. Marketplace = `~/.clavis/marketplaces/clavis-marketplace`
(commit straight to `main`), host = `~/Repos/FS/clavis` (also `main`; a background session must edit it via
`EnterWorktree` and then push the commit to `main`).

| Package | State | Commits |
|---|---|---|
| WP-1 bar mockup | done | `e30d798` (marketplace) |
| WP0 `layout-contracts` rename | done, runtime-verified | `98de211` |
| WP1 `WindowManager` split + `LayoutTree` | done | `d9895a7` |
| WP2 session activity + session ids | done | `8a39514`, `06e1ad9`, `50a8156` |
| Host: WP0 fallout + BuildSpec cache key | done | `967c0f2`, `2c21571`, `81479a2` (host repo) |
| WP3 chat becomes a panel kind | done, **not runtime-verified** | `76c5078` |
| WP4 chats aggregate | done, **not runtime-verified** | `db7c40d` |
| WP5 | **next** | - |
| WP5b, WP6 - WP10 | not started | - |

**Not runtime-verified since WP2.** Four contract majors have moved now (session, host x2, layout) and
nothing has booted against them. WP3 in particular changes what the primary window contains, so it needs a
launch. The next launch also recompiles every item once (new `.buildspec` sidecars). Launch with
`dotnet run --project src/FabioSoft.Clavis.Shell` from the host repo, in the background, and confirm via the
newest `~/.clavis/logs/clavis-*.log`.

**First-launch checks specific to WP3** (all cheap, all worth doing before WP4 builds on it):
1. The chat appears at all - over the existing `state.yaml`, whose primary window holds a slot of the retired
   kind `conversation`. `WpfHostConfig.RetiredPanelKinds` should rewrite it to `chat` on restore; if that
   misfires the symptom is a tab stuck on its compile placeholder forever and no chat.
2. Prompt submit still works (the panel now owns Enter), and Up/Down still recall history.
3. Trigger a permission: Left/Right move the choice and Enter confirms, with the caret still in the prompt.
4. `Ctrl+Up`/`Ctrl+Down` still scroll the chat while the prompt holds focus (the binding moved to panel kind
   `chat`).
5. Close the chat tab, then `TogglePanel chat` (palette `open-chat`, or `Ctrl+P` -> Chat) brings it back.
6. Tear the chat off into a second window - the prompt must travel with it.

### Local environment quirks (this development machine)

- **`dotnet` on PATH is the wrong SDK.** It resolves to a private 8.0 SDK; this repo needs 10.0. Prefix every
  dotnet command with `$env:PATH = "C:\Program Files\dotnet;$env:PATH"; $env:DOTNET_ROOT = "C:\Program Files\dotnet"`.
- **`FabioSoft.TestHost` is not in `Clavis.sln`** (the Shell builds it via an MSBuild task), so a solution
  restore misses it and a fresh clone/worktree fails to build the Shell with `NETSDK1004`. Fix with
  `dotnet restore src/tools/FabioSoft.TestHost`.
- **`dotnet test` takes one project at a time** - passing two is `MSB1008`.
- PowerShell here-strings get mangled through the shell tool; write files with the file tool instead. And
  `Set-Location` does not change .NET's process cwd, so `[System.IO.File]` calls need absolute paths.

### Pre-existing problems, none caused by this work

- **`yamldotnet -> YamlDotNet` declares major 18 but ships 1.** `Validate-Dependencies.ps1` fails on it, which
  also makes `tools/run-tests.ps1` throw before running anything. Do **not** let `-Fix` "correct" it.
- **`tests/integration` is unbuildable.** All eight of its `ProjectReference`s point at `.csproj` files that
  do not exist (plugins are source-compiled from `PLUGIN.md`), so the suite has been dead since the
  marketplace split. Its sources are still edited for correctness, but never compile-verified.
- The recurring `MarketplaceProgress`/`MarketplaceFailed reason=no-subscriber` dead letters in the logs are
  the background watcher, tied to the yamldotnet drift.
- `KernelTests.a plugin load context is reclaimed...` fails in a **fresh worktree** without a full solution
  restore; it passes in the main checkout. Not a code fault.

## Decisions taken

- **Rename the existing concept**: `workspace-contracts` -> `layout-contracts`; the freed name is reused for
  the new feature at **major 2** (deliberate - see WP0).
- **The prompt input moves into the chat panel.** Each chat owns its own prompt box. Bigger change, but it
  makes the chat panel self-contained and is a precondition for two chats visible at once later.
- **Sessions start lazily and stay alive.** A workspace starts its session on first activation and keeps it
  running in the background - that is what makes the bar's activity indicator meaningful.
- **Full feature, phased.** Every work package leaves the app working and committable.

## Two questions answered up front

**One bus, not a bus per workspace.** The bus is a Nucleus core primitive: the kernel creates one and
injects `IBus` into each plugin at activation. Plugins are activated once, as singletons - `WpfHost` is one
object owning all windows, `Conversation` one object owning all chats - so a per-workspace bus would have no
distinct subscriber set to serve. Splitting it would fragment the single-subject infrastructure (the
dead-letter Rx subject, the bootstrap replay buffer, the EventsPanel firehose, `LogSink`'s per-launch log)
and force bridging for every inherently global fact (the workspace list, the bar, activity aggregation). The
addressing already exists: `SessionId` sits on the `AgentStreamEvent` base, `claude-bridge` already does
`sessions.TryGetValue(message.SessionId, ...)`, and WP3/WP6 add `workspaceId` to the panel/window messages.
Back-pressure isolation - the one real argument for separate buses - is already handled by the Bus's
**per-subscriber bounded channels**. Genuine fault isolation would be an ALC or process boundary, not a bus.

**The bar does not become `Application.Current.MainWindow`.** MainWindow is load-bearing for popup
placement: `SelectorWindow.ShowSelector()` parents to it and positions at
`owner.Left + (owner.ActualWidth - Width) / 2`, `owner.Top + 80`, and `ConfirmDialog` uses `CenterOwner` - so
a ~30 px strip as MainWindow would jam the command palette, panel picker, model picker and every ask-user
selection against the top edge, sized against the bar. Owned windows also hide/minimize with their owner, so
the bar neither owns the primary nor is owned by it, and its `Closing` must not send `ApplicationShutdown`.
`ShutdownMode=OnExplicitShutdown` means nothing depends on MainWindow for process lifetime anyway.

The question underneath - *what is the app's persistent presence?* - is deliberately deferred. For v1 the
primary stays MainWindow and the shutdown trigger, because the existing summon/hide flow (`Ctrl+Shift+V`,
with the bar skipped by `HideAll`) already delivers "everything out of the way, bar still visible". Making
the primary closeable while the bar lives on changes the whole lifetime model and is its own decision.

## Architecture

```
Workspaces  -- WorkspaceActivated ------>  WpfHost      (swap docking surfaces, show/hide that workspace's windows)
            -- WorkspaceActivated ------>  Conversation (switch which chat it projects)
            -- WorkspaceActivated ------>  Selection    (retarget the model/mode/effort pickers)
            -- StartNewSession --------->  ClaudeBridge (unchanged - already N-session ready)
Conversation -- SessionActivityChanged ->  Workspaces   (fills the bar's indicator)
```

Acyclic, every arrow a one-way fact. A new **essential** plugin `plugins/workspaces` is the single
authority for workspace identity, name, accent, working directory, ordinal, live session id, and derived
activity. It owns no window and no chat state.

Rejected: putting workspaces in the kernel (core knows nothing - it is application policy), and in
`wpf-host` (whose own comments hold the invariant "the host knows no session vocabulary", and whose
`WindowManager.cs` is already 1561 lines).

**The host repo needs no change.** The only `workspace` hits in `clavis/src/` are an unrelated comment and a
Roslyn assembly name.

Note `plugins/claude-bridge` needs **no change at all** - it is already
`ConcurrentDictionary<Guid, Session>` + `ConcurrentDictionary<Guid, SessionAxes>`.

---

## WP-1 - Bar mockup first (design, no code) - DONE

Nothing is implemented until the bar's visual design is settled. Author
`docs/design/mockups/clavis-workspace-bar.html` in the **clavis-marketplace** repo and open it in Chrome
(`Start-Process chrome <file>`). Per the marketplace CLAUDE.md, mockups are browser HTML in that folder -
never a Claude artifact.

House style, copied from the newest mockup `clavis-session-status-refined.html`: one standalone file; the
Google Fonts `<link>` for Inter + JetBrains Mono + Rajdhani; the verbatim `:root` token block (`--bg #0A0A0F`,
`--panel-deep #0E0E14`, `--rail #1B1B22`, `--raised #36363F`, `--line #282834`, `--frame #5A5A66`,
`--text #C8C8D0`, `--text-bright #E8E8EC`, `--text-dim #9A9AA4`, `--clavis #9FD5F0`, `--human #ADA6F2`,
`--green #7BD49B`, `--yellow #E4C47E`, `--red #E47E7E`, plus `--agent-font`/`--ui-font`/`--mono-font`);
`.wrap` at 960px; `.eyebrow` -> `h1` with `.accent` -> `p.lede`; one `<section>` per idea opening
`<h2><span class="idx">NN</span> ...</h2>` + `p.section-note`; every specimen inside
`.preview` > `.caption` ("shown in - `<span class="loc">`") > `.stage`; `@keyframes` inline under a
`/* ---- NN - Name ---- */` banner; a `@media (prefers-reduced-motion: reduce)` block killing every
animation; a mono `<footer>` stating provenance. `border-radius: 50%` on dots only - no rounded rectangles.
Do **not** copy `CLAVIS Workspace.html`: it links a missing `clavis-tokens.css`, pulls React from a CDN, and
uses unsanctioned fonts (EB Garamond, Satoshi) - it renders broken today.

Sections to show, at real size (bar height ~30 px, full screen width):

1. **The bar at rest** - four workspaces, one active. Per entry: the F-key hint (`micro`, Rajdhani 9,
   `text-dim`), the activity dot (an `Ellipse`, 7-9 px), the workspace name (`label`, Rajdhani 11 uppercase,
   1.5 px tracking), and the accent. Active entry brightens to `--text-bright`; inactive sits at
   `--text-dim`.
2. **Where the accent goes** - two or three variants to choose between: a 2 px accent edge on the active
   entry (matching the existing active-tab treatment) vs the dot itself carrying the accent vs a 1 px
   underline. The design language forbids tinting a large area with an accent, so the entry background stays
   `--panel-deep` in every variant.
3. **The three activity states** side by side, per the "pulsing is reserved for activity" rule:
   **idle** = steady dim (`--text-dim`), **working** = breathing (`--green`, the 600 ms
   `Motion.BreathingDuration` oscillation), **waiting for input** = steady accent (`--clavis`). Waiting is
   the most urgent state and must *not* pulse, so it draws the eye by colour - show this reads correctly
   next to a breathing neighbour.
4. **A crowded bar** - eight or more workspaces, to check the name-truncation and spacing behaviour, plus
   what an entry past F11 looks like (no key hint).
5. **The F12 overview** as a top-edge slide-in beneath the bar - rows with name, accent, working directory,
   model/mode/effort, activity + elapsed, queued count, context fill.

The chosen accent treatment and dot mapping feed directly into WP5 (`AccentPalette.cs`,
`Theme/Styles.xaml` accent keys) and WP7 (`ActivityDot`, `WorkspaceBarRow`).

---

## WP0 - Rename to `layout-contracts` (mechanical, atomic) - DONE

`modules/workspace-contracts` is not about a workspace; it is about where things are on screen (windows,
docked panels, slide-ins, snapshots). `layout` covers all four.

| Old | New |
|---|---|
| `modules/workspace-contracts` | `modules/layout-contracts` (`1.0.0`) |
| `FabioSoft.Contracts.Workspace` | `FabioSoft.Contracts.Layout` |
| `WorkspaceMessages.fs` (230 lines) | `PanelMessages.fs`, `WindowMessages.fs`, `LayoutSnapshot.fs` |
| `WorkspaceSnapshotRequested`/`WorkspaceSnapshot` | `LayoutSnapshotRequested`/`LayoutSnapshot` (`WindowSnapshot`/`PanelSnapshot` keep their names) |
| `plugins/wpf-host/WorkspaceStore.cs` | `plugins/wpf-host/LayoutFile.cs` (matches `KeymapFile.cs`/`MarkdownPanelFile.cs`; "Store" is role-flavoured) |
| `WorkspaceLayout` record | `PersistedLayout` |
| `CaptureWorkspace`/`SaveWorkspace`/`RestoreSavedLayout` | `CaptureLayout`/`SaveLayout`/`RestoreLayout` |
| MCP tool `workspace_snapshot` | `layout_snapshot` (agent-visible; `ClavisDocs.cs` prose too) |

`WindowManager` is **not** renamed - CLAUDE.md says keep consistency where the violation already exists.

**The freed name is reused at major 2.** New `modules/workspace-contracts` version `2.0.0`, assembly
`FabioSoft.Contracts.Workspace`. This is a safety mechanism: the old
`~/.clavis/modules/FabioSoft.Contracts.Workspace.dll` v1.0.1 stays on disk and the kernel's Default-ALC
resolver **binds by major**, so a major-2 reference cannot silently bind to the leftover v1 - it fails
loudly at load instead of resolving old types that then fail to dispatch.

**One atomic commit** across the module + all 12 dependents - a half-renamed working copy is the dangerous
state, because a plugin still naming the old namespace compiles against the leftover DLL and then silently
fails to dispatch:

1. `git mv modules/workspace-contracts modules/layout-contracts`, split the file, rewrite the namespace,
   set `assemblyName: FabioSoft.Contracts.Layout`, `version: 1.0.0`.
2. Sweep 12 `PLUGIN.md`s: `{ name: workspace-contracts, version: 1 }` -> `{ name: layout-contracts, version: 1 }`
   and `globalUsings: FabioSoft.Contracts.Workspace` -> `...Layout`. Dependents keep `version: 1`.
3. Sweep sources for `FabioSoft.Contracts.Workspace` (only `agent-gateway/ClavisTools.cs` fully qualifies it).
4. `tools/Validate-Dependencies.ps1`, then `dotnet run --project src/tools/FabioSoft.Clavis.CompileTest`.
5. **Prune** `~/.clavis/modules/FabioSoft.Contracts.Workspace.dll` and its `.staging` before the first
   launch. Nothing prunes retired module DLLs - note this as a core follow-up.

**No persistence change in WP0.** The rename keeps the layout v1 shape and `CurrentVersion = 1` byte for
byte, so an existing `state.yaml` keeps loading and the commit is provably behaviour-neutral. The v2 shape
and `LayoutMigration.FromVersion1` belong in **WP6**, where per-workspace layouts actually exist - a
migration that wraps the old window list "into a workspace" cannot be written before workspaces do. Keep the
state section id `WpfHost` throughout.

**Durable vs disposable (WP5, not here).** The workspace *list* (name, accent, working directory, axes, slot)
is durable user intent -> `configuration.yaml`, `Workspaces` section, owned by the new plugin. Only the
*layout* stays in `state.yaml`. Then the documented contract finally holds: deleting `state.yaml` loses
dockings, keeps workspaces.

Bumps: `layout-contracts 1.0.0`; patch bump on every dependent; `agent-gateway` major (agent-visible tool rename).

Tests: rename the existing `WorkspaceStoreTests` to `LayoutFileTests` - no new coverage here, the point is
that the same assertions still pass against the renamed type.

---

## WP1 - Decompose `WindowManager.cs` (pure refactor, no behaviour change) - DONE

It was 1561 lines (3x the guideline) and must not grow. Split **before** any workspace code lands.

**Deviation from the original design, deliberately.** The first draft moved groups of members into separate
collaborator classes (`PanelPlacement`, `CrossWindowDrag`, `WindowVisibility`, ...). Doing that means
threading ~25 private fields (`_windows`, `_bus`, `_pendingRestore*`, `_kindPlacement`, the reveal flags, ...)
into new objects - and that is precisely where behaviour changes hide. WP1's contract is *no behaviour
change*, so `WindowManager` stays one class **split across partial files by concern**, which achieves the
file-size goal at zero risk. Real collaborator extraction can follow later, once the workspace work has
settled and there is coverage to protect it. (The old plan also wanted a `LayoutSnapshot.cs`, which would now
read as the contract type of that name - another reason to prefix the partials with `WindowManager.`.)

| File | Contains | Lines |
|---|---|---|
| `WindowManager.cs` | fields, records, ctor, `Register`, `OtherWindowRects`, `ResolveWindow`, `GetPrimary`/`GetFocused`/`OrderedWindows`, `Dispose` | 263 |
| `WindowManager.Subscriptions.cs` | the whole `SubscribeToBus` routing table | 213 |
| `WindowManager.Snapshot.cs` | `BuildSnapshot` | 54 |
| `WindowManager.Visibility.cs` | the transition guard, `RevealWhenReady`/`StartRevealFailsafe`/`Reveal`, `Summon`, `ToggleVisibility`, `HideAll`, `ShowWithFade` | 204 |
| `WindowManager.Windows.cs` | `CreatePrimaryWindow`, `RecreateSecondaryWindow`, `NewSecondaryHost`, `LinkToPrimaryOwner`, `CloseSecondaryWindow`, `CloseIfEmptySecondary` | 105 |
| `WindowManager.LayoutRestore.cs` | `OnStateResult`, `RestoreSavedLayout`, `RestoreLayout`, `ResolveRestoreView`, `FlushRestoreSends`, `ScheduleSave`/`SaveLayout`/`CaptureLayout`/`CaptureWindow`, `ApplyBounds`/`IsOnScreen`/`BoundsOf` | 203 |
| `WindowManager.Panels.cs` | `SeedDefaultSlidePlacement`, `PlacePanel`, `FindLiveInstance`, `RevealInstance`, `BringToFront`, `TogglePanel`, `CloseActivePanel`, `ClosePanel`, `CloseSlideInPanel`, `RetitlePanel`, `PlaceFresh` | 280 |
| `WindowManager.CrossWindowDrag.cs` | `MovePanelAcrossWindows`, `ResolveCrossWindowDrop`, `TearOffToNewWindow`, `PositionAtCursor`, hint + hit-test helpers | 189 |
| `WindowManager.Placeholder.cs` | `CreatePlaceholderView`, `DisposePlaceholder` | 85 |
| **`LayoutTree.cs`** (`public static`, no WPF) | `EnumerateSlots`, `EnumerateSlotsWithVisibility`, `FoldState`, `IsCenterWithin` | 95 |

`LayoutTree.cs` is the load-bearing part: those walks were `private static` inside a WPF-bound class and
therefore untestable. `FoldState` takes the panel-state lookup as an argument instead of reading `_panelState`,
so folding is a pure function of (tree, state). `IsOnScreen` keeps reading `SystemParameters` but delegates
its arithmetic to `LayoutTree.IsCenterWithin(state, left, top, width, height)`, which *is* deterministic -
the desktop bounds are passed in, so an unplugged-monitor case is testable without a real monitor.
Per the existing convention (`FocusRing`, `WindowSnap` are `public static`; `WorkAreaMaximize`,
`WindowSnapBehavior` are `internal`), pure tested logic is public - `InternalsVisibleTo` is forbidden.

Verification beyond the compile: concatenating the partials' class bodies and diffing against the original
body showed the **only** differences are the five call sites that gained a `LayoutTree.` prefix and the
`IsOnScreen` delegation. Everything else is byte-identical, which is what "no behaviour change" should mean.

Tests: `LayoutTreeTests` - 12 cases (nested-tree enumeration order, empty leaf, active-tab tagging, state
folded per slot with a miss defaulting to `""`, shape preserved through a fold, and a theory over the
on-screen predicate including an unplugged monitor at negative coordinates). wpf-host suite 42 -> 54.
`LayoutMigrationTests` moves to WP6 with the v2 shape.

---

## WP2 - Session activity + session ids - DONE

Landed in three commits, resequenced so the additive work shipped before the breaking work:
`8a39514` (contract + pure projection, session-contracts 2.2.0 - no edge moved),
`06e1ad9` (the per-session transition publisher),
`50a8156` (the breaking half: session-contracts 3.0.0, host-contracts 2.0.0, 17 edges, 10 items).

**Two pieces were deliberately deferred to WP5, both for the same reason - they need a signal that does
not exist yet, and building them now would produce machinery that looks live but cannot work:**

1. **`PendingSelectionId` on `SessionState` + the conversation's `SelectionRequested`/`SelectionCompleted`
   subscription.** `SelectionRequested.SessionId` is `Guid.Empty` in practice, so the subscription could
   never match a session. The field is on the contract (the choke point); the wiring waits for a
   session-aware gateway.
2. **Per-session capabilities in the Selection plugin.** Turning the single `volatile AgentCapabilities`
   into a `ConcurrentDictionary<Guid, AgentCapabilities>` needs `WorkspaceActivated` to say which session is
   visible. Without it the pickers still fall back to "whichever session reported last" - today's behaviour
   with extra storage no reader uses. Do it in WP5, where the signal arrives.

**Also note:** the 17 edges were re-pointed by hand, *not* with `Validate-Dependencies.ps1 -Fix`, because
`-Fix` would also rewrite the pre-existing `yamldotnet -> YamlDotNet` drift (declares 18, ships 1) - an
unrelated problem that should not be resolved as a side effect. Validator now reports 72/72 correct with
only that edge outstanding.

### Original scope

Fix the session-less smells rather than working around them.

- **`ConversationStateChanged`: delete.** Zero producers, zero consumers, no `SessionId`. Leaving it invites
  someone to wire the session-less version.
- **`PermissionDecided`** -> `PermissionDecided(sessionId, requestId, decision)`.
- **`PermissionPending(bool)`: leave alone here, delete in WP3.** It is a single global bool that cannot
  survive N sessions, but its only consumer is the host's Left/Right/Enter routing, which does not go away
  until WP3 - deleting it in WP2 would break the host for a whole package. (The original plan said "delete"
  here, which was an ordering mistake.) It stays untouched until the routing becomes a chat-scoped panel
  binding, then goes with it.
- **`SelectionRequested`/`SelectionCompleted`** gain `sessionId` (`Guid.Empty` = not session-bound).
  `SelectionBroker` keeps correlating by `RequestId`; the session id is for observers.
- **`SelectionPlugin._capabilities`** (`volatile AgentCapabilities?`, global last-writer-wins) ->
  `ConcurrentDictionary<Guid, AgentCapabilities>` + a visible-session id fed by `WorkspaceActivated`.

New in `modules/session-contracts` (activity is a property of a session; the workspace plugin does the
session -> workspace mapping):

```fsharp
/// The three activity states a session can be in. String literals so they cross load contexts and
/// round-trip through YAML without enum-identity concerns (same pattern as KeymapScope).
[<RequireQualifiedAccess>]
module SessionActivity =
    [<Literal>] let Idle = "idle"       // no turn running and nothing wanted from the user
    [<Literal>] let Working = "working" // a turn is running: thinking, a tool, a hook, compacting, retrying
    [<Literal>] let Waiting = "waiting" // blocked on a human: a permission prompt or an ask-user selection

[<Sealed>]
type SessionActivityChanged(sessionId: Guid, activity: string, detail: string, since: DateTimeOffset) =
    member _.SessionId = sessionId
    member _.Activity = activity
    member _.Detail = detail      // "thinking" / "Bash" / "permission: Write"; empty when nothing to say
    member _.Since = since        // so a consumer renders elapsed time without polling
```

**The Conversation plugin computes it** - not the bridge (which knows a turn started but not whether the
user answered a permission, and knows nothing of ask-user waits) and not a new aggregator (which would have
to duplicate turn bookkeeping to be correct). Conversation already owns this truth (`IsProcessing`,
`PendingPermission`) and already edge-detect-publishes in `PublishPermissionPendingIfChanged`; extend that
from one global bool to per-session transitions. Published on transitions only.

Two correctness gaps to close, both in conversation:

1. **`SessionStatus` has no waiting case and tool execution reads as `Thinking`.** Do *not* add cases -
   `SessionStatus` is internal conversation vocabulary and `Thinking` is honest for a running turn. Compute
   activity as a pure function instead, so `Thinking` + an unresolved `PermissionItem` = `Waiting`:
   `public static string ActivityOf(SessionState)` and `ActivityDetailOf(SessionState)` in a new
   `plugins/conversation/SessionActivityProjection.cs`.
2. **`ask_user` waits are invisible today.** With the session id on `SelectionRequested`, conversation
   subscribes to `SelectionRequested`/`SelectionCompleted` and records `PendingSelectionId` on
   `SessionState`. `Waiting` then covers both blockers - and the chat can render an inline
   "waiting for your pick" row, which it cannot today.

   > **Known limitation, discovered during WP2 recon: the session id on `SelectionRequested` will be
   > `Guid.Empty` in practice until the gateway is session-aware.** `ask_user` is an MCP tool
   > (`ClavisTools.AskUser`, the single construction site at `ClavisTools.cs:194`), and AgentGateway hosts
   > **one** MCP server with no notion of which session called it - it mints a bare `requestId` Guid and
   > nothing more. So WP2 delivers per-session `Waiting` for **permission prompts** (which do carry a
   > `SessionId`, via `AgentPermissionRequest`) but not for ask-user picks. Add the field anyway - it is the
   > choke point and costs nothing now - but do not claim the ask-user case works. Making it work means
   > giving the gateway per-session MCP instances (there is already a comment at
   > `AgentGatewayPlugin.cs:139` noting the server "needs CreateNewInstance to spin up further instances for
   > concurrent sessions"), which is its own piece of work and belongs after WP5, when more than one session
   > can actually exist.

`SessionPhase.Whisper` needs no change (it reads `SessionStatus`, untouched).

Bumps: `session-contracts 3.0.0`, `host-contracts 2.0.0`, plus the declared major in **17 dependency edges**
across 10 items (session-contracts: agent-gateway, claude-bridge, command-palette, conversation,
events-panel, marketplace-plugin, selection, task-tracker, usage-limits; host-contracts: agent-gateway,
command-palette, conversation, events-panel, selection, task-tracker, usage-limits, wpf-host).
**Use `tools/Validate-Dependencies.ps1 -Fix`** - it rewrites every declared major to the one the producer
actually ships, so the 17 edges are mechanical rather than hand-edited. Note its regex only matches
inline-flow `- { name: x, version: y }` entries, so a block-style dependency would be skipped silently.

Two manifest oddities to fix while here: `marketplace-plugin` and `usage-limits` both declare a
`session-contracts` dependency but list no matching `globalUsings` entry (`usage-limits` has no
`globalUsings` block at all), and `wpf-host` global-usings `FabioSoft.Contracts.Services` without declaring
a `services-contracts` dependency.

Tests: `ActivityOf` theory over the full status matrix (Idle/Ready -> idle; Thinking/Retrying/Compacting ->
working; Thinking + unresolved `PermissionItem` -> waiting; Thinking + `PendingSelectionId` -> waiting;
Aborting/Aborted/Ended -> idle); `ActivityDetailOf` picks the newest active tool; publish emits once per
transition and not on unchanged updates; `SelectionRequested` for session A does not mark session B waiting;
`PermissionDecided` routes to the right session in a two-chat state.

---

## WP3 - Chat becomes a panel kind (land alone - largest behavioural change) - DONE

Landed as designed, with five deviations, all recorded here because each is a decision a later package will
otherwise re-litigate. Catalog gate green: 38/38 items and 23/23 test suites compile and pass
(wpf-host 54 -> 67, conversation 154 -> 169, panel-registry 6 -> 9).

1. **The permission keys are the chat panel's, not keymap bindings.** The plan wanted Left/Right/Enter as
   "ordinary panel-scoped bindings on kind `chat`". That cannot work: a panel-local binding bypasses the
   text-input guard by design, so a bare-`Enter` binding for kind `chat` would fire *unconditionally* and
   prompt submission would be dead. The keymap has no "only while this instance is blocked" scope - the plan
   itself notes panel-instance scope is a different shape (see WP8). So `ChatPanelView` handles the three keys
   at its own root, tunnelling ahead of the prompt box's Enter. This still deletes everything WP3 wanted gone
   (`PermissionPending`, the `_permissionPending` cache, `TryHandlePermissionKeys`) and is strictly more local
   than what it replaced. It also let the input stay *enabled* during a decision, so `SetPromptInputEnabled`
   went too and the caret no longer jumps out of the prompt.
2. **Contract majors, not minors.** The plan said `layout-contracts 1.1.0` / `host-contracts 2.1.0`, but WP3
   *deletes* four message types (`OpenConversation`; `PermissionPending`, `PromptInputAvailability`,
   `PromptModeChanged`), which is breaking. Shipped as **`layout-contracts 2.0.0`** and
   **`host-contracts 3.0.0`**, with the declared major re-pointed by hand in 13 dependents (not with
   `-Fix`, per the yamldotnet caveat). Validator: 72/72 correct, only the known yamldotnet edge outstanding.
   The majors are the safety mechanism - a stale cached dependent fails loudly instead of binding old types.
3. **`LivePanels.cs`, not `PanelPlacement.cs`.** `PanelPlacement` is already a record struct inside
   `WindowManager` (a kind's last placement), so that filename would have read as two different things. The
   rule is the pure `LivePanels.Find(candidates, cardinality, workspaceId, exclude)`; the host only enumerates
   what is live. `LivePanel`/`LivePanels` are public for the same reason `LayoutTree` is - `InternalsVisibleTo`
   is forbidden.
4. **`PanelInstanceReady` carries the cardinality.** The plan put `Cardinality` only on
   `PanelKindRegistration`, which the host does not subscribe to. Rather than make the host watch
   registrations, the registry copies the declared value onto `PanelInstanceReady` (a settable member, like
   `StatusTemplate`, so no positional argument was added). The host caches it per kind so a `TogglePanel` -
   which carries only a kind - applies the same rule.
5. **`WpfHostConfig` grew two seams instead of the host learning a kind name.** `DefaultPanels`
   (`["chat"]`) opens the empty state on a launch with **no** saved layout, replacing `SeedConversation`; a
   saved layout always wins, including an empty one, so a chat you closed stays closed. `RetiredPanelKinds`
   (`conversation` -> `chat`) rewrites the renamed kind when reading a saved layout - without it every existing
   `state.yaml` would restore a tab stuck on its compile placeholder forever. Both are marketplace policy
   expressed as configuration, so the host still names no panel kind in its code.

Also worth knowing for WP4/WP6:

- **`FocusInputRequested` moved to Conversation** (the prompt lives there now), and the host's two
  `primary.Focus()` calls in `Reveal`/`Summon` became `primary.FocusSurface()` - "focus the first thing in the
  active panel", which is both correct and chat-agnostic.
- **`main-content` is gone.** The chrome regions that remain are `title-bar-left`, `title-bar-right`,
  `status-bar`, `status-bar-right`.
- **`WorkspaceId` is threaded but always `Guid.Empty`** through `OpenPanel`/`TogglePanel`/`RestorePanel`/
  `PanelInstanceReady`/`PanelSnapshot`/`WindowSnapshot` and the host's `_panelWorkspace` map. With everything
  Empty, `OnePerWorkspace` is exactly the old application-wide dedupe, so WP6 fills in a seam rather than
  building one.
- **Moved files:** `wpf-host/InputHandler.cs` and `wpf-host/WindowHost.Mode.cs` are deleted; their content
  lives in `conversation/Views/PromptInput.cs` + `PromptInput.Mode.cs`, with the recall rules extracted to the
  tested pure `PromptHistory`. `WindowChromeViews` no longer builds an input box or input row.

### Original scope

The chat becomes a normal registered panel kind `"chat"`, owned by the Conversation plugin. Deleted outright:

- `WindowManager.ConversationKind`, the `ResolveRestoreView` special case, the re-seed in `RestoreLayout`,
  the `ConversationKind` skips in both restore loops.
- `WindowHost.ConversationPanelId` (the one app-wide Guid), `_conversationPanelView`, `_conversationContent`,
  `ConversationPanelView`, `SeedConversation`, `IsSolePanelLocked`.
- The `main-content` region - the chat view is now built per instance by the panel view factory, not
  contributed to a winner-takes-all region.
- `OpenConversation` from the contracts - `TogglePanel chat` replaces it.

```csharp
bus.Send(new PanelKindRegistration(
    "chat", "Chat", 320, 200, "", isUserOpenable: true,
    context => ChatPanelView.Create(bus, ResolveChat(context)))
    { Cardinality = PanelCardinality.OnePerWorkspace });
```

The instance's `SavedState` blob carries `{ workspaceId, chatId }` - exactly what the opaque per-instance
blob is for - so a restored chat panel re-attaches to its workspace's chat instead of being re-seeded.

**The prompt input moves into the chat panel** (new `plugins/conversation/Views/ChatPanelView.cs`: chat
content + prompt input + mode accent). This deletes four host<->conversation messages
(`PromptInputAvailability`, `PromptModeChanged`, `SetPromptInputEnabled`, `PermissionPending`), the
`volatile bool _permissionPending` cache, and the hardcoded `TryHandlePermissionKeys` precedence block in
`WindowHost.OnKeyDown` - the permission Left/Right/Enter become ordinary **panel-scoped bindings on kind
`chat`**, routed as panel-local commands to the focused chat instance. The status bar stays window chrome
driven by `ActivePanelChanged`; that part is already right.

**Replacing `FindLiveInstance`'s app-wide dedupe: declared cardinality + workspace scoping.** Both, because
"singleton" conflated two questions:

```fsharp
[<RequireQualifiedAccess>]
module PanelCardinality =
    [<Literal>] let OnePerWorkspace = "one-per-workspace"     // the default: today's feel, per workspace
    [<Literal>] let OnePerApplication = "one-per-application" // the overview, the plugin manager
    [<Literal>] let Many = "many"                            // markdown:{id}-style

type PanelKindRegistration(...seven args unchanged...) =
    /// Settable so existing C# registrations keep their seven-argument constructor (same pattern as
    /// StatusTemplate). Empty means OnePerWorkspace.
    member val Cardinality = PanelCardinality.OnePerWorkspace with get, set
```

`FindLiveInstance(kind, exclude)` becomes `LivePanels.Find(kind, cardinality, workspaceId, exclude)` in
`PanelPlacement.cs`. The registry stays a pure router; the host remains the single enforcement point, but
now enforces a *declared* rule. Additive workspace-aware overloads (the 1-arg forms mean "the active
workspace", resolved by the host, so the registry never learns which workspace is active):

```fsharp
type OpenPanel(kind: string, workspaceId: Guid) =    new(kind) = OpenPanel(kind, Guid.Empty)
type TogglePanel(kind: string, workspaceId: Guid) =  new(kind) = TogglePanel(kind, Guid.Empty)
type RestorePanel(instanceId, kind, savedState, workspaceId: Guid) = ...
type PanelInstanceReady(instanceId, kind, title, minWidth, minHeight, view, workspaceId: Guid) = ...
type PanelSnapshot(..., workspaceId: Guid) = ...
type WindowSnapshot(..., workspaceId: Guid) = ...
```

Bumps as shipped: `layout-contracts 2.0.0`, `host-contracts 3.0.0` (both major - see deviation 2),
`wpf-host 4.0.0`, `conversation 8.0.0`, `agent-gateway 3.0.0` (a message it could send was removed),
`panel-registry 1.2.0`, `command-palette 1.1.3`, `keymap 1.0.3`.

Tests as shipped: `ChatPanelStateTests` (round-trips `{workspaceId, chatId}`; seven unreadable-blob cases all
yield a fresh chat rather than throwing; a half-written blob keeps the field it has). `PromptHistoryTests` -
new coverage for logic that was previously untestable inside `InputHandler` (recall up/down, the stashed
draft, the oldest-entry floor, submit leaving recall). `LivePanelsTests` (`Many` never reuses;
`OnePerApplication` reuses across workspaces; `OnePerWorkspace` does not; the excluded instance never matches
itself; an unset cardinality is `OnePerWorkspace` and scopes to the workspace). `LayoutTreeTests` gained three
retired-kind rename cases. `PanelCatalogTests` gained cardinality/workspace pass-through, including through a
buffered replay. No `KeymapBindingsTests` addition - per deviation 1 there are no chat permission bindings to
resolve.

---

## WP4 - `ConversationState` -> chats aggregate - DONE

Landed as designed. Catalog gate green: 38/38 items, 23/23 test suites (conversation 169 -> 180).
`conversation 9.0.0`; no contract module moved, so nothing else needed a bump - the whole package is internal
to one plugin, which is what made it a good one to land straight after WP3.

Deviations and decisions worth carrying forward:

1. **`ActiveSession` / `ActiveSessionId` / `WithActiveSession` were kept**, as derived accessors over
   `VisibleChat.LiveSession`. The plan's shape dropped them, but they name exactly the same fact ("the live
   session of the chat the user sees") and are read in ~20 places across the update, the view model and the
   plugin. Renaming would have churned the 52 kB test file for no behaviour change. `Sessions` on the
   aggregate is gone for real, replaced by `AllSessions` (every session of every chat) - that one *was* two
   different things wearing one name.
2. **The tick now refreshes every chat with a running turn**, not just the visible one. The plan said
   `HasLiveTiming` becomes "any chat has a Running turn"; had `HandleTick` stayed visible-chat-only, a
   background chat's elapsed time would freeze while the timer kept spinning for it and then jump on switch.
   The per-turn arithmetic is extracted to `TickTurns`, and a chat with nothing running comes back
   reference-identical so the projection skips it.
3. **`HandleFullRestart` has two entry points.** The user-driven one restarts the visible chat; the one
   inlined from `AgentSessionEnded` restarts *the chat that owns that session*, which is not necessarily the
   visible one - a background chat's session can end too. Both funnel into one private `Restart(state, chat)`.
4. **`ChatViewModels` owns the projection**, including two facts that are not per-chat yet: prompt
   availability and the session's permission mode. They are application-wide today (one bridge, one capability
   catalog) but rendered per chat panel, so the holder remembers them and applies them to every view model -
   including one created later, which would otherwise miss them. When WP5 makes them per-session, this is the
   one place that changes.
5. **`ChatPanelBinding` resolves the panel's blob against live state** in the plugin, not in the view: the
   panel is handed a view model plus the identity to persist. A saved `chatId` that still exists wins; anything
   else lands on the visible chat and the resolved id is written back, so a hand-opened panel gains a concrete
   chat id and returns to the same chat next launch.
6. **`ConversationState.Empty` exists but is unused.** `Init` still seeds one chat, because nothing creates
   chats yet - that is WP5's job, and `Empty` is the honest starting point it will switch to.

Still true after WP4: there is exactly **one** chat at runtime, so this package is a structural change with no
visible behaviour change. The first-launch checks listed under WP3 are still the ones to run.

### Original scope

Keep **one** aggregate, one pure update, one lock - N independent states would mean N locks, N tick timers,
and no cheap cross-workspace answers. The real defect is that `Sessions` does two jobs: a history list with
a live tail (`HandleFullRestart` ends+disposes and appends) **and** the would-be multi-workspace axis.
Separate them by inserting the missing level:

```csharp
public sealed record Chat
{
    public Guid ChatId { get; init; }
    public Guid WorkspaceId { get; init; }
    public string WorkingDirectory { get; init; } = "";
    public IReadOnlyList<SessionState> Sessions { get; init; } = [];  // history, one live tail
    public Guid LiveSessionId { get; init; }
    public SessionState? LiveSession => Sessions.FirstOrDefault(s => s.Id == LiveSessionId);
    public Chat WithLiveSession(Func<SessionState, SessionState> updater);
}

public sealed record ConversationState
{
    public IReadOnlyList<Chat> Chats { get; init; } = [];
    public Guid? VisibleChatId { get; init; }
    public Chat? VisibleChat => ...;
    public ConversationState WithChat(Guid chatId, Func<Chat, Chat> updater);
    public ConversationState WithSessionById(Guid sessionId, Func<SessionState, SessionState> updater);
    public static ConversationState Empty => new();   // no implicit session; Workspaces starts them
}
```

`HandleFullRestart` moves down onto `Chat` where it belongs. **`SessionState` is untouched** - it was
already fully per-session, which is why this feature is tractable at all.

ViewModel projection: one `ConversationViewModel` per chat, created by the panel view factory, held in
`Dictionary<Guid /*chatId*/, ConversationViewModel>`. After each pure update, diff old/new `Chat` records by
`ReferenceEquals` and project only the chats that changed. A background chat's panel is alive but off-screen
so its projection is still needed (its scroll must be right on switch) - the `ReferenceEquals` filter keeps
it cheap. `HasLiveTiming` becomes "any chat has a Running turn"; the tick rebuilds only those.

Tests: stream events route by `SessionId` into the right chat and leave others reference-equal;
`HandleFullRestart` ends+disposes only the target chat's live session; switching `VisibleChat` mutates no
chat; the tick only touches chats with a `Running` turn; `AgentValues.Build(state.VisibleChat)` unchanged
for a single-chat state (regression guard).

---

## WP5 - `workspace-contracts` 2.0.0 + the `Workspaces` plugin (headless) - NEXT

New `modules/workspace-contracts` (2.0.0) and `plugins/workspaces` (`essential: true`; deps
`configuration`, `workspace-contracts`, `session-contracts`, `layout-contracts`).

```fsharp
type WorkspaceInfo(workspaceId: Guid, name: string, accentKey: string, workingDirectory: string,
                   sessionId: Guid, activity: string, activityDetail: string, ordinal: int)
type WorkspaceListChanged(workspaces: IReadOnlyList<WorkspaceInfo>, activeWorkspaceId: Guid)
type RequestWorkspaces()                                        // late-subscriber pattern
[<Description("Activate a workspace by id")>]              type ActivateWorkspace(workspaceId: Guid)
/// Activate the workspace in a slot - or CREATE it there if the slot is free. Pressing an unused F-key is
/// the primary way a workspace comes into being, so this is activate-or-create, never a no-op.
[<Description("Activate or create the workspace in a slot (1-11)")>] type ActivateWorkspaceSlot(slot: int)
type WorkspaceActivated(workspaceId: Guid, sessionId: Guid)
type WorkspaceSessionStarted(workspaceId: Guid, sessionId: Guid, workingDirectory: string)
[<Description("Create a new workspace")>]                  type CreateWorkspace(name: string, workingDirectory: string)
[<Description("Close the active workspace")>]              type CloseActiveWorkspace()
type CloseWorkspace(workspaceId: Guid)
type RenameWorkspace(workspaceId: Guid, name: string)
type WorkspaceClosed(workspaceId: Guid)
```

**The slot is a stable address, not an ordinal.** `WorkspaceInfo.Ordinal` is renamed `Slot` and behaves
accordingly: closing a workspace **frees its slot and renumbers nothing** - slot 3 stays slot 3 forever, so
the key you learned never moves under you, and the bar shows a gap. A new workspace takes the **lowest free
slot**. (This replaces the earlier "close reflows ordinals" intent, which would have silently remapped every
shortcut on a close.)

**`exit` is repurposed and a real quit is added.** `exit` in `AliasCatalog` currently shuts the application
down; it now means **close this workspace** (dispose its session, drop its tab, carry on). That leaves no way
out of the app, so add `ExitApplication` (palette alias `quit`) publishing `ApplicationShutdown`, plus a quit
glyph in the bar's right tail beside `+`. Quit is the only destructive gesture in the bar: it is the only
element that turns red on hover and the only one that goes through `ConfirmDialog` first. Closing the **last**
workspace leaves the bar empty with just `+` and quit - the honest consequence of separating the two
gestures, and the bar is still a way back in.

Files: `WorkspacesPlugin.cs` (impure shell), `WorkspaceSet.cs` + `WorkspaceUpdate.cs` (pure),
`WorkspaceFile.cs`, `AccentPalette.cs`, `WorkspacesConfig.cs`.

**`Workspaces` takes over session creation.** Today `ConversationPlugin.ActivateAsync` sends
`StartNewSession` with `config.WorkingDirectory`; the working directory is now per workspace, so
`Workspaces` owns `StartNewSession` and `ConversationConfig.WorkingDirectory`/`Model` are deleted.
Conversation reacts to `WorkspaceSessionStarted`. **Lazy start, stay alive**: the active workspace at boot,
others on first activation - restoring 8 workspaces must not spawn 8 `claude.exe` at launch.

Accent is a **theme resource key**, never a baked brush: add `Accent1Brush`.. to `Theme/Styles.xaml` and
persist the *key*, so a random assignment is stable across restarts and re-themable.

**The accent palette must come from the Clavis design language, not the Layer2 corporate colours**, and it
must not collide with the activity dot. The design language splits the accents deliberately:
**primary blue = STATE** ("is this active/good right now?") and **secondary periwinkle = IDENTITY** ("what is
this thing called?"). A workspace's accent is pure identity, so it draws from the identity family -
`--human #ADA6F2`, `--violet #C79BF0`, `--clavis #9FD5F0` and further hues in that range - never from the
signal colours (`green`/`yellow`/`red`), which are reserved for meaning. Correspondingly the **accent and the
activity dot are separate marks**: the dot carries activity (steady grey idle / pulsing blue working / steady
yellow waiting), the accent carries identity (the 2 px constant-length left tick settled in WP-1). Tinting the
dot with the workspace accent would destroy the activity signal, so it is prohibited.

Switching in this package is via palette commands only - provable end-to-end before any new UI exists.

Tests: `WorkspaceUpdateTests` - create takes the lowest free slot; **close frees the slot and renumbers
nothing** (closing 2 leaves 1, 3, 4 untouched and the next create lands in 2); closing the active workspace
re-activates a neighbour; closing the last one leaves an empty set, not a refusal;
`ActivateWorkspaceSlot` on a free slot **creates**, on an occupied slot activates, above the cap is ignored;
`SessionActivityChanged` for an unknown session is ignored; the session starts lazily exactly once per
workspace. `WorkspaceFileTests` - config round-trip preserving slot gaps; missing section -> one default
workspace from the current directory.

---

## WP5b - Agent instances: discover, adopt, hand off

A workspace's agent should outlive Clavis. Today the bridge spawns a child process per session and closing
Clavis kills the work; the goal is that an agent keeps running while Clavis is shut, and Clavis picks it
back up on the next launch.

### What the CLI actually offers (verified against 2.1.220, not assumed)

- `claude --bg` starts a durable background agent; `claude agents --json` lists live sessions (documented
  flag, scriptable, no TTY) as `{pid, id, cwd, kind, startedAt, sessionId, name, status, state}`.
- Each live session writes `~/.claude/sessions/<pid>.json` with the same fields plus `peerProtocol: 1` and
  a `bridgeSessionId`.
- `--session-id <uuid>`, `--resume <sessionId>`, `--fork-session`, `--input-format/--output-format
  stream-json`, `-n <name>` are all documented.

**There is no supported local attach.** The only Claude named pipe on the machine is
`claude-mcp-browser-bridge` - nothing per agent - and the live channel for a background agent runs through
a cloud bridge (`bridgeSessionId`). `peerProtocol` is undocumented internals of an auto-updating CLI.
`--resume` does **not** attach: it starts a *new* process over the persisted transcript, and two processes
on one session id is not safe. So resume is *take over*, never *join*.

Consequence, decided deliberately: **Clavis owns the stream while it is open, and hands the session back to
a background agent when it closes.** Reverse-engineering the peer/bridge channel was rejected - it would
break without warning on a CLI that updates itself.

### The facade (provider-neutral - no `claude`, no `--bg`, no pid in any contract)

```fsharp
type AgentInstance(instanceId: string, name: string, workingDirectory: string,
                   status: string, startedAt: DateTimeOffset, isAdopted: bool)
type AgentInstancesRequested()
type AgentInstancesAvailable(instances: IReadOnlyList<AgentInstance>)
[<Description("Take over an existing agent instance")>]
type AdoptAgentInstance(instanceId: string, sessionId: Guid)
/// mode: "keep-running" (hand back to the background) or "stop".
type ReleaseAgentInstance(sessionId: Guid, mode: string)
type AgentInstanceAdopted(sessionId: Guid, instanceId: string)
type AgentInstanceReleased(instanceId: string, keptRunning: bool)
```

`StartNewSession` stays, but becomes *one* way to obtain an instance rather than the only one. The facade
stops assuming Clavis spawns and owns a process; lifecycle is the provider's business.

### What ClaudeBridge does underneath

- **Adopt**: `claude --resume <sessionId> --input-format stream-json --output-format stream-json`, so the
  existing transcript continues in a Clavis-owned process.
- **Create**: as today but with `--session-id <uuid>` and `-n <workspace name>`, so the session is durable,
  identifiable, and visible in `claude agents`.
- **Release (keep-running)**: end the owned stream, then `claude --bg --resume <sessionId>` so it carries on.
- **Discover**: `claude agents --json`, mapping `sessionId`/`name`/`cwd`/`status` onto `AgentInstance`.

### Edge cases that need deciding when this is built

- **Mid-turn hand-off.** Releasing while a turn is running is the ugly case: either wait for `AgentResult`
  or accept that the re-dispatched agent resumes from the transcript. Prefer waiting, with a timeout.
- **Crash.** No clean shutdown means no hand-off; the session is still *resumable*, just not still running.
  Acceptable, but the bar's "not started" state must not claim otherwise.
- **Adopting something Clavis did not start**, including one whose `cwd` differs from the workspace's
  working directory - offer it, but do not silently rebind the workspace's directory.
- **Two Clavis homes** could both try to adopt one instance. Adoption must be exclusive; last-writer-wins
  would give two windows onto one transcript.
- **Orphans**: agents Clavis dispatched and never reclaimed. The overview panel (WP9) is where they surface.

Tests: pure mapping from `claude agents --json` to `AgentInstance` (including a malformed row, an absent
`name`, and a `cwd` outside every workspace); the release-mode decision table; adoption refused when the
instance is already adopted.

---

## WP6 - Per-workspace surfaces + layout v2

**N surfaces, lazily created, hidden** - not one surface captured and restored. One-surface swapping tears
down and rebuilds panel views on every switch: scroll positions lost, `PanelClosed` fired so the registry
disposes instances (git-log timers restart, the chat view is recreated), on a gesture pressed dozens of
times an hour. N surfaces keep background panels alive - which is a *feature*: it is the only way
"workspace 3 is working" can mean anything.

```csharp
// plugins/wpf-host/WorkspaceSurfaces.cs - one per WindowHost
internal sealed class WorkspaceSurfaces
{
    public DockingSurface Active { get; }
    public DockingSurface For(Guid workspaceId);   // creates + adds hidden on first ask
    public IEnumerable<(Guid WorkspaceId, DockingSurface Surface)> All { get; }
    public bool Activate(Guid workspaceId);        // Motion.crossfade between the two
    public event EventHandler<Guid>? Activated;
}
```

`WindowHost.Surface` becomes `=> _surfaces.Active`, so the ~40 existing call sites compile unchanged; only
the genuinely workspace-aware ones (capture, snapshot, `LivePanels.Find`) iterate `All`. The switch
animates via `Motion.crossfade` - a hard cut is a bug per the design language. A workspace's surface and its
panels (`RestorePanel`) materialise **on first activation**, reusing the existing pending-restore machinery
keyed by workspace.

**Secondary windows belong to a workspace.** A panel torn off while workspace 2 is active belongs to
workspace 2, so `WindowHost` carries a `WorkspaceId` (`Guid.Empty` for the primary and the bar) and
secondaries hide/show with the switch. `OrderedWindows`, `HideAll`, `Summon` and the drop targets filter
accordingly.

Persistence (`state.yaml`, `WpfHost` section, `version: 2`) - normalise geometry away from per-workspace
layout so the primary's bounds are not duplicated N times:

```yaml
version: 2
activeWorkspaceId: <guid>
windows:
  - { windowId: <guid>, role: primary, workspaceId: 00000000-..., bounds: {...} }
  - { windowId: <guid>, role: panel,   workspaceId: <guid>,       bounds: {...} }
layouts:
  - { windowId: <guid>, workspaceId: <guid>, layout: {...}, slideIns: [...] }
```

**Do not touch reveal gating.** There are now two independent async state answers (the host's layout and the
workspace list); adding a third precondition to `RevealWhenReady` adds a third way to hang, and that gate is
the most fragile part of boot. So the host's layout is **self-sufficient**: it persists `activeWorkspaceId`
itself and reveals on exactly today's two preconditions plus the 2 s failsafe plus `BootstrapComplete`. If
the workspace list later disagrees, the host discards orphan layouts on the first `WorkspaceListChanged`.

Tests: `PanelCatalogTests` - `WorkspaceId` survives resolve and buffered replay; `LayoutFileTests` - v2
round-trips, orphan layouts dropped; `LayoutMigrationTests` - v1 -> v2 end-to-end.

---

## WP7 - The bar

**Owner: `wpf-host`, as a new window role.** It already owns every HWND, the summon/hide flow,
`Application.Current.MainWindow`, `WorkAreaMaximize`, and the physical->DIP conversion; a second plugin
minting a top-level `Window` would fork window ownership. So the host owns `BarWindow.cs` and defines one
region **`workspace-bar`**; the `Workspaces` plugin contributes the strip via `UiRegionContribution`. The
host stays free of workspace vocabulary.

This forces an existing-defect fix: `WindowManager` routes **all** region contributions to `GetPrimary()`
only. Region routing becomes **by window role** (`primary` / `panel` / `bar`), which also stops secondary
windows' `title-bar-left/right` regions being defined-but-never-fed.

Chromeless config (precedent: `TearOffPreview.cs:51-66`, with hit-testing inverted):
`WindowStyle=None, AllowsTransparency=true, ResizeMode=NoResize, ShowInTaskbar=false, ShowActivated=false,
Topmost=true, WindowStartupLocation=Manual, IsHitTestVisible=true, Owner=null`, no close glyph.

**Focus stealing: `ShowActivated=false` is not enough** - it only covers the first show; a *click* on the bar
activates it and yanks the caret out of the prompt. Add `NoActivateWindow.cs` (one
`[ExcludeFromCodeCoverage]` interop file, new to this codebase): on `SourceInitialized` OR `WS_EX_NOACTIVATE`
into `GWL_EXSTYLE` and hook `WM_MOUSEACTIVATE -> MA_NOACTIVATE`. Not optional - without it the feature is
unusable.

**Maximized windows: the honest answer.** A `Topmost` bar draws above a normal maximized window, so the bar
stays visible - but `WorkAreaMaximize.Constrain` does not know about it, so a maximized *Clavis* window's
top strip sits underneath the bar. Fix the case the user will actually see: subtract the bar height from the
work area inside `WorkAreaMaximize`.

**`SHAppBarMessage` is cut for v1** (bare Topmost instead). AppBar registration is the "correct" answer, but
a leaked registration survives a crash and permanently steals desktop space until logoff - and this app
*deliberately crashes* on non-viable startup (`StartupViability`). Add `AppBar.cs` as a designed seam behind
`WorkspaceBarConfig.ReserveScreenSpace = false`.

DPI + multi-monitor: place on the monitor containing the primary window using the existing per-monitor
work-area machinery (`MaximizedWindowBounds` / `WindowSnapBehavior.RectOf` / `ScreenRectangle`), converting
physical px -> DIPs via `PresentationSource.FromVisual(primary).CompositionTarget.TransformFromDevice`.
Reposition on `SystemParameters.StaticPropertyChanged`.

**Summon/hide sharp edges:**
- The bar is **not** in `_windows`: never in `OrderedWindows` (Tab ring), `OtherWindowRects` (snapping),
  tear-off drop targets, `CaptureLayout`, or `BuildSnapshot`.
- `HideAll` **skips** it - that is the point: banished Clavis still shows which workspace is working.
- Never `Application.Current.MainWindow`; never `Owner`-linked to the primary (owned windows hide with the owner).
- Its `Closing` must **not** send `ApplicationShutdown`; closed only by `WindowManager.Dispose`.
- Clicking an entry while Clavis is hidden summons: `Workspaces` publishes `SummonClavis` alongside `WorkspaceActivated`.
- `Summon()`'s `Topmost=true; Topmost=false;` z-order kick can momentarily lift the primary above the bar -
  re-assert the bar's `Topmost` at the end of `Summon`.

**The activity dot**: grow `modules/clavis-controls` with `ActivityDot` built on `StatusDot.sized` - an
`Ellipse`, never a `Border` - three states driven by `Motion.breathe`/`stopBreathing`. Obeying "pulsing is
reserved for activity" (`WindowHost.Focus.cs:258-261`): **idle = steady dim** (`TextDimBrush`),
**working = breathing** (`GreenBrush`, 600 ms `Motion.BreathingDuration`), **waiting = steady accent**
(`ClavisBrush`) - the most urgent state must not pulse, so it draws the eye by colour. Do not hand-roll a
ring in the plugin (that is `task-tracker`'s 1.9 s ring - the anti-precedent).

**Tab geometry and motion** (settled by the WP-1 mockup): every tab is a fixed **180 x 48**, three logical
columns with no dividers - the **slot number** (Rajdhani 21/600 in `ClavisBrush`, 55% opacity until active),
the **activity dot**, and the **title** (Rajdhani 12.5/500, *sentence case* - the `title` role, not the
uppercase `label` role - clamped to two rows with an ellipsis and the full title as a tooltip). Tabs
**animate in and out** at the app's one duration (250 ms, CubicEaseOut) by transitioning width, padding and
opacity, so creating a workspace with an unused F-key slides its tab in at its slot position and closing one
slides it out leaving the gap. Every other bar state change animates too - active-tab fill, the number's
opacity lift, the accent tick's brightening.

**The identity accent is a 2px left tick of constant length.** It is kept (a workspace should feel like a
place, not just a number) but fenced in tightly: it is only ever the tick, it never touches the number or the
dot, and its **geometry does not change with selection** - identity is not a function of whether you are
looking at the workspace, so only its opacity lifts from 55% to full. That keeps the three colour languages
on the strip cleanly separated: **blue number = position**, **dot = state**, **tick = identity**, with only
the dot carrying motion.

Tests: `ActivityDotTests` - the state -> (colour key, breathing) mapping as a pure function
(idle -> dim grey steady, working -> `ClavisBrush` breathing, waiting -> `YellowBrush` steady);
`WorkspaceBarRowTests` - row model from a `WorkspaceListChanged`: ordered by slot, gaps preserved, title
truncation leaves the full string as the tooltip;
`BarPlacementTests` - the pure monitor-rect -> bar-rect computation (DPI factor, multi-monitor, unplugged fallback).

---

## WP8 - F1-F11 / F12

**Scope: `Application`, definitively not `System`.** System scope means `RegisterHotKey` on the primary
HWND, stealing F1-F12 from *every application on the machine* (F1 is help everywhere, F12 is devtools).
`GlobalHotkey.TryVirtualKey` already maps F1-F24, so this would silently "work" and be a disaster. Keyboard
switching therefore needs a focused Clavis window; the bar's click covers the hidden case.

**Fix the text-input swallow properly.** `WindowHost.cs:440` bails whenever a text input is focused and the
modifiers are not Ctrl/Win, so bare F1 dies. Widening `isTextSafe` to "Ctrl/Win **or** an F-key" is a hack
that leaves the next non-text key broken. Invert the predicate - ask about the **key**, not just the
modifiers - in `clavis-rendering`'s `KeyGestureReader` (additive):

```fsharp
/// True when a gesture would be consumed by a focused text input as editing or caret input, so a keymap
/// binding must yield to it. Text-producing keys (letters, digits, punctuation, Space, Enter, Back, Delete,
/// Tab) and caret movement (arrows, Home, End, PageUp/Down) with at most Shift qualify. Function keys,
/// Escape, and anything with Ctrl/Alt/Win do not - so a shortcut on those fires while typing.
val isTextEditingGesture : ModifierKeys -> Key -> bool
```
```csharp
if (IsTextInputFocused() && KeyGestureReader.isTextEditingGesture(Keyboard.Modifiers, key) && !isPanelLocal)
    return;
```
`isTextSafe` is deleted (one call site) and the Tab special case disappears - Tab is now correctly classified once.

**`KeyBinding` gains no window dimension.** The bar carries `WS_EX_NOACTIVATE` and never receives keyboard
input, so there is nothing to disambiguate; and the real gap is panel *instance* vs *kind* scope, a
different shape.

**Fix the "no per-plugin shortcut declaration" smell** rather than hardcoding twelve more bindings into
another plugin's `KeymapBindings.Defaults`:

```fsharp
/// A plugin declares the default gestures for its own commands, so a shortcut ships with the feature
/// instead of being hardcoded in the keymap plugin. The keymap folds these into its default set; a user
/// rebinding still wins, and a gesture already claimed by an earlier declaration is reported as a conflict
/// with the first declaration winning.
[<Sealed>]
type DefaultBindingsDeclared(pluginId: string, bindings: IReadOnlyList<KeyBinding>) = ...
[<Sealed>]
type RequestDefaultBindings() = do ()   // mirrors PanelKindsRequested
```

`KeymapBindings.Merge(persisted, declared)` stays pure with one extra input; precedence
**user rebinding > plugin declaration > built-in default**. `Workspaces` declares F1-F11 ->
`ActivateWorkspaceSlot <n>` (parameterised commands already route through the palette's `MessageActivator`,
exactly like `TogglePanel events`) and F12 -> `TogglePanel workspace-overview`.

Tests: `isTextEditingGesture` theory - `(None, F1)` false, `(Shift, F1)` false, `(None, A)` true,
`(Shift, Left)` true, `(Ctrl, Left)` false, `(None, Tab)` true, `(None, Escape)` false;
`KeymapBindings.Merge` precedence, declared-gesture conflict first-wins, a declaration for an unknown
command is kept.

---

## WP9 - The overview panel (F12)

**Architecturally: a panel kind. Nothing new.** `workspace-overview`, registered by `Workspaces`,
`Cardinality = OnePerApplication`, default placement an edge slide-in via the existing
`WpfHostConfig.DefaultSlidePanels` (top edge). It inherits open/toggle/close/restore/persist/tear-off/Esc/
palette-command for free, and F12 is literally `TogglePanel workspace-overview`. A bespoke chromeless
overlay is rejected - it would be the third overlay mechanism after the shortcut-help overlay and slide-ins.

Rows show name, accent, working directory, model/mode/effort, activity + detail, elapsed-since, queued
count, context fill.

Tests: `WorkspaceOverviewRowsTests` - pure row projection from `WorkspaceListChanged` +
`SessionActivityChanged` (elapsed-since formatting, sort by ordinal, active row marked).

---

## WP10 - Docs + agent surface

Marketplace `docs/CODEMAP.md`; regenerate `docs/MESSAGE-MAP.md`
(`tools/Generate-MessageMap.ps1 -CoreSrc <clavis>/src`); `MARKETPLACE.md`; `plugins/wpf-host/PLUGIN.md`
(also fix its existing stale `config/WpfHost.yaml` vs `state.yaml` contradiction and the `usage-pace` ->
`usage-limits` slip); new `plugins/workspaces/PLUGIN.md`. Host repo `CLAUDE.md` +
`docs/{CODEMAP.md, DEPENDENCY-MAP.md, CORE-AND-BUS.md}` - the Terminology section needs the new
Workspace definition and the layout rename. `agent-gateway/{ClavisTools.cs, ClavisDocs.cs}`:
`layout_snapshot` plus new `workspaces_list` / `activate_workspace` tools.

---

## Cut from the first release

- **`SHAppBarMessage` screen-space reservation** - seam only. A leaked registration steals desktop space
  until logoff, and this app deliberately crashes on non-viable startup.
- **Migrating the existing hardcoded `Ctrl+<x>` panel toggles onto `DefaultBindingsDeclared`** - ship the
  mechanism, migrate only the workspace bindings.
- **Folding `task-tracker`'s 1.9 s ring onto `ActivityDot`** - correct, but an unrelated plugin.
- **Cross-workspace panel drag** - separate visual trees; the OLE fall-through path would need
  workspace-aware hit-testing for no demand yet. Explicitly unsupported.
- **Bar interactions beyond click** - no drag-to-reorder, no inline rename, no context menu. Those are
  commands plus the overview panel.
- **"Bar follows the focused monitor"** - one configured monitor; multi-monitor DPI churn is a bug farm.
- **More than 11 keyboard-switchable workspaces** - F1-F11 is the cap by construction; workspace 12+ is
  click/overview only. No Shift+F1 second bank.
- **Session suspend/release and a `MaxLiveSessions` cap** - lazy start already avoids the boot storm.
- **Per-workspace theme** - one of four palette keys, randomly assigned, rerollable by command. No picker.
- **Workspace templates/cloning.**

---

## Verification

Per package: `tools/run-tests.ps1` in the marketplace (runs `Validate-Dependencies.ps1` first, catching
stale dependency majors after the bumps).

Gate for WP0 and every contract bump:
```
dotnet run --project src/tools/FabioSoft.Clavis.CompileTest     # in the host repo
```
It compiles every marketplace item and its tests from a cold staging dir - exactly what catches a
half-rename or a stale dependency major. Run `FabioSoft.Clavis.WatcherTest` once after WP0 (it boots the
real `marketplace-plugin` against the catalog).

> **Stale-cache gotcha - FIXED in core, no longer a per-package chore.** Historically
> `PluginCompiler.isUpToDate` scanned source files only, so an item whose *only* change was its manifest -
> retargeting a dependency name, bumping a declared major - kept a cached assembly still binding the **old**
> reference and failed to load at runtime with nothing marking it stale. WP0 hit this on all 12 dependents;
> six (including `Selection`) would have silently failed to load, and the caches had to be purged by hand.
>
> The cache is now keyed on a **`BuildSpec` fingerprint** (SHA-256 over global usings, ordered sources,
> packages, resources and output shape) in a `.buildspec` sidecar beside the cached assembly, alongside the
> timestamp check. A manifest dependency change reaches the compiler as a `GlobalUsings` change - `BuildSpec`
> has no dependency field, references come from the reference-root directories - so it now invalidates the
> cache correctly. **`Version` is excluded on purpose:** the lifecycle pipeline writes version bumps into
> `PLUGIN.md` *after* compiling, and an earlier attempt that keyed on the manifest's *timestamp* broke the
> watcher pipeline for exactly that reason (`WatcherTest` failed three checks; verified by reverting the one
> file). With the fingerprint, `WatcherTest` passes.
>
> Practical note: the first launch after this lands recompiles every item once, because existing caches have
> no sidecar and a missing sidecar counts as stale.
>
> **CompileTest gotcha - applies to every package that adds or renames a module (WP0, WP2, WP5).**
> `Program.fs` sets `referenceRoots = [ shellBin; librariesDir; modulesDir ]`: the tool *reads*
> `~/.clavis/modules` and never installs the modules it produces. So a new or renamed module compiles fine
> itself and then **every dependent fails** with `The type or namespace name 'X' does not exist in the
> namespace 'FabioSoft.Contracts'` until its DLL is present in `~/.clavis/modules`. Two ways to install it:
> launch Clavis once (compile-on-launch rebuilds and installs modules in dependency order), or - when
> launching the GUI is unwanted - build the module from a throwaway `.fsproj` over its `sources:` list
> (`net10.0-windows`, `AssemblyName`/`RootNamespace` from `PLUGIN.md`, `ManagePackageVersionsCentrally=false`)
> and copy the DLL in. The DLL only needs a matching major, since the kernel binds by bare major.
> Do not read this failure as a broken rename - check the modules dir first.

Manual end-to-end, after WP7/WP8:
1. Cold launch over a pre-existing `state.yaml`; wait for `WpfHost: ... primary window shown` in the newest
   `~/.clavis/logs/clavis-*.log`. The old layout must survive as workspace 1.
2. Create two more workspaces with different working directories; F1/F2/F3 switch.
3. Type in the prompt, then press F2 - proves the text-input swallow fix.
4. Tear a panel off in workspace 2, restart - proves per-workspace secondaries.
5. Trigger a permission in workspace 2 while viewing workspace 1 - the bar must show `waiting`.
6. `Ctrl+Shift+V` to hide everything: the bar stays, and clicking an entry summons.
7. F12 opens the overview and lists all sessions.

## Named risks

- **WP3 (prompt input move)** is the largest behavioural change and touches focus, permission keys, and the
  mode accent. Land it alone, before any workspace multiplies chats.
- **N live sessions = N `claude.exe`**, and usage limits are account-global by design, so heavy parallel use
  burns one budget. Lazy start mitigates; a cap is deferred.
- **`WS_EX_NOACTIVATE` interop is new** to this codebase and untestable by unit test - isolate it in one
  `[ExcludeFromCodeCoverage]` file with a reason and verify by hand.
- **Stale module DLL after WP0** - `~/.clavis/modules/FabioSoft.Contracts.Workspace.dll` v1 must be pruned
  manually; nothing in the kernel prunes retired module outputs.
- **ALC identity**: no new `DependencyProperty`, attached property, or `RegisterClassHandler`. The bar strip
  is plain CLR + `SetResourceReference` + `INotifyPropertyChanged`, reaching the host as a
  `UiRegionContribution` whose `ViewFactory` is a BCL `Func<obj>`. `RegionManager.AddContribution` already
  replaces a same-`PluginId` entry, so reloading `Workspaces` swaps the strip cleanly.
