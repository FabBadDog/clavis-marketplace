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

    let private styled (styleKey: string) (content: string) (onClick: Action) : Button =
        let button = Button(Content = content)
        button.SetResourceReference(FrameworkElement.StyleProperty, styleKey)
        button.Click.Add(fun _ -> onClick.Invoke())
        button

    /// Neutral button: line border, body text, brightens on hover. The default affordance.
    let create (content: string) (onClick: Action) : Button = styled "ActionButton" content onClick

    /// Primary button: clavis border + text, faint clavis fill on hover. The affirmative action
    /// (Install, Apply, Confirm).
    let primary (content: string) (onClick: Action) : Button = styled "ActionButtonPrimary" content onClick

    /// Danger button: neutral at rest, reveals red on hover. Destructive actions (Uninstall, Delete).
    let danger (content: string) (onClick: Action) : Button = styled "ActionButtonDanger" content onClick
