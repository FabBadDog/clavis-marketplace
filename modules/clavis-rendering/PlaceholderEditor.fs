namespace FabioSoft.Clavis.Rendering

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Controls.Primitives
open System.Windows.Input
open System.Windows.Media
open FabioSoft.Contracts.Placeholders
open FabioSoft.Clavis.Placeholders

/// One completion suggestion row shown in the popup list; ToString drives its display.
[<ExcludeFromCodeCoverage>]
type internal CompletionRow(item: CompletionItem, replaceStart: int) =
    member _.Item = item
    member _.ReplaceStart = replaceStart

    override _.ToString() =
        if String.IsNullOrEmpty item.Detail then item.Label
        else $"{item.Label}   -   {item.Detail}"

/// A multiline text editor with placeholder IntelliSense: typing `{` opens a completion popup fed by the
/// current provider descriptors (supplied by the `descriptors` accessor), offering namespaces, components,
/// value keys, and formats. This is the status-line editor's completion brain lifted into a reusable
/// control, so any plugin authoring a placeholder template gets the same autocomplete. Lives in the
/// never-unloaded Default ALC, so it may use WPF freely.
[<ExcludeFromCodeCoverage>] // WPF construction
type PlaceholderEditor(descriptors: Func<IReadOnlyList<PlaceholderDescriptor>>) =

    let editor =
        TextBox(
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            BorderThickness = Thickness(0.0),
            Padding = Thickness(10.0, 8.0, 10.0, 8.0))

    // Programmatic Text writes (loading a definition) must not spring the popup open, so the TextChanged
    // handler is gated on this.
    let mutable suppress = false

    let resolveBrush (key: string) (fallback: Brush) =
        match editor.TryFindResource key with
        | :? Brush as brush -> brush
        | _ -> fallback

    let resolveFontFamily (key: string) =
        match editor.TryFindResource key with
        | :? FontFamily as family -> family
        | _ -> FontFamily("Segoe UI")

    let resolveDouble (key: string) (fallback: float) =
        match editor.TryFindResource key with
        | :? float as value -> value
        | _ -> fallback

    // The popup's child is outside the window's visual tree, where DynamicResource references fall back to
    // defaults; resolve the theme tokens off the editor (which still reaches Application.Resources) and
    // apply them as literals so the list reads at the design's body role.
    let list =
        ListBox(
            MaxHeight = 240.0,
            BorderThickness = Thickness(0.0),
            FontFamily = resolveFontFamily "AgentFont",
            FontSize = resolveDouble "BodyFontSize" 14.0,
            Background = resolveBrush "BlackBrush" Brushes.Black,
            Foreground = resolveBrush "TextBrush" Brushes.White)

    let popup =
        Popup(
            PlacementTarget = editor,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            Child =
                Border(
                    Child = list,
                    MinWidth = 320.0,
                    BorderThickness = Thickness(1.0),
                    Background = resolveBrush "BlackBrush" Brushes.Black,
                    BorderBrush = resolveBrush "FrameBrush" Brushes.Gray))

    let close () = popup.IsOpen <- false

    let show () =
        let result =
            PlaceholderCompletion.Complete(
                editor.Text, editor.CaretIndex, descriptors.Invoke(), PlaceholderComponents.All, PlaceholderFormats.Known)

        if result.Items.Count = 0 then
            close ()
        else
            list.Items.Clear()
            for item in result.Items do
                list.Items.Add(CompletionRow(item, result.ReplaceStart)) |> ignore
            list.SelectedIndex <- 0
            popup.IsOpen <- true

    let accept () =
        match list.SelectedItem with
        | :? CompletionRow as row ->
            let caret = editor.CaretIndex
            let text = editor.Text
            close ()
            // Splice InsertText over [ReplaceStart, caret). Plain assignment; the TextChanged handler
            // re-shows for chained completion (accepting "agent." immediately offers its keys).
            editor.Text <- text[.. row.ReplaceStart - 1] + row.Item.InsertText + text[caret ..]
            editor.CaretIndex <- row.ReplaceStart + row.Item.InsertText.Length
            show ()
        | _ -> ()

    do
        editor.PreviewKeyDown.Add(fun args ->
            if popup.IsOpen then
                match args.Key with
                | Key.Down ->
                    list.SelectedIndex <- min (list.SelectedIndex + 1) (list.Items.Count - 1)
                    args.Handled <- true
                | Key.Up ->
                    list.SelectedIndex <- max (list.SelectedIndex - 1) 0
                    args.Handled <- true
                | Key.Enter
                | Key.Tab ->
                    accept ()
                    args.Handled <- true
                | Key.Escape ->
                    close ()
                    args.Handled <- true
                | _ -> ())

        list.MouseDoubleClick.Add(fun _ -> accept ())
        editor.GotKeyboardFocus.Add(fun _ -> show ())
        editor.TextChanged.Add(fun _ -> if not suppress then show ())
        editor.SelectionChanged.Add(fun _ -> if popup.IsOpen then show ())

    /// The editor element to place in a layout.
    member _.Element: FrameworkElement = editor

    /// The current template text. Setting it does not open the completion popup.
    member _.Text
        with get () = editor.Text
        and set (value: string) =
            suppress <- true
            editor.Text <- (if isNull value then "" else value)
            suppress <- false
