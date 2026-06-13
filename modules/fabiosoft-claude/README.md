# FabioSoft.Claude -- Standalone Claude Code Bridge Library

A pure F# library wrapping Claude Code's CLI. Zero dependency on any CLAVIS project. Designed to be published and used independently.

## Contains

- Session management (start, send, resume, list, close)
- NDJSON stream parsing (claude.exe stdout to typed StreamEvent)
- Stream combinators (textOnly, toolUses, collectText, etc.)
- Metadata queries (environment, models -- zero API tokens consumed)
- CLI process adapter (via CliWrap)

## Does NOT contain

- UI code or WPF types
- CLAVIS domain types (Conversation, Exchange, AppState)
- Mutable application state
- Any reference to FabioSoft.Clavis.Core, FabioSoft.Clavis.Widgets, or FabioSoft.Clavis.Shell

## Dependencies

- `FSharp.Data` -- JSON parsing via JsonValue
