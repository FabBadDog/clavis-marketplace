namespace FabioSoft.Clavis.Rendering

open System
open System.Collections.Generic
open System.ComponentModel
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Data
open System.Windows.Input

/// One option in a SegmentedSelector: a label shown in the brush named by ForegroundKey (a theme resource
/// key; empty falls back to the default text brush), underlined while selected. IsSelected is driven by
/// the owning model and notifies so the underline tracks it.
type SegmentItem(label: string, foregroundKey: string) as this =
    let propertyChanged = Event<PropertyChangedEventHandler, PropertyChangedEventArgs>()
    let mutable isSelected = false

    member _.Label = label
    member _.ForegroundKey = foregroundKey

    member _.IsSelected
        with get () = isSelected
        and set value =
            if value <> isSelected then
                isSelected <- value
                propertyChanged.Trigger(this, PropertyChangedEventArgs("IsSelected"))

    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member _.PropertyChanged = propertyChanged.Publish

/// A single-select horizontal selector: a row of labelled options, the selected one underlined in clavis,
/// navigated with MoveSelection (the host binds Left/Right to it) and chosen by click. Two consumers share
/// it: the events-panel severity filter reacts live to SelectionChanged, while the permission prompt drives
/// SelectedIndex from the conversation state (for the highlight) and acts on Committed (a deliberate click).
/// Pure - holds no WPF, fully unit-testable.
type SegmentedSelectorModel(items: IReadOnlyList<SegmentItem>) as this =
    let propertyChanged = Event<PropertyChangedEventHandler, PropertyChangedEventArgs>()
    let selectionChanged = Event<EventHandler<int>, int>()
    let committed = Event<EventHandler<int>, int>()
    let mutable selectedIndex = -1

    let clamp index =
        if items.Count = 0 then
            -1
        else
            max 0 (min (items.Count - 1) index)

    let applyHighlight () =
        for i in 0 .. items.Count - 1 do
            items[i].IsSelected <- (i = selectedIndex)

    member _.Items = items

    member _.SelectedIndex
        with get () = selectedIndex
        and set value =
            let next = clamp value
            if next <> selectedIndex then
                selectedIndex <- next
                applyHighlight ()
                propertyChanged.Trigger(this, PropertyChangedEventArgs("SelectedIndex"))
                selectionChanged.Trigger(this, next)

    /// Move the selection by delta, clamped to the option range (Left/Right navigation).
    member this.MoveSelection(delta: int) =
        this.SelectedIndex <- selectedIndex + delta

    /// A user click on an option: select it (raising SelectionChanged when it moves) and signal a
    /// deliberate choice via Committed even when the index did not change.
    member this.Choose(index: int) =
        this.SelectedIndex <- index
        committed.Trigger(this, clamp index)

    /// Fired whenever the selected index changes, from any source. Live consumers (the severity filter)
    /// apply the new selection immediately.
    [<CLIEvent>]
    member _.SelectionChanged = selectionChanged.Publish

    /// Fired only on a deliberate click. Consumers that separate navigation from commit (the permission
    /// prompt: Left/Right navigates, click/Enter commits) act on this.
    [<CLIEvent>]
    member _.Committed = committed.Publish

    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member _.PropertyChanged = propertyChanged.Publish

/// The view for a SegmentedSelectorModel: a horizontal row of options, each a label over a clavis underline
/// shown while selected. Reads its model from DataContext, so it drops into XAML (the permission prompt
/// binds DataContext to its selector) or code (the events panel sets DataContext directly). Lives in the
/// never-unloaded Default ALC, so a custom control here roots no plugin types.
[<ExcludeFromCodeCoverage>] // WPF control construction
type SegmentedSelector() as this =
    inherit ContentControl()

    let boolToVisibility = BooleanToVisibilityConverter()

    let buildOption (model: SegmentedSelectorModel) (index: int) (item: SegmentItem) =
        let label =
            TextBlock(
                Text = item.Label,
                FontSize = 10.0,
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center)
        label.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont")
        let foregroundKey = if String.IsNullOrEmpty item.ForegroundKey then "TextBrush" else item.ForegroundKey
        label.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey)

        let underline = Border(Height = 2.0, Margin = Thickness(0.0, 3.0, 0.0, 0.0), Background = Colors.clavis)
        underline.SetBinding(UIElement.VisibilityProperty, Binding("IsSelected", Source = item, Converter = boolToVisibility))
        |> ignore

        let option = StackPanel(Margin = Thickness(0.0, 0.0, 14.0, 0.0), Cursor = Cursors.Hand)
        option.Children.Add(label) |> ignore
        option.Children.Add(underline) |> ignore
        option.MouseLeftButtonUp.Add(fun args ->
            model.Choose(index)
            args.Handled <- true)
        option

    let build (model: SegmentedSelectorModel) =
        let row = StackPanel(Orientation = Orientation.Horizontal)
        model.Items |> Seq.iteri (fun index item -> row.Children.Add(buildOption model index item) |> ignore)
        row :> obj

    do
        this.DataContextChanged.Add(fun args ->
            this.Content <-
                match args.NewValue with
                | :? SegmentedSelectorModel as model -> build model
                | _ -> null)
