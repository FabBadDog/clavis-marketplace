namespace FabioSoft.Clavis.Controls

open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Shapes

/// A small circular status indicator - a dirty marker, a commit bullet, an activity dot. Always an Ellipse
/// (a dot is a circle, never a square Border), filled from a theme colour key so callers stay themeable.
/// The caller positions it (margin, alignment) and toggles Visibility for state.
[<ExcludeFromCodeCoverage>] // WPF control construction
[<RequireQualifiedAccess>]
module StatusDot =

    let sized (colorKey: string) (size: float) : Ellipse =
        let dot = Ellipse(Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center)
        dot.SetResourceReference(Shape.FillProperty, colorKey)
        dot
