---
name: clavis-rendering
assemblyName: FabioSoft.Clavis.Rendering
version: 2.4.0
apiVersion: 1.0.0
description: Shared WPF rendering controls: MarkdownPresenter, DockingSurface, the SelectorWindow list-selection popup, the PlaceholderStrip status line and the geometric StatIcon set (with its XAML StatIconConverter).
produces: [ FabioSoft.Clavis.Rendering.dll ]
privateAssemblies: [ Markdig.dll ]
dependencies:
  - { name: clavis-placeholders, version: 1 }
language: fsharp
rootNamespace: FabioSoft.Clavis.Rendering
useWpf: true
sources:
  - Theme.fs
  - BadgeViewModel.fs
  - KeyToBrushConverter.fs
  - FocusOverlay.fs
  - Motion.fs
  - CloseButton.fs
  - SegmentedSelector.fs
  - Selector.fs
  - LimitWindow.fs
  - LimitsPlane.fs
  - StatIcon.fs
  - StatIconConverter.fs
  - PlaceholderStrip.fs
  - ResponsiveZoneBar.fs
  - CopyMenu.fs
  - TextReveal.fs
  - MarkdownPresenter.fs
  - DockingSurface.fs
  - KeyGestureReader.fs
  - ShortcutHelpOverlay.fs
  - SlideInHost.fs
packages:
  - { name: Markdig, version: 1.1.2 }
---
