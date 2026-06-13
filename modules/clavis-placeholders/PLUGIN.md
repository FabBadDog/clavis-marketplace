---
name: clavis-placeholders
assemblyName: FabioSoft.Clavis.Placeholders
version: 1.0.0
apiVersion: 1.0.0
description: Namespaced placeholder template engine - parse, resolve, and IntelliSense - shared by the status line and markdown panels.
produces: [ FabioSoft.Clavis.Placeholders.dll ]
dependencies:
  - { name: placeholders-contracts, version: 1 }
---

# clavis-placeholders

The pure template engine behind the configurable status line, the contextual window top bar, and the
markdown panels. No bus, no WPF - it parses a template, resolves it against a value snapshot, and computes
IntelliSense suggestions. The WPF layer (`clavis-rendering`) turns the resolved component segments into
controls.

## Grammar

Inside `{...}`: `head[:tail]`, where `head` is `name` or `name(arg)`. If `name` is a registered component
(`bar`, `badge`, `limitPlane`, `microstat`) the token is a component and `tail` is `value[:format]`;
otherwise `head` is a value key and `tail` is its format. Splitting is on the FIRST `:` so .NET format
strings (`HH:mm:ss`) survive. Components and formats compose: `{badge:time.now:HH:mm}` is a badge holding
`time.now` formatted `HH:mm`. Unknown value tokens render verbatim.

- `{git.branch}` - value
- `{agent.name:uppercase}` - value + named transform
- `{time.now:HH:mm}` - value + .NET format (applied when the value parses as a date/number)
- `{bar:agent.contextPercent}` - component + value
- `{microstat(arrow-up):turn.runtime}` - component(arg) + value

## API

- `PlaceholderEngine.Parse / Resolve / ResolveToText` - parsing and resolution.
- `PlaceholderFormats.Apply` / `.Known` - format/transform application and the list for IntelliSense.
- `PlaceholderComponents.All` - the built-in component names.
- `PlaceholderCompletion.Complete` - IntelliSense suggestions at a caret, over the aggregated provider
  descriptors (`PlaceholderDescriptor` from `clavis-contracts-placeholders`).
