# Code map (Clavis marketplace)

A one-screen index of every item in this marketplace: the modules (Default-ALC shared assemblies) and
the plugins (collectible, compiled on launch). For the host and the core libraries see
`docs/CODEMAP.md` in the **clavis** core repo; for the whole cross-repo dependency graph see that repo's
`docs/DEPENDENCY-MAP.md`. Each item also has its own `PLUGIN.md` (the catalog frontmatter + notes); start
there for detail. For who-publishes/who-subscribes a bus message, see `docs/MESSAGE-MAP.md`.

Every item is pure **source**, compiled on launch by Clavis core when stale. Plugins load into a
collectible `AssemblyLoadContext` (reloadable); modules load into the Default ALC (not unloadable).

## Modules - contract assemblies (`modules/`)

Cross-plugin message groups. Their types must have one identity across every plugin ALC, so they live in
the Default ALC. They are pure message DTOs (no dependencies).

| Item (`assemblyName`) | Carries |
|---|---|
| `session-contracts` (`FabioSoft.Contracts.Session`) | Session commands + the provider-neutral `AgentStreamEvent` family + conversation/summary messages |
| `host-contracts` (`...Contracts.Host`) | UI regions, user input, permission flow, summon/toggle |
| `workspace-contracts` (`...Contracts.Workspace`) | Multi-window + dockable-panel protocol (panel kinds, open/restore/close, per-instance state, windows) |
| `keymap-contracts` (`...Contracts.Keymap`) | Keybindings + command catalog (`KeymapChanged`, `CommandsAvailable`, `RunCommand`, panel commands) |
| `resource-contracts` (`...Contracts.Resource`) | `IResource` + load/write subsystem (`LoadResource`, `RegisterScheme`, ...) |
| `services-contracts` (`...Contracts.Services`) | Per-plugin config (`GetConfig`/`SaveConfig`/`ConfigChanged`) + disposable runtime state |
| `editor-contracts` (`...Contracts.Editor`) | Code-editor panel messages (`OpenFileInEditor`, `EditorStateChanged`, `EditorClosed`) |
| `marketplace-contracts` (`...Contracts.Marketplace`) | Marketplace lifecycle broadcasts (`MarketplaceProgress`/`MarketplaceCompleted`/`MarketplaceFailed`/`RestartRequired`) |

## Modules - control + bridge libraries (`modules/`)

Non-unloadable because they are `DependencyProperty`-heavy WPF (which roots types) or shared standalone
libraries.

| Item (`assemblyName`) | Purpose | Key types |
|---|---|---|
| `clavis-rendering` (`FabioSoft.Clavis.Rendering`) | Shared WPF controls + theme primitives + motion. | `MarkdownPresenter`, `DockingSurface`/`DockingModel`, `Motion`, `SegmentedSelector`, `SelectorWindow`/`SelectorOptions`/`SelectorHistory`, `PlaceholderStrip`, `PlaceholderEditor` |
| `clavis-controls` (`FabioSoft.Clavis.Controls`) | Shared form/input/layout widgets themed via host resource keys. | `Inputs`, `ActionButton`, `LabeledField`, `SectionHeader`, `StatusDot`, `TreeBrowser` |
| `fabiosoft-editor` (`FabioSoft.Editor`) | Application-neutral AvalonEdit wrapper. | `CodeEditor`, `CodeEditorSyntax` |
| `fabiosoft-claude` (`FabioSoft.Claude`) | Standalone `claude.exe` bridge: spawns the process, parses NDJSON stream-json. Zero Clavis deps. | `Session`, `StreamEvent`, `ClaudeCommand` |

## Plugins (`plugins/`, compiled on launch)

Each is an `IPlugin<TConfig>` with its own `PLUGIN.md`. UI plugins build with `UseWPF` and contribute into
WpfHost's named regions / docking surface.

| Plugin (`pluginId`) | Purpose |
|---|---|
| `WpfHost` | Owns the application windows + docking surface; persists/restores the workspace. |
| `Conversation` | The elm/flux chat: state, pure update, view templates, permission/hook resolution. |
| `ClaudeBridge` | Wraps `FabioSoft.Claude` sessions; maps stream events onto bus messages. |
| `AgentGateway` | Exposes Clavis actions to the agent as MCP tools (snapshot, panels, prompt, ...). |
| `Configuration` | The sectioned config + state YAML stores under `~/.clavis`. |
| `PanelRegistry` | Catalogs panel kinds; routes `OpenPanel`/`RestorePanel` into ready instances. |
| `CommandPalette` | Command palette + keybinding/command catalog UI (a `SelectorWindow` client). |
| `Selection` | Model/effort/mode/panel selection popups + the agent-driven ask-the-user selection. |
| `KeyMap` / `KeymapPanel` | Keybinding store / keybinding editor panel. |
| `EventsPanel` | Raw bus-activity firehose with keyboard filters. |
| `GitLogPanel` | Live `git log` panel. |
| `MarkdownPanel` | User-authored markdown panels (live placeholders) + a manager to create/edit them. |
| `CodeEditorPanel` | File browser + `FabioSoft.Editor` code editor panel. |
| `UsageLimits` | Usage-limit plane + runway slide-in. |
| `Settings` | Settings UI. |
| `PluginManager` | Lists/loads/unloads plugins. |
| `MarketplacePlugin` | Self-modification: compile/test/reload/commit marketplace items (uses the core marketplace libraries). |
| `ResourceBroker` + `FileSystem` + `Http` | Resource scheme registry and `file://` / `http(s)://` handlers. |
