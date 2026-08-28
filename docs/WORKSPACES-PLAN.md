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
| WP5 workspace-contracts 2.0.0 + Workspaces plugin | done, **not runtime-verified** | `9965ed0` + the Selection half |
| WP6a layout v2 + migration | done, **not runtime-verified** | `5c020d6` |
| WP6b window-level workspace ownership | done, **not runtime-verified** | `5a514e3` |
| WP6c N surfaces per window | done, **not runtime-verified** | `f807db8` |
| WP8 keyboard: F1-F11/F12 + declared bindings | done, **not runtime-verified** | `5cc47b5` |
| WP9 the F12 overview panel | done, **not runtime-verified** | `dbbcfe7` |
| WP10 docs + agent surface | marketplace side done; host repo docs remain | `39f3334` |
| WP7 the bar | done, **not runtime-verified** | `f0c1dd6` |
| WP5b agent instances (bridge) | done, **hand-over runtime-verified** | `342e95d`, `d4d7604`, `045e671`, `820e67c`, `27a37a8`, `c481c33` |
| WP5c continuous sessions: resume, fleet tabs, take-over, park on close | done, **not runtime-verified** | this pass |
| Chat panel unclosable (`IsClosable` on registration + instance) | done | `7d676f1` |
| WP11 one window per workspace | done, **not runtime-verified** | `8d6a156` + `af0c4f7` (host repo) |
| WP12a scope infrastructure in Clavis core | done, tested | host branch `worktree-feature+workspace-scoping` |
| WP12b restore/panel defects behind the reported symptoms | done, **launch-verified 28.08** | `71e76b5`, `03c6fff`, `e7da8cb`, `e2e38e7` |
| WP12c `conversation`/`selection` per workspace + wpf-host split | **not started** - gated on the host branch reaching `main` | - |

## WP12 - Workspace scoping

The workspace feature was reported as behaving inconsistently: sometimes two chat panels in one window,
sometimes a window with no panel at all. The diagnosis was that **scope is a manually threaded field with a
magic value** - `workspaceId` is carried on message after message, and `Guid.Empty` means something
different to each reader (`IsInActiveWorkspace`: "the active one"; `PlaceContribution`: "every window";
`BindPanelToChat`: "the visible chat"; `LayoutMigration.Adopt`: "not yet assigned"). Any reader that
forgets the filter, or resolves the magic value differently, produces exactly those symptoms.

**WP12a - the scope model (host repo, done).** A plugin can now be `scoped`, running once per scope with its
own object and its own `ScopedBus`; the routing rule is the pure `MessageScope.reaches`. One real `Bus`
remains underneath, so dead letters, the activity stream and the bootstrap buffer stay single. The scope
owner announces `ScopeOpened`/`ScopeClosed` and names no plugin. Also fixed there: `IBus.Request` matched
only on the response *type*, so two concurrent requests of the same type resolved each other's answers.

**WP12b - the actual defects (this repo, done and launch-verified).** Four, all in the restore path and all
independent of the scope model:

1. A window faded to zero kept a held animation value, so it came back as an opaque black rectangle that
   still dragged by its caption - it read as a hang (`71e76b5`).
2. "Already restored" was guarded per **workspace**, but a workspace owns one tree per window and the two
   kinds of window come back at different moments. One panel window's restore marked the whole workspace
   done, and its chrome window's tree was never put on screen (`03c6fff`).
3. The bootstrap window stayed anonymous until the first `WorkspaceActivated` adopted it, although the boot
   had already restored a *specific* workspace's tree into it. That workspace comes from `state.yaml`, the
   first to activate from `configuration.yaml` - two files that may disagree (`e7da8cb`).
4. `PlacePanel` recorded a panel's workspace, kind and cardinality only on the fresh-open path, so after a
   restart every restored panel was of no known workspace: asking for a restored kind opened a second one
   beside it, and unclosable kinds stopped being unclosable (`e2e38e7`). **Most likely the main cause of the
   duplicate chat.**

Launch on 28.08 confirmed a clean boot and exactly one chat per workspace across four workspaces, each in
its own window, with a clean shutdown.

**WP12c - the remaining structural half (not started).** Making the bug class impossible rather than fixed:
`conversation` and `selection` become scoped plugins, and wpf-host splits into a global part (bar, window
registry, reveal, shutdown, persistence) and a `scoped` part owning exactly one workspace's surface, handed
to it as `WorkspaceSurfaceReady`. Two findings shape it:

- **The two are entangled.** `conversation` subscribes to 22 message types, and the per-workspace half of
  them is published by the chat panel *inside wpf-host*. They can only be addressed once wpf-host is split,
  so scoping `conversation` first would break the running app.
- **`claude-bridge` cannot address a workspace.** It is application-scoped and keys by `SessionId` while the
  conversation instances are keyed by `WorkspaceId`, so `AgentStreamEvent` and friends have no route. With
  the bridge staying global, it learns the pairing from the `WorkspaceSessionStarted` it already receives.

**Gate:** the marketplace compiles on every launch against the `Nucleus.Contracts.dll` beside the host exe.
`ScopeOpened`, `ScopeClosed`, `SetActiveScope` and `IBus.Scope` are only on the host feature branch, so any
marketplace code using them breaks a launch from `main` until that branch lands.

> ## Launch verification, 27.07 - WP3/WP4/WP5 boot chain CONFIRMED
>
> Two launches against the real `~/.clavis` (config + state backed up as `*.bak-wp5test-20260727`).
> **The WP5 handover works end to end**, which was the open question:
>
> - `Conversation` activates (+02.758) and registers the `chat` kind (+02.861); `Workspaces` activates
>   (+03.003); **exactly one** session starts (+03.576) in the workspace's directory; `WpfHost` activates
>   *after* both (+03.846) - so `WorkspaceSessionStarted` went through the bootstrap buffer as designed.
>   One `claude.exe` child, not two.
> - `configuration.yaml` gained a correct `Workspaces` section: one workspace, slot 1, `Accent1Brush`,
>   `workingDirectory: C:\Users\fhertell\Repos\FS\clavis`.
> - `RetiredPanelKinds` rewrote the saved `conversation` slot to `chat`; no stuck placeholder.
> - The chat panel's `SavedState` blob round-tripped as
>   `{"workspaceId":"26e32960-…","chatId":"728ee53b-…"}` - WP3, WP4 and WP5 co-operating.
> - Rendering confirmed by window capture: init turn with session-start hooks, timeline rail, stats column,
>   the prompt input inside the chat panel (its framing line correctly clavis-blue when focused at the reveal
>   and frame-grey when not), the `title-bar-left` branch strip, the `title-bar-right` agent cluster, and the
>   status bar with `ctx 0/1M` + working directory. Slide-ins (`usage-limits`, `git-log`) preserved.
> - No `PluginError`, no crash, no non-viable startup. The recurring `Marketplace*` dead letters are the
>   pre-existing watcher noise.
>
> ### Closed anomaly: a secondary window closed itself on the COLD launch (once, not reproducible)
>
> On the **first** (cold, everything recompiling) launch, the restored secondary window holding `code-editor`
> closed itself ~16 s after the reveal, so its panel was lost from the saved layout. On a **warm** relaunch
> from the identical saved layout it survived, and instrumentation on all four close paths
> (`CloseIfEmptySecondary`, `ClosePanel`, `TogglePanel`, the cardinality dedupe) fired **none** of them.
>
> What is known: pre-WP3 logs contain no `WindowClosed` at all, so it cannot be dismissed as pre-existing.
> The marketplace watcher is **ruled out** - its first message in that window lands *after* the close. The
> log shows `WindowFocusChanged` → `WindowClosed` → `WindowFocusChanged`, i.e. the window was activated and
> then closed, which matches `RevealInstance`'s `BringToFront` followed by a close, but nothing logged a close
> decision. Timing-dependent, and only on a cold launch where background plugins activate 5-18 s after the
> reveal.
>
> **Resolution:** left as a single unexplained cold-launch occurrence. A second disappearance later the same
> session was the user closing the window by hand (confirmed), so there is no warm-launch reproduction. If it
> ever recurs, re-add the four `DIAG` log lines (reverted, not committed) and launch cold - i.e. after touching
> a source file so the item recompiles.
>
> ### Interactive checks - CONFIRMED BY THE USER
>
> WPF routes keyboard input through the foreground window's focused element, and a background process cannot
> take foreground (`SetForegroundWindow` is refused); `PostMessage(WM_CHAR)` to the top-level HWND does not
> reach the focused `TextBox`. So these remain unverified: **prompt submit**, **Up/Down history recall**,
> **`Ctrl+Up`/`Ctrl+Down` chat scroll**, the **permission `Left`/`Right`/`Enter`** keys, `Ctrl+P`, and
> **tear-off** (a real mouse drag). They are checks 2-6 below.
>
> ## The gate is passed - WP6 is unblocked
>
> The launch below was the precondition for WP6 and it succeeded. Kept for the record, and because the
> first-launch checks are the right smoke test after any future boot-path change.
>
> **Nothing has booted since WP2**, and WP3-WP5 are no longer just structural. Five contract majors have moved
> (session 3, host 3, layout 2, workspace 2 reusing a freed name) and - the part that actually matters - **WP5
> rewired the boot sequence**: session creation moved out of `Conversation` into the new essential `Workspaces`
> plugin, and `Conversation` now starts from `ConversationState.Empty` with **no chat at all** until
> `WorkspaceSessionStarted` arrives. If that chain has a flaw the symptom is stark: no chat and no prompt.
>
> The chain is sound as far as static review goes - `Workspaces` subscribes to `ConfigResult` before sending
> `GetConfig`, and the bus's bootstrap buffer holds a message until a subscriber appears, so
> `WorkspaceSessionStarted` reaches `Conversation` regardless of activation order - but that is not the same as
> having seen it work.
>
> **Do not start WP6 before a launch.** WP6 restructures the docking surface and layout persistence; stacked on
> an unverified boot rewrite, a failure would have four candidate causes and could not be isolated. The
> packages are small and individually revertible, which only helps if you know which one broke.
>
> Launch with `dotnet run --project src/FabioSoft.Clavis.Shell` from the host repo, in the background, and
> confirm via the newest `~/.clavis/logs/clavis-*.log`. The first launch also recompiles every item once (new
> `.buildspec` sidecars) and rebuilds the contract modules, so give it time.

**First-launch checks, in this order** (1-2 gate everything else):

0. **The boot chain (WP5).** A chat exists at all: the log should show `Workspaces plugin activated`, then a
   `StartNewSession`, then the chat panel filling. No chat and no prompt means the
   Workspaces -> Conversation handover failed - check the log for a `WorkspaceSessionStarted` dead letter.
   Also confirm exactly **one** `claude.exe`: two would mean both plugins are still starting sessions.
   `configuration.yaml` should gain a `Workspaces` section with one workspace in slot 1.
0b. **`exit` no longer quits** - it closes the active workspace; `quit` is the way out. Closing the only
   workspace is expected to leave an empty bar-less state, which is currently only recoverable via `workspace`
   (create) in the palette, so try `quit` first.
1. **The retired kind (WP3).** Over the existing `state.yaml`, whose primary window holds a slot of the retired
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

## WP5 - `workspace-contracts` 2.0.0 + the `Workspaces` plugin (headless) - DONE

Catalog gate green: 40/40 items, 24/24 test suites, 77/77 dependency edges (workspaces 44 tests, palette 45).
Bumps: new `workspace-contracts 2.0.0` and `workspaces 1.0.0`; `host-contracts 3.1.0` (`ExitApplication`
added); `conversation 10.0.0` (its config lost `WorkingDirectory`/`Model`); `command-palette 2.0.0` (`exit`
changed meaning); `wpf-host 4.1.0`.

**Session creation moved, which is the load-bearing part.** `Workspaces` mints the session id and sends
`StartNewSession` with the workspace's directory; `Conversation` starts from `ConversationState.Empty` and
creates a chat when `WorkspaceSessionStarted` arrives, switches the visible chat on `WorkspaceActivated`, and
drops it on `WorkspaceClosed`. `ConversationConfig.WorkingDirectory`/`Model` are gone, and
`StartNewSessionEffect` now carries the chat's directory so a restart still knows where to run. There is
therefore exactly one place a session is born, which is what WP6 onwards depends on.

Deviations and decisions:

1. **Accent assignment is deterministic (least-used), not random.** The plan said randomly assigned;
   least-used-wins guarantees no two workspaces collide until there are more than four, which is the outcome
   random assignment was reaching for, and it is testable. `AccentPalette.Next` covers the re-roll gesture.
   The four keys are `Accent1Brush`..`Accent4Brush`, added to the host theme from the identity family only
   (`#ADA6F2`, `#9FD5F0`, `#C79BF0`, `#8FBEEA`).
2. **Slot 0 means "no slot".** F1-F11 is the cap by construction, so a workspace created when all eleven are
   taken gets slot 0: reachable by click or from the overview, no key hint. `InSlotOrder()` puts the slotless
   ones last, which is also the render order the bar wants.
3. **`Create` takes an explicit slot.** `ActivateWorkspaceSlot` on a free slot must give you *that* slot, not
   the lowest free one - pressing F5 on an empty bar lands in slot 5. The generated name follows the slot it
   actually took.
4. **Effects, not bus messages, out of the pure core.** `WorkspaceUpdate` returns
   `StartSessionEffect`/`DisposeSessionEffect`/`ActivatedEffect`/`ClosedEffect`/`SessionStartedEffect`, and
   the shell translates them. Same shape as `ConversationEffect`, and it keeps the slot and lazy-start rules
   testable without a bus.
5. **Activity never writes the file.** An activity change re-announces the list but skips persistence, so a
   streaming turn does not rewrite `configuration.yaml` four times a second.
6. **The palette needed no change to reach the new commands.** `MessageCatalog.Discover()` scans every loaded
   `FabioSoft.Contracts.*` assembly, so the `[<Description>]`-carrying workspace messages become palette
   commands as soon as the module is loaded. Only the aliases were touched: `exit` -> `CloseActiveWorkspace`,
   new `quit` -> `ExitApplication`, plus `workspace` and `workspaces`.

7. **The Selection half landed too** - the WP2 deferral is closed. Its single `volatile AgentCapabilities`
   became a `ConcurrentDictionary<Guid, AgentCapabilities>` plus a visible-session id fed by
   `WorkspaceActivated` (and by `WorkspaceSessionStarted`, since activation reports `Guid.Empty` for a
   workspace whose session has not started yet). The rule is the pure `SessionCapabilities.Resolve`: the
   visible session wins; failing that a **sole** snapshot is used, which keeps the one-workspace case behaving
   exactly as before; with several snapshots and no visible session nothing is offered, because there is no
   honest answer. The pickers needed no other change - they already send `capabilities.SessionId`, so a pick
   now targets the visible session by construction.

### Original scope

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

## WP5b - Agent instances: discover, adopt, hand off - BRIDGE DONE AND VERIFIED (consumed by WP5c)

The bridge half landed in full: the provider-neutral contracts, the pure `AgentInstances` module,
`ClaudeCommand.withBackground`, and the ClaudeBridge wiring (`AgentInstancesRequested` -> discovery,
`AdoptAgentInstance` -> stop-then-`--resume`, `ReleaseAgentInstance` -> stop or `--bg --resume`). Catalog
gate green: 40/40 items, 24/24 suites, fabiosoft-claude 193 -> 222, claude-bridge 78 -> 93.

The mechanism itself is not theoretical: the hand-over was verified end to end against real agents on 29.07, in
both directions. It had **no consumer** when it landed, which WP5c below fixes - and WP5c's requirements changed
two decisions recorded here, so read that section too before trusting this one's Still-open list.

### What running the real CLI changed

The shape here was verified against `claude agents --json` rather than assumed, and that corrected two things:

- **`startedAt` is epoch milliseconds, not a timestamp string.** The first cut read it as a string, so every
  real instance dated to `DateTimeOffset.MinValue` while the tests passed on an invented ISO fixture. Fixed, with
  a regression test naming the real format and a string fallback kept for a future format change.
- **The listing contains every live session on the machine** - the user's own editors and terminals, not just
  Clavis's agents (`kind` is `background` for all of them, and status is `busy`/`waiting`, not `running`).
  Offering those for adoption would let Clavis `--resume` a conversation somebody is holding open, which is
  exactly the two-processes-on-one-transcript corruption this design set out to avoid.

So **ownership had to become explicit**: every session Clavis starts is named `clavis/<label>` via `-n` (the
workspace name, else the working directory's last segment), and only marked instances are offered. The name went
from cosmetic to load-bearing, which is why `StartNewSession` gained an optional `Name` and `SessionConfig`
gained `Name`/`ResumeSessionId`/`SessionId`.

Observed in the wild while investigating, which argues the exclusivity rule guards something real: two live
`claude` processes were sharing one session id on this machine, with no Clavis involved.

### Decisions

- **Adoption is exclusive and claimed before the spawn** (`AgentInstanceRegistry`). A refused claim costs
  nothing; two owners corrupt the transcript.
- **Release waits for the running turn** (`TurnGate`, started on `SendPrompt`, cleared on `AgentResult`), up to
  `HandOffTurnWaitSeconds`, then proceeds and logs the loss - handing back restarts the process over the
  persisted transcript, so an unfinished turn is gone, but a wedged one must not block shutdown.
- **The owned stream is always disposed before the background agent spawns.** The two must never overlap.
- **Adoption resumes in the instance's own directory**, cached from the last discovery pass, so taking over an
  agent never silently moves it.
- **Nothing releases automatically - *in the bridge*.** `DisposeSession` still ends an agent; keeping one alive
  needs an explicit `ReleaseAgentInstance(keep-running)`. The bridge deliberately holds no shutdown policy: it
  belongs to whoever owns the workspace lifecycle. WP5c is that owner deciding - `Workspaces` parks every session
  on the way out - so the detached-agents-nobody-tracks concern is answered by the same launch reclaiming them,
  not by refusing to park.

### Still open

Three items that were open here are now closed by WP5c below: nothing consuming the family, the busy-agent
interrupt, and the shutdown policy. What remains:

- **Two Clavis homes** still share the provider's session store. Exclusivity guards one home; cross-home
  coordination needs out-of-band state. Mitigated by adoption being an explicit user pick, never automatic.
- **Crash** leaves no hand-off: the session stays resumable but is not still running, and the bar must not
  claim otherwise. Note WP5c narrows this - a crashed run's conversation is still reopened from its persisted
  session id, so the loss is the *running* agent, not the conversation.
- **Orphans** - marked agents nobody reclaimed - surface in the overview panel (WP9).

### Original scope

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

**Correction, 29.07: the earlier claim that no per-agent local channel exists was wrong.** It said the only
Claude named pipe on the machine was `claude-mcp-browser-bridge`. That came from grepping the pipe list for
`claude|anthropic|mcp` - the daemon's pipes are named `cc-daemon-*`, so the filter hid them. There is a
supervisor daemon per Claude home (`claude daemon`, a hidden but real subcommand: `run`/`status`/`logs`/
`stop`), it owns a pty per background agent, and it describes them in `<home>/daemon/roster.json`. Each
agent is two processes: a `--bg-pty-host` wrapper (the roster's pid) and the agent CLI beneath it (the pid
`sessions/<pid>.json` registers).

**There is still no local *attach* in the sense of joining.** `--resume` starts a new process over the
persisted transcript, and the CLI refuses it outright while a background agent holds the session
(`Session <id> is currently running as a background agent`). So the corruption this design feared was never
reachable - the CLI already guards it - and resume remains *take over*, never *join*.

**What makes take-over work is `claude stop <agent-id>`**, a documented top-level command that ends a
background agent. Adoption stops the agent, then resumes its conversation, which survives intact. Verified
end to end against a throwaway agent: told a marker, stopped, resumed through the stream-json path Clavis
uses, still knew the marker. Reverse-engineering the daemon's control protocol was investigated and then
abandoned as unnecessary - it exists only as compiled V8 bytecode, and `claude stop` is the supported way to
do the one thing it was wanted for.

### The facade (provider-neutral - no `claude`, no `--bg`, no pid in any contract)

```fsharp
type AgentInstance(instanceId: string, name: string, workingDirectory: string,
                   status: string, startedAt: DateTimeOffset, isAdopted: bool, isOwned: bool)
type AgentInstancesRequested()
type AgentInstancesAvailable(instances: IReadOnlyList<AgentInstance>)
[<Description("Take over an existing agent instance")>]
type AdoptAgentInstance(instanceId: string, sessionId: Guid)
/// mode: "keep-running" (hand back to the background) or "stop".
type ReleaseAgentInstance(sessionId: Guid, mode: string)
type AgentInstanceAdopted(sessionId: Guid, instanceId: string)
type AgentInstanceAdoptionFailed(sessionId: Guid, instanceId: string)
type AgentInstanceReleased(instanceId: string, keptRunning: bool)
```

`StartNewSession` stays, but becomes *one* way to obtain an instance rather than the only one. The facade
stops assuming Clavis spawns and owns a process; lifecycle is the provider's business.

### What ClaudeBridge does underneath

- **Adopt**: `claude stop <agentId>` first - the CLI refuses to resume a session its agent still holds - then
  `claude --resume <sessionId> --input-format stream-json --output-format stream-json`, so the existing
  transcript continues in a Clavis-owned process. If the agent will not stop, the claim is released and
  `AgentInstanceAdoptionFailed` says so, because a caller waiting on `AgentInstanceAdopted` would otherwise
  wait for a session that never starts. Stopping needs the CLI's short handle (`id`), which is **not**
  derivable from the session id - the real listing addressed session `2b3bba05` as `a7683d47` - so
  `AgentInstanceInfo` carries both, learned from a discovery pass.
- **Create**: as today but with `--session-id <uuid>` and `-n <workspace name>`, so the session is durable,
  identifiable, and visible in `claude agents`.
- **Release (keep-running)**: end the owned stream, then `claude --bg --resume <sessionId>` so it carries on.
- **Discover**: `claude agents --json`, mapping `sessionId`/`id`/`name`/`cwd`/`kind`/`status` onto
  `AgentInstance`.

**Ownership is a label, `kind` is the gate (29.07).** The `clavis/` marker originally decided who could be
adopted, on the reasoning that resuming a foreign session would hijack it. Since adoption now stops first,
that no longer holds: an agent started in the CLI's own agent view is a legitimate hand-over target, which is
what the user asked for. The marker survives as `IsOwned` so the UI can show whose agent it is. The real gate
is `kind`: **background** agents can be stopped and resumed; **interactive** sessions are somebody's terminal
and stopping one is a hijack, not a hand-over, so they are filtered out - including Clavis-marked ones,
because ownership was never what made it safe. A listing that stops reporting `kind` yields no targets rather
than treating every terminal as one.

Tests: the pure mapping from `claude agents --json` (malformed row, absent `name`, a `cwd` outside every
workspace, epoch-millisecond and string timestamps, a foreign name kept but not owned, the short handle being
distinct from the session id), the release-mode decision table, the hand-off and stop argument lists,
adoption refused when already held / interactive / of unreported kind but allowed for a foreign background
agent, plus the registry's exclusivity and the turn gate's wait/timeout behaviour.

---

## WP5c - Continuous sessions: resume, fleet tabs, take-over on switch, park on close - DONE, NOT RUNTIME-VERIFIED

The bridge from WP5b was correct and unreachable. This package is what reaches it, from four requirements:
sessions listed in both Clavis's tabs and the provider's own agent view; the last session active again on
launch; switching to a tab whose agent is running elsewhere takes it over; and a take-over that has to wait
says so rather than interrupting.

### Three more verified CLI facts, each of which changed the design

Probed directly rather than assumed, and all three cost more than they look:

- **A session Clavis holds is listed as `interactive`, with no short handle.** Spawning with Clavis's exact
  argument shape (`--print --input-format stream-json -n clavis/...`) appears in `claude agents --json` as
  `kind: interactive`, `entrypoint: sdk-cli`, and **no `id` field**. The short handle is what every take-over
  command takes, so **visibility is symmetric while ownership is not**: the other side can see a Clavis session
  and can only take it over after Clavis has handed it back. The first requirement is therefore already half
  satisfied by doing nothing, and its other half is not achievable at all - worth knowing before building
  toward a shared-ownership model that cannot exist. (Incidentally this is also why the `kind` gate correctly
  excludes Clavis's own live sessions from adoption, for a reason WP5b had not actually verified.)
- **Parking mints a *new* session id.** `--bg --resume f7f4dd65…` came up as `f293c646…`. The spawn is
  fire-and-forget, so Clavis never observes the new id. **Nothing may persist an instance id expecting to find
  that agent later**, which is why reclaiming matches on name plus working directory instead. `--bg` does print
  the new short handle (`backgrounded · f293c646 · clavis/probe-park`), and an earlier draft persisted that -
  dropped, because the handle is only meaningful while that exact agent lives, and name matching needs nothing
  extra written down at all.
- **The status vocabulary is `busy` / `idle` / absent**, with a separate `state` of `working`/`done`/`blocked`;
  a blocked agent reports no `status` at all. So `isWorking` requires a *positively* reported `busy`: waiting on
  a status the provider never sends would wait forever and never hand over, and a lost turn is recoverable where
  a take-over that can never happen is not.

Also corrected: WP5b's claim that `kind` is `background` for every listed session. It is not - print-mode
sessions are `interactive`.

### The shape

- **Three ways to obtain a session, with a priority that matters** (`SessionPlan`). An agent running this
  workspace's conversation is **taken over**; else a remembered conversation is **reopened** from its
  transcript; else one is **started fresh**. A running agent always wins, because resuming a session an agent
  still holds is refused by the provider - and were it not, it would fork the conversation and lose whatever
  the agent had done since.
- **`ResumeSession`** is a plain `--resume`, deliberately *not* the adoption path: there is nothing to stop, and
  requiring an agent to let go first would fail every resume of a session whose agent is simply gone.
- **The first activation waits for the first discovery answer.** This is the subtle one. Activation used to
  happen the moment config arrived; deciding the plan before knowing what is running would always read as
  "nothing is running", so a parked agent would be left running while its transcript was reopened separately -
  one conversation with two lives, plus an orphaned agent. Bounded (`InitialDiscoveryWaitSeconds`, 6s) because a
  missing provider must not leave Clavis with no chat; giving up early only costs a take-over.
- **Fleet tabs**: every background agent no workspace claims becomes a **slotless, unpersisted** tab, drawn with
  a `~` where the slot number goes and a dim tick instead of an accent. Activating one takes it over and
  **promotes** it to a real workspace. Slotless on purpose - a short-lived agent somebody spawned must not
  consume one of eleven F-keys, and it is not a place of yours until you adopt it.
- **A busy agent is waited out, not interrupted.** Adoption polls until the target stops reporting `busy`,
  publishing `AgentInstanceAdoptionWaiting`; the chat shows the notice and one gesture, `ForceTakeOver`, whose
  wording says what it costs. The default wait is unbounded on purpose: a timeout would eventually interrupt
  somebody's work silently, which is the single decision that belongs to the user. This replaces WP5b's
  "the picker should confirm first" - waiting is the better answer, and it needed no dialog.
- **A superseded wait must report nothing.** Forcing re-issues the same adoption without the wait, and the
  original wait loop is still polling. Left alone it would find the agent gone, conclude failure, and undo the
  take-over that had just succeeded. A second adoption of an instance now cancels the first wait, which returns
  `Superseded` and touches neither the claim nor the bus.
- **Quitting parks every agent, behind a shutdown barrier.** `ApplicationShutdown` calls `app.Shutdown()`
  immediately with no drain, so parking would have raced process exit - and parking has to *spawn* a process
  while Clavis is still alive to spawn it. Hence two-phase quitting in `host-contracts` +
  `wpf-host` (`ShutdownParticipant` / `ShutdownPreparing` / `ShutdownPrepared`, `ShutdownGraceSeconds`).
  **The barrier always opens**: a participant that never answers delays the quit, never prevents it. With
  nothing declared the behaviour is byte-for-byte what it was.

### Decisions

- **The provider session id is durable config, not disposable state.** It sits in the `Workspaces` section of
  `configuration.yaml` beside name and slot, because losing your layout must not lose your conversations. This
  reverses WP5's "nothing live is persisted" - a transcript id is closer to "this workspace is a continuing
  conversation" than to a docking position.
- **An ambiguous reclaim reclaims nothing.** Two agents answering to one name in one directory means there is no
  way to tell which conversation is the workspace's; attaching it to the wrong one is worse than to neither, and
  both still appear as fleet tabs so nothing is hidden.
- **The workspace↔agent pairing lives in `workspaces`, not in `fabiosoft-claude`.** It was written in the module
  first and moved: it needs a workspace's name and directory (workspace knowledge) and nothing provider-specific
  beyond what `AgentInstance` already carries, and a UI plugin must never reference a provider assembly. What
  *is* provider knowledge - the status vocabulary - stayed.
- **An adopted agent keeps its name.** Reclaiming matches on the name, so letting adoption rename a session to
  something directory-derived would quietly make it unreclaimable. A foreign agent gains the `clavis/` marker in
  front of its label, because adopting it does make it Clavis's.

### Launch verification, 30.07 - three of four mechanisms confirmed, one regression found and fixed

Launched against the real `~/.clavis` (config + state backed up as `*.bak-wp5c-20260730`).

Confirmed working:

- **The discovery gate**: `discovered 5 reclaimable agent instance(s)` at +29.8s, session obtained at +30.5s -
  the answer really does precede the decision.
- **Persistence**: every workspace gained an `agentSessionId`. The resume path itself only proves out on a
  *second* launch.
- **Park on close**: all four sessions handed back and confirmed `kept running: True`; `claude agents` then
  listed `clavis/Workspace 1…4` as live background agents.
- **The busy-wait, on a live target that mattered.** A fleet tab for the *developing session itself* was clicked;
  the log reads `2b3bba05… is still working; waiting for its turn to finish` and the agent survived. Had
  `isWorking` been wrong in the other direction it would have stopped the session doing the work.

**Regression found: every tab showed no panels.** Layouts are stored per *(window, workspace)*, and the saved
layout was keyed to the empty workspace id while the live workspace was `26e32960` - so nothing matched and every
surface came up bare. Cause: this package delayed the first `WorkspaceActivated` until discovery resolved, so
`wpf-host` restored its layout while no workspace was active yet. Fixed by deferring **only** the
`ObtainSessionEffect` and activating immediately - the two were already separate effects, and delaying both was
the error. Being the active workspace needs no discovery answer; choosing a session route does.

Two smaller defects fixed with it:

- The `ShutdownPrepared` handler dead-lettered with an NRE: an answer can arrive after `Application.Current` is
  already null, and it marshalled onto the dispatcher unconditionally.
- **The grace period was consumed by queue latency, not by the work.** The clock starts when `ShutdownPreparing`
  is *sent*, and the Workspaces subscriber channel took nearly twelve seconds to reach the broadcast (the
  window closed at 11:51:05.8; the 12s timer fired at 11:51:17.806, 326ms after parking began). Parking survived
  only because the hand-off is a fire-and-forget spawn that outlived the process - luck, not design. Grace raised
  to 30s, sized for latency plus work, and the elapsed wait is now logged.

Also observed, not a defect: pressing an unused F-key **creates** a workspace (activate-or-create, by design), so
switching to empty slots minted Workspaces 2-4 and spawned an agent each.

### Still open

- **The resume path is still unverified** - it needs a second launch against the now-parked agents. The other
  three mechanisms are confirmed.
- **Persist churn.** The quit exposed repeated `SaveConfig` round trips (`dead-letter ConfigSaved` five times in
  one second), which is the likeliest reason the Workspaces channel ran twelve seconds behind. Worth a look: the
  grace period is currently absorbing a symptom.
- **`FleetPollSeconds` costs a provider process per poll** (15s default). Fine for one machine; worth revisiting
  if the listing ever gets expensive.
- **Two fleet tabs for one conversation** are possible if the provider ever lists an agent twice under different
  ids; the synthetic workspace id is derived per instance id, so they would not collide, they would just both
  appear.

### Tooling

`tools/Install-Module.ps1` closes a real gap rather than working around one: items compile in alphabetical order
against whatever is already installed in `~/.clavis/modules`, and neither the host nor the CompileTest harness
leaves an updated module there - so a contract added alongside its consumer fails with CS0246 (`claude-bridge`
sorts before `session-contracts`). It synthesizes the project from `PLUGIN.md` frontmatter, builds against the
same three reference roots the kernel probes, and deletes the project again so the marketplace stays pure source.

---

## WP6 - Per-workspace surfaces + layout v2 - LAYOUT + WINDOWS DONE, SURFACES REMAIN

Split in two so each half is committable. **Layout v2 + migration has landed**; the N-surface machinery has
not. Catalog gate green after the first half: 40/40 items, 24/24 suites (wpf-host 67 -> 78).

### Landed: layout v2, migration, and window/workspace ownership (`wpf-host 5.0.0`)

The persisted shape is normalised exactly as designed: a `windows` list of (identity, **role**, **workspace**,
bounds) and a separate `layouts` list of one docking tree + slide-ins per **(window, workspace)** pair, plus
`activeWorkspaceId` at the top. Geometry stays on the window so the primary's bounds are not duplicated per
workspace. `WindowRole` (`primary`/`panel`) replaces the `isPrimary` bool - WP7 needs a third role for the bar,
which a bool cannot express.

- **Version 1 is migrated, not discarded.** `LayoutMigration.FromVersion1` lifts each v1 window's inline tree
  into a `layouts` entry. The host does not know a workspace while reading the file, so migrated entries carry
  `Guid.Empty` as an explicit **"unassigned"** marker - *not* an orphan - and `LayoutMigration.Adopt` binds them
  on the first `WorkspaceActivated`. Treating unassigned as orphaned would silently delete everyone's layout,
  which is why `DropOrphans` deliberately keeps `Guid.Empty` entries.
- **`DropOrphans` is written and tested but not yet wired** - it belongs to the second half, on the first
  `WorkspaceListChanged`.
- **Reveal gating untouched**, as the plan requires.
- `WindowHost.WorkspaceId` exists and is captured; a secondary window records the workspace it was torn off in.
  Nothing hides or shows by workspace yet - that is the second half.
- Carrying over unshown workspaces: `CaptureLayout` keeps the layouts of workspaces that are not on screen from
  the last-read file, so saving while workspace 1 is visible does not erase workspace 2's arrangement.
- `wpf-host` now declares `workspace-contracts` and subscribes to `WorkspaceActivated`. That is intended by
  WP6 - the host becomes workspace-aware here - but note it still knows no *session* vocabulary.

Tests: `LayoutFileTests` rewritten for v2 (round-trip with a split tree, geometry-not-per-workspace, slide-ins,
a secondary carrying its workspace, a future version discarded) and a new `LayoutMigrationTests` (v1 -> v2
end-to-end, geometry and slide-ins landing in the right place, unassigned-not-orphaned, adopt binding /
not-rebinding / no-op, orphan dropping, unassigned surviving a drop).

### Also landed: window-level workspace ownership (`wpf-host 5.1.0`)

- **`OrderedWindows()` is scoped to the active workspace**, and that turned out to be the whole job for the
  window half: it is the single funnel the reveal, summon, banish *and* the cross-window Tab ring all read, so
  filtering there makes a foreign workspace's window uniformly absent from every one of them rather than each
  site remembering. An unassigned secondary (`Guid.Empty`, pre-adoption) counts as present, so it is never
  stranded invisible.
- Switching workspaces hides the other workspaces' secondaries and fades this one's back in
  (`ApplyWorkspaceWindowVisibility`), guarded to do nothing before the reveal or mid-slide - hiding during a
  slide would capture an animated position as a window's resting place.
- A secondary's layout is captured under **its own** workspace, not the active one, so a hidden window is not
  refiled to whatever you were looking at when the debounced save fired.
- `DropOrphans` is wired to the **first** `WorkspaceListChanged` (one-shot: later lists reflect in-session
  creates and closes, which the live windows already track).

### Remaining - WP6c: N surfaces per window

The last piece is one `DockingSurface` per workspace *inside* a window (`WorkspaceSurfaces`, lazily created,
hidden, `Motion.crossfade` on switch, `WindowHost.Surface => _surfaces.Active` so the ~40 call sites compile
unchanged), plus per-workspace `RestorePanel` on first activation.

**Why it was not done here, and what has to happen first.** `WindowHost` builds three collaborators that each
capture *one* `DockingSurface` for the window's lifetime: `ActivePanelWatcher` (drives the title/status chrome
from the active panel), `FocusVisualController` (the focus ring) and `PanelTitleController` (the secondary
window's title cross-fade). With N surfaces they would all keep watching surface #1, so on workspace 2 the
chrome would report the wrong active panel and the focus ring would track a hidden tree. **Make those three
surface-agnostic (or re-target them on switch) before introducing `WorkspaceSurfaces`** - that is the real
first step of WP6c, not the surface container itself. A `WorkspaceSurfaces` draft was written and deliberately
discarded rather than committed unwired; its shape is described above.

### Original scope

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

## WP7 - The bar - DONE

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

## WP8 - F1-F11 / F12 - DONE

**Scope: `Application`, definitively not `System`.** System scope means `RegisterHotKey` on the primary
HWND, stealing F1-F12 from *every application on the machine* (F1 is help everywhere, F12 is devtools).
`GlobalHotkey.TryVirtualKey` already maps F1-F24, so this would silently "work" and be a disaster. Keyboard
switching therefore needs a focused Clavis window; the bar's click covers the hidden case.

**"A focused window" is not enough - it has to be a focused *element*, and switching destroys it.** WPF routes
key presses from the focused element outwards, so `Window.PreviewKeyDown` - the single key resolver - never
runs when nothing inside the window holds focus. Activating a workspace replaces the whole surface
(`WindowHost.ActivateWorkspace`), which takes the focused element out of the visual tree with it, and nothing
put focus back: so **the F-key that switches workspace killed the next F-key press**, until the incoming chat
panel finished loading and took focus by itself. That read as "switching only works sometimes, and works again
if you wait a moment" - the moment being the session start on a workspace's first visit. Two parts to the fix:
`WindowHost` keeps a `Focusable`, non-tab-stop root as a fallback (`EnsureWindowFocus`, matching what the
secondary layout already did), and the `WorkspaceActivated` handler calls `FocusActiveWorkspace` - park focus
on the window, then ask the new workspace's chat for it via `FocusInputRequested`, deferred a tick because a
first visit is still materialising its panels. That also fixes the second half of the same defect: after a
switch the caret now lands in the new chat's prompt instead of nowhere.

**The real reason a switch stalled for seconds was thread-pool starvation, not the key press.** With tracing
on both ends it was clear the press always arrived and always resolved to `ActivateWorkspaceSlot`; the message
then sat undelivered for **1.5 to 11.4 seconds** while the handler itself reported `waited 0ms for the lock,
then worked 6-52ms`. The log went silent during the gap and then burst - and the log sink is a bus subscriber
too, so the whole delivery layer was blocked, not the workspaces plugin. Every bus subscriber is pumped on a
thread-pool thread, and the marketplace lifecycle pipeline runs CPU-bound Roslyn compiles plus a blocking
`WaitForExit` on that same pool; the pool only grows by roughly one thread per second, which is exactly the
shape of the delays. Two changes: `Program.raiseThreadPoolFloor` lifts the minimum worker/completion-port
count before anything boots, so the pool does not have to climb, and `LifecyclePipeline.RunAsync` starts its
work with `TaskCreationOptions.LongRunning` on a dedicated thread instead of occupying a pool thread for the
duration of a build. Measured after: 20+ rapid switches with background recompiles running delivered in
**1-34 ms**.

Two things that look like the obvious next step and are both wrong. **Do not cap the pipeline with a
semaphore** - a `SemaphoreSlim(ProcessorCount / 4)` gives 2 slots on an 8-core machine, and the startup
reconciliation sweeps the whole catalog, so the next edit queues behind that sweep and the `WatcherTest`
harness times out (it did; the regression was confirmed by reverting to baseline and re-running). And **do not
move bus consumers onto dedicated threads** - there are hundreds of subscribers, and `async`/`await`
continuations return to the pool regardless.

**The bar's click had its own, unrelated way of doing nothing.** The strip re-rendered by clearing and
rebuilding every tab on each `WorkspaceListChanged` - and a session's *activity* changes that list while an
agent works. A tab replaced between its mouse-down and mouse-up never sees the up, so the click was dropped;
falling quiet made it work again. `WorkspaceBarView` now keeps one `WorkspaceTab` per workspace alive across
renders and only applies what changed, which also stops the breathing dot restarting on every activity tick.

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

## WP9 - The overview panel (F12) - DONE

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

## WP10 - Docs + agent surface - MARKETPLACE SIDE DONE, HOST REPO REMAINS

Landed: `docs/MESSAGE-MAP.md` regenerated (136 messages, 27 components), `docs/CODEMAP.md` refreshed, and the
agent surface gained **`workspaces_list`** and **`activate_workspace`** (`agent-gateway 3.1.0`). Note the map
shows `ActivateWorkspaceSlot` and `ExitApplication` with no publisher - correct, not a defect: both are
dispatched reflectively by the palette's `MessageActivator` from a keymap binding, which a static scan cannot
see.

**Remaining: the host repo** (`~/Repos/FS/clavis`) - its `CLAUDE.md` Terminology section needs the Workspace
definition, and `docs/{CODEMAP,DEPENDENCY-MAP,CORE-AND-BUS}.md` need the new module and plugin. Deferred only
because a background session must edit that repo through `EnterWorktree`, which was not worth opening for a
docs-only change mid-flight.

### Original scope

Marketplace `docs/CODEMAP.md`; regenerate `docs/MESSAGE-MAP.md`
(`tools/Generate-MessageMap.ps1 -CoreSrc <clavis>/src`); `MARKETPLACE.md`; `plugins/wpf-host/PLUGIN.md`
(also fix its existing stale `config/WpfHost.yaml` vs `state.yaml` contradiction and the `usage-pace` ->
`usage-limits` slip); new `plugins/workspaces/PLUGIN.md`. Host repo `CLAUDE.md` +
`docs/{CODEMAP.md, DEPENDENCY-MAP.md, CORE-AND-BUS.md}` - the Terminology section needs the new
Workspace definition and the layout rename. `agent-gateway/{ClavisTools.cs, ClavisDocs.cs}`:
`layout_snapshot` plus new `workspaces_list` / `activate_workspace` tools.

---

## WP11 - One window per workspace - DONE, **not runtime-verified**

> **Landed** as `8d6a156` (marketplace) plus `af0c4f7` (host repo: the CompileTest gap below). Catalog gate:
> 40/40 items compile, wpf-host 109 -> 113 tests. **Nothing has been launched** - a window-model change is
> exactly the kind whose correctness lives in a running app, so the first-launch checks at the end of this
> section are the real gate.
>
> **Deviations from the plan below, all deliberate:**
>
> 1. **`WorkspaceSurfaces` was not deleted and `WindowHost` was not touched beyond its close cross.** Each
>    workspace window simply activates onto exactly one workspace and never swaps, so the N-surface machinery
>    degenerates to one surface per window on its own. Deleting it would have meant rewriting `WindowHost`,
>    `ActivePanelWatcher`, `FocusVisualController` and `PanelTitleController` for no behavioural gain, in the
>    same commit as a window-model change. It can go later, on its own, when something is watching.
> 2. **`IsPrimary` was kept, meaning "is a workspace window".** Renaming it reaches `WindowSnapshot.IsPrimary`
>    and `agent-gateway`'s tool output, which is agent-visible surface and deserves its own justification.
>    Recorded here rather than done quietly.
> 3. **The bar did not become `Application.Current.MainWindow`, and did not need to.** `WindowManager.Register`
>    already reassigns MainWindow on every window's `Activated`, and the bar is never activated - so MainWindow
>    tracks the focused workspace window, which is what popup placement wants. "The bar is the application"
>    is therefore about *lifetime*, and that is delivered by workspace windows refusing to close.
> 4. **Region contributions are moved, not duplicated, for the unscoped case.** See the corrected claim below:
>    the factory *could* build per window, but today's contributors return one long-lived element from it. The
>    `WorkspaceId` seam is in place for a contributor that grows per-workspace state.
>
> **Two further causes of the duplicate chats were found and fixed here**, neither of which is about the window
> model - they were simply invisible until the layout was read carefully:
>
> - **Window ids are minted per launch** while a saved layout names the previous launch's, so a workspace's
>   chrome window could never be matched by id across a restart. `NeedsDefaultPanels` then answered "nothing
>   restorable", the workspace was seeded a fresh chat, and its saved chat slot was carried over untouched -
>   two chats, which is precisely what the live `state.yaml` held. Fixed by `LayoutMigration.RebindWorkspaceWindow`
>   (the workspace is the stable identity, not the window id), with four tests.
> - **The boot restored the active workspace's panels and the first `WorkspaceActivated` restored them again**,
>   two `RestoreRequest`s per saved panel. Restoring is now idempotent per workspace.
>
> **The catalog gate had a blind spot that hid the first compile error of this package** and is fixed in the
> host repo: `CompileTest` resolved every reference from `~/.clavis/modules` - the assemblies the *last launch*
> installed - so a changed contract module was invisible to its own dependents, and the harness reported green
> on a catalog that could not compile together. It also sorted the whole catalog by name, interleaving modules
> with the plugins depending on them. Modules now build first, into a scratch dir the dependents reference.
>
> **Known unrelated flake:** `ClaudeBridgePluginTests."StartNewSession sends Initialize handshake to session"`
> fails under full-catalog load and passes in isolation (97/97). It waits on a fixed `Task.Delay(100)`. Not
> touched here.

### Original plan

This reverses the deferral in "Two questions answered up front". That section kept the primary window as the
application's persistent presence and called the underlying question - *what is the app's persistent
presence?* - deliberately deferred. It is now answered: **the bar is.** Each workspace gets its own window;
the bar outlives all of them and is the only way out of the application.

**What forced it.** Not aesthetics - a defect. All workspaces share one surface stack in one window, and
`CaptureWorkspaceLayout` reads `host.Surface` (the *active* surface) and files it under `_activeWorkspaceId`.
The two are set from different places, so any drift between them writes one workspace's surface into another
workspace's layout. A live `state.yaml` showed exactly that: workspace `26e32960` with two `chat` slots
carrying the same `chatId`, and workspace `b098b3ec` additionally carrying `26e32960`'s two chat panels
(YAML anchors `*o3`/`*o4` - the same objects in both entries). One window per workspace removes the shared
mutable "which surface is active" entirely, so this class of bug has nowhere left to live.

**Two claims from the earlier analysis were wrong and are corrected here**, because both were used as
arguments against this shape:

1. **"A contributed chrome element cannot appear in N windows because a WPF element has one parent."**
   False. `UiRegionContribution` carries a `ViewFactory: Func<obj>`, and `RegionManager.AddContribution`
   invokes it per contribution (`RegionManager.cs:43`), so every window gets its own element. The comment at
   `WindowManager.Subscriptions.cs:17` states this same wrong reason for its routing rule; the rule (one
   owner per region) is right, its justification is not. What is actually true is narrower: two elements from
   one factory bind to the *same view model* and would show the same workspace's data. That is a state
   problem, and the state already exists per workspace - WP4 gave Conversation one `ConversationViewModel`
   per chat, WP5 gave Selection per-session capabilities.
2. **"The bar as the app's presence jams popup placement, because `SelectorWindow` centres on
   `Application.Current.MainWindow`."** Does not apply. `WindowManager.Register` reassigns
   `Application.Current.MainWindow` on *every* window's `Activated`, and the bar is `ShowActivated = false`
   plus `NoActivateWindow`, so it never becomes MainWindow. MainWindow already tracks the focused window, so
   popups already follow it. `Selector.fs:548` needs no change at all.

**Path A over path B.** The alternative considered was a scope per workspace: a sub-bus and a plugin instance
per workspace, so each workspace's plugins build their own chrome and no plugin need know workspaces exist.
The contract permits it - `IPlugin.ActivateAsync(bus, config)` returns an `IDisposable` and says nothing
about singletons; it is the kernel that activates once. It was rejected for this package because
**`wpf-host` must stay global under it anyway** - it owns every HWND, and a second window owner would be a
genuine second truth. A region contribution would therefore *still* have to name its workspace, so path B
contains path A whole and adds a kernel rework (bus hierarchy, a global-vs-workspace plugin taxonomy, and
re-aggregation for `LogSink`, dead letters, the EventsPanel firehose and the bar's activity). B remains the
right answer if real isolation is ever the goal; it is not the cheapest way to per-workspace chrome.

Decisions taken with the user, to be implemented:

- **A workspace window has no close cross.** Closing a workspace happens from the bar or the palette; the
  chat panel inside it is already unclosable, so neither gesture can strand a workspace without a chat.
- **Exactly one workspace is visible.** Activating one shows its windows and hides the others -
  `ApplyWorkspaceWindowVisibility` already does precisely this for secondaries; it loses its `!IsPrimary`
  filter. Background panels keep running, which is what makes the bar's activity indicator mean anything.
- **The bar is the lifetime anchor.** A workspace window's `Closing` no longer calls `BeginShutdown`;
  quitting is `ExitApplication` and the bar.

Shape of the work:

- `WorkspaceSurfaces` is deleted. `WindowHost` holds one surface again, and `IsPrimary` splits into a role:
  a **workspace window** (full chrome - title bar and status bar) or a **panel window** (a tear-off).
- `WindowManager._primaryWindowId` becomes a workspace -> window map; `GetPrimary()` becomes
  `WorkspaceWindow(workspaceId)`, created on a workspace's first activation.
- `UiRegionContribution` gains a `WorkspaceId`: routed to that workspace's window, `Guid.Empty` meaning every
  workspace window (the factory runs once per window). Conversation sends one per chat; app-wide
  contributors (`task-tracker`, `usage-limits`) keep sending `Guid.Empty` and are unchanged.
- Layout v2 keys by `(window, workspace)` already, which becomes 1:1; `WindowRole` gains `workspace`.

### First-launch checks for WP11, in this order

`state.yaml` was deleted before this landed, so the first launch starts from no saved layout at all - which is
the simplest case and the one to check first.

1. **One workspace, one window, one chat.** The window comes up with exactly one chat tab and no close cross on
   its title bar. Alt+F4 does nothing. `quit` in the palette still exits.
2. **A second workspace gets its own window** (press an unused F-key). The first workspace's window disappears
   as the second appears - never both at once - and the second has its own chat, not the first one's.
3. **Chrome follows the switch.** The branch strip, the agent cluster and the status bar are present in
   whichever window is on screen, and show that workspace's values. This is the moved-not-copied path; a WPF
   "element already has a logical parent" crash on the second switch would mean the removal half is missing.
4. **Switch back and forth twice, then quit and relaunch.** Each workspace comes back with its own panels, and
   **neither has gained a second chat tab** - that is the whole point of the package. Check `state.yaml`: one
   `layouts` entry per workspace, each holding that workspace's own panels and no other's.
5. **Tear a panel off** into its own window, switch workspace, switch back: it hides and returns with its
   workspace, and it is owned by that workspace's window rather than whichever was active when it was created.

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
