namespace FabioSoft.Clavis.Rendering

open System
open System.Diagnostics.CodeAnalysis
open System.Globalization
open System.Windows
open System.Windows.Data
open System.Windows.Media

/// Binds an icon-name string to a geometric StatIcon element for XAML consumers (the turn-stats column).
/// The brush is resolved at convert time from the host resource named by BrushKey - theme via resource key,
/// never baked - and the mark is drawn at Size. One-way only: there is no element-to-name conversion.
[<ExcludeFromCodeCoverage>] // WPF value converter; the geometry it produces is exercised through StatIcon
type StatIconConverter() =

    static let fallback = SolidColorBrush(Color.FromRgb(0xB0uy, 0xB0uy, 0xBAuy))

    do fallback.Freeze()

    member val Size = 11.0 with get, set

    member val BrushKey = "TextDimBrush" with get, set

    interface IValueConverter with

        member this.Convert(value: obj, _targetType: Type, _parameter: obj, _culture: CultureInfo) =
            let name =
                match value with
                | :? string as text -> text
                | _ -> ""

            let brush =
                match Application.Current with
                | null -> fallback :> Brush
                | app ->
                    match app.TryFindResource this.BrushKey with
                    | :? Brush as resolved -> resolved
                    | _ -> fallback :> Brush

            StatIcon.Create(name, this.Size, brush) :> obj

        member _.ConvertBack(_value: obj, _targetType: Type, _parameter: obj, _culture: CultureInfo) =
            raise (NotSupportedException("StatIconConverter is one-way: an icon element has no source name."))
