namespace FabioSoft.Clavis.Controls

open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls

/// A form field: a small dim caption stacked above its control, fixed to a column width so a row of fields
/// lines up. The control is any element (a text input, a dropdown, a button); the caption is plain text.
[<ExcludeFromCodeCoverage>] // WPF control construction
[<RequireQualifiedAccess>]
module LabeledField =

    [<Literal>]
    let private captionSize = 9.0

    let create (caption: string) (control: FrameworkElement) (width: float) : StackPanel =
        let label = TextBlock(Text = caption, FontSize = captionSize, Margin = Thickness(0.0, 0.0, 0.0, 2.0))
        label.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont")
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush")

        control.Width <- width

        let stack = StackPanel(Margin = Thickness(0.0, 0.0, 10.0, 6.0))
        stack.Children.Add(label) |> ignore
        stack.Children.Add(control) |> ignore
        stack
