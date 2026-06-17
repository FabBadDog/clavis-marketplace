namespace FabioSoft.Clavis.Rendering

/// The text and accent of a single badge, bound by the shared BadgeTemplate. AccentKey is a theme resource
/// key (e.g. "LevelErrorBrush") that KeyToBrushConverter resolves to a Brush, so a view-model carries its
/// badge colour as a theme-agnostic key. Immutable: a consumer creates a fresh instance whenever the badge
/// changes (the row view-model is the INotifyPropertyChanged source), so the value object needs no change
/// notification of its own. A class, not a record, so C# and F# construct it identically.
type BadgeViewModel(text: string, accentKey: string) =

    member _.Text = text
    member _.AccentKey = accentKey
