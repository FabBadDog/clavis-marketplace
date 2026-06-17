namespace FabioSoft.Clavis.Rendering

open System
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Data
open System.Windows.Media

/// Resolves a theme resource key (a string carried by a BadgeViewModel) to its Brush from the application
/// resources, so the badge template can bind a colour by key. An empty or unknown key yields UnsetValue,
/// so the label falls back to the Badge.Label style's default colour.
[<ExcludeFromCodeCoverage>] // WPF value converter over the live application resources
type KeyToBrushConverter() =

    interface IValueConverter with

        member _.Convert(value, _targetType, _parameter, _culture) =
            match value with
            | :? string as key when not (String.IsNullOrWhiteSpace key) ->
                match Application.Current.TryFindResource key with
                | :? Brush as brush -> box brush
                | _ -> box DependencyProperty.UnsetValue
            | _ -> box DependencyProperty.UnsetValue

        member _.ConvertBack(_value, _targetType, _parameter, _culture) =
            box DependencyProperty.UnsetValue
