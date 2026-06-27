---
name: fabiosoft-claude
assemblyName: FabioSoft.Claude
version: 2.0.0
apiVersion: 1.0.0
description: Standalone Claude Code bridge library.
produces: [ FabioSoft.Claude.dll ]
privateAssemblies: [ System.Reactive.dll, FsToolkit.ErrorHandling.dll ]
language: fsharp
rootNamespace: FabioSoft.Claude
sources:
  - Types.fs
  - NdjsonParser.fs
  - UsageApi.fs
  - ClaudeCommand.fs
  - PermissionRules.fs
  - HookCatalog.fs
  - Session.fs
packages:
  - { name: FsToolkit.ErrorHandling, version: 5.2.0 }
  - { name: System.Reactive, version: 6.1.0 }
---
