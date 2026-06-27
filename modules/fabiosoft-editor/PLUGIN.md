---
name: fabiosoft-editor
assemblyName: FabioSoft.Editor
version: 1.0.1
apiVersion: 1.0.0
description: AvalonEdit-backed code editor control (Default-ALC, DP-heavy).
produces: [ FabioSoft.Editor.dll ]
privateAssemblies: [ ICSharpCode.AvalonEdit.dll ]
language: fsharp
rootNamespace: FabioSoft.Editor
useWpf: true
sources:
  - CodeEditor.fs
packages:
  - { name: AvalonEdit, version: 6.3.1.120 }
---
