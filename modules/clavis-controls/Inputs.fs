namespace FabioSoft.Clavis.Controls

open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls

/// The shared dark, square form inputs: a single-line text box and a dropdown. Both wear the host's keyed
/// styles (InputTextBox / InputComboBox in WpfHost's Styles.xaml) so every binding editor, filter, and
/// settings field looks the same; a consumer fills in the content and reads the value back. (Named `Inputs`,
/// not `Input`, to avoid colliding with the `System.Windows.Input` namespace in C# consumers.)
[<ExcludeFromCodeCoverage>] // WPF control construction
[<RequireQualifiedAccess>]
module Inputs =

    [<Literal>]
    let private fieldHeight = 24.0

    /// A single-line text input. The placeholder shows as a tooltip (the dark template has no watermark).
    let text (placeholder: string) : TextBox =
        let box = TextBox(Height = fieldHeight, ToolTip = placeholder)
        box.SetResourceReference(FrameworkElement.StyleProperty, "InputTextBox")
        box

    /// A dropdown. The caller sets ItemsSource / SelectedItem; the dark popup styling comes from the theme.
    let combo () : ComboBox =
        let box = ComboBox(Height = fieldHeight)
        box.SetResourceReference(FrameworkElement.StyleProperty, "InputComboBox")
        box

    /// A search / filter field: the shared input chrome but on the black canvas with mono text, per the
    /// design language's search-field treatment. Reuses InputTextBox's template (which binds Background /
    /// FontFamily / FontSize), overriding those for the search look. Focus recolours the line via the
    /// app-wide focus overlay. The placeholder shows as a tooltip.
    let search (placeholder: string) : TextBox =
        let box = TextBox(Height = fieldHeight, ToolTip = placeholder)
        box.SetResourceReference(FrameworkElement.StyleProperty, "InputTextBox")
        box.SetResourceReference(Control.BackgroundProperty, "BlackBrush")
        box.SetResourceReference(Control.FontFamilyProperty, "MonoFont")
        box.SetResourceReference(Control.FontSizeProperty, "MonoFontSize")
        box
