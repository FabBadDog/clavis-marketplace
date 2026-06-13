namespace FabioSoft.Clavis.Controls

open System
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls

/// A dark, square text button (the "Bind", "Apply", "Add" affordance) wearing the host's keyed ActionButton
/// style, so action buttons read the same everywhere. The content is any string; onClick runs on click.
[<ExcludeFromCodeCoverage>] // WPF control construction
[<RequireQualifiedAccess>]
module ActionButton =

    let create (content: string) (onClick: Action) : Button =
        let button = Button(Content = content)
        button.SetResourceReference(FrameworkElement.StyleProperty, "ActionButton")
        button.Click.Add(fun _ -> onClick.Invoke())
        button
