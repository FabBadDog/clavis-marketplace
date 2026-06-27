---
name: clavis-controls
assemblyName: FabioSoft.Clavis.Controls
version: 1.0.1
apiVersion: 1.0.0
description: Shared WPF form/input/layout widgets - inputs, buttons, labeled fields, section headers, metadata text, status dots, and a generic tree browser.
produces: [ FabioSoft.Clavis.Controls.dll ]
language: fsharp
rootNamespace: FabioSoft.Clavis.Controls
useWpf: true
sources:
  - Inputs.fs
  - ActionButton.fs
  - IconButton.fs
  - LabeledField.fs
  - SectionHeader.fs
  - MetadataText.fs
  - StatusDot.fs
  - TreeBrowser.fs
  - EmptyState.fs
---

# clavis-controls

## Purpose

The reusable WPF building blocks the plugins share, so every plugin's input box, dropdown, button,
labeled field, section header, metadata label, status dot, and tree view reads and behaves the same and
new plugins do not re-build them. A standalone Default-ALC module (like `clavis-rendering` and
`fabiosoft-editor`): reusable controls referenced across plugins cannot be a collectible plugin, because a
plugin's types are invisible to other plugins' load contexts.

Each widget is a small factory returning a plain WPF element. Look comes entirely from the host theme via
`SetResourceReference(prop, key)` against the keys WpfHost registers in `Theme/Styles.xaml`
(`InputTextBox`, `InputComboBox`, `ActionButton`, `SectionHeaderTextStyle`, `MetadataTextStyle`,
`MonoFont`, `ClavisBrush`, ...) - nothing is baked, so the host (or a future theme) re-skins everything by
changing those resources. Behaviour and content are plain CLR parameters (placeholder, caption, width,
click handler), so a consumer parameterizes a widget without restyling it.

## Components

- `Inputs.text placeholder` / `Inputs.combo ()` - dark square text input / dropdown (style keys
  `InputTextBox` / `InputComboBox`).
- `ActionButton.create content onClick` - dark square text button (style key `ActionButton`).
- `IconButton.create glyph onClick` - chrome-free glyph button (the generic non-close counterpart of
  `clavis-rendering`'s `CloseButton`, which stays the canonical close affordance).
- `LabeledField.create caption control width` - a caption stacked above a control.
- `SectionHeader.create text` - a dim, spaced group header (style key `SectionHeaderTextStyle`).
- `MetadataText.create text` / `MetadataText.accent text` / `MetadataText.sized text size` - monospace
  metadata labels (dim, or clavis-accented for identifiers like a commit hash).
- `StatusDot.sized colorKey size` - a circular status indicator (an `Ellipse`, never a square `Border`).
- `TreeBrowser.create roots onActivate` over `ITreeNode` - a generic lazy tree view; `TreeBrowserModel`
  holds the pure selection logic (`activatable`).

## Consuming it

A plugin adds `- { name: clavis-controls, version: ^1.0.0 }` to its `PLUGIN.md` `dependencies` (catalog +
build-order metadata; the runtime build already references every installed module), then
`using FabioSoft.Clavis.Controls;` (C#) / `open FabioSoft.Clavis.Controls` (F#) and calls the factory.

## Notes

- No `DependencyProperty` anywhere - INPC for binding sources, plain CLR properties for control state -
  so the assembly roots no types beyond the standard WPF registries it cannot avoid.
- Square corners only; a dot is a circle (`Ellipse`), never a rounded `Border`.
