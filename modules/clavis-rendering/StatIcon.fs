namespace FabioSoft.Clavis.Rendering

open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Windows.Shapes

/// Geometric vector icons for the status line and the turn-stats column, replacing the former Unicode glyph
/// vocabulary so a mark renders identically regardless of the installed fonts. Each icon is authored on a
/// 16x16 grid and scaled uniformly to the requested size; an unknown name renders a neutral dot. Shared by
/// PlaceholderStrip and (via the C# conversation plugin) the turn stats, so both speak one icon language.
[<ExcludeFromCodeCoverage>] // pure WPF geometry construction
type StatIcon =

    // The full 16x16-grid circle (radius 6, centre 8,8), reused by the clock, queued and cost marks.
    static member private Ring = "M2,8 A6,6 0 1 0 14,8 A6,6 0 1 0 2,8 Z"

    static member private Dot = "M6,8 A2,2 0 1 0 10,8 A2,2 0 1 0 6,8 Z"

    static member private GridSize = 16.0

    static member private StrokeWidth = 1.5

    // (path data on the 16x16 grid, isFilled). Stroked marks read as line drawings; filled ones as solids.
    static member private Shape(name: string) =
        match name with
        | "clock" | "time" -> Some(StatIcon.Ring + " M8,8 L8,4.5 M8,8 L10.5,9.5", false)
        | "queued" -> Some(StatIcon.Ring, false)
        | "tokens" -> Some("M8,2.5 L8,13.5 M8,2.5 L5.5,5 M8,2.5 L10.5,5 M8,13.5 L5.5,11 M8,13.5 L10.5,11", false)
        | "up" | "arrow-up" -> Some("M8,3 L8,13 M8,3 L5,6.5 M8,3 L11,6.5", false)
        | "down" | "arrow-down" -> Some("M8,3 L8,13 M8,13 L5,9.5 M8,13 L11,9.5", false)
        | "cost" -> Some(StatIcon.Ring + " M8,3.5 L8,12.5", false)
        | "tools" -> Some("M8,2 L14,8 L8,14 L2,8 Z", false)
        | "context" | "ctx" -> Some("M2.5,3.5 L13.5,3.5 L13.5,12.5 L2.5,12.5 Z M2.5,6.5 L13.5,6.5", false)
        | "retries" | "retry" -> Some("M11,3.8 A5,5 0 1 1 4.6,4.4 M11,3.8 L9.1,3 M11,3.8 L11.2,5.8", false)
        | _ -> None

    /// A vector icon for `name`, scaled to `size` and painted in `brush`. Unknown names render a neutral dot
    /// so a mistyped template token degrades to a quiet mark rather than throwing.
    static member Create(name: string, size: float, brush: Brush) : FrameworkElement =
        let data, filled =
            match StatIcon.Shape name with
            | Some(geometry, isFilled) -> geometry, isFilled
            | None -> StatIcon.Dot, true

        let path = Path(Data = Geometry.Parse data, SnapsToDevicePixels = true)

        if filled then
            path.Fill <- brush
        else
            path.Stroke <- brush
            path.StrokeThickness <- StatIcon.StrokeWidth
            path.StrokeStartLineCap <- PenLineCap.Round
            path.StrokeEndLineCap <- PenLineCap.Round
            path.StrokeLineJoin <- PenLineJoin.Round

        let canvas = Canvas(Width = StatIcon.GridSize, Height = StatIcon.GridSize, Background = Brushes.Transparent)
        canvas.Children.Add path |> ignore
        Viewbox(Width = size, Height = size, Stretch = Stretch.Uniform, Child = canvas) :> FrameworkElement
