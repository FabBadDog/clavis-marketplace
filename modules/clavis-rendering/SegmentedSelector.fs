namespace FabioSoft.Clavis.Rendering

open System
open System.Collections.Generic
open System.ComponentModel
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Data
open System.Windows.Input
open System.Windows.Media
open System.Windows.Media.Animation

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

/// The view for a SegmentedSelectorModel: a horizontal row of option labels with one shared clavis underline
/// that slides and resizes to the selected option (rather than a per-option underline snapping on and off).
/// Reads its model from DataContext, so it drops into XAML (the permission prompt binds DataContext to its
/// selector) or code (the events panel sets DataContext directly). Lives in the never-unloaded Default ALC,
/// so a custom control here roots no plugin types.
[<ExcludeFromCodeCoverage>] // WPF control construction
type SegmentedSelector() as this =
    inherit ContentControl()

    let options = ResizeArray<FrameworkElement>()
    let mutable rootGrid: Grid = null
    let mutable underline: Border = null
    let mutable underlineShift: TranslateTransform = null
    let mutable currentModel: SegmentedSelectorModel option = None
    let mutable subscription: IDisposable = null

    // Slide the single shared underline to the selected option, growing/shrinking its width to match - so
    // navigating the choices animates one line rather than snapping a per-option underline on and off. The
    // first show jumps (there is nothing to slide from); later moves animate. It needs a laid-out row, so it
    // no-ops until the control is loaded and the target has a measured width; the Loaded handler positions
    // the initial selection.
    let positionUnderline (index: int) (animate: bool) =
        if isNull underline then
            ()
        elif index < 0 || index >= options.Count then
            underline.Visibility <- Visibility.Collapsed
        elif this.IsLoaded && not (isNull rootGrid) then
            let target = options[index]
            if target.ActualWidth > 0.0 then
                let x = target.TransformToAncestor(rootGrid).Transform(Point(0.0, 0.0)).X
                let width = target.ActualWidth
                let firstShow = underline.Visibility <> Visibility.Visible
                underline.Visibility <- Visibility.Visible
                if animate && not firstShow then
                    underlineShift.BeginAnimation(
                        TranslateTransform.XProperty, DoubleAnimation(x, Motion.Standard, EasingFunction = Motion.easeOut()))
                    underline.BeginAnimation(
                        FrameworkElement.WidthProperty, DoubleAnimation(width, Motion.Standard, EasingFunction = Motion.easeOut()))
                else
                    underlineShift.X <- x
                    underline.Width <- width

    let buildOption (model: SegmentedSelectorModel) (index: int) (item: SegmentItem) =
        let label =
            TextBlock(
                Text = item.Label,
                FontSize = 10.0,
                FontWeight = FontWeights.Medium,
                Margin = Thickness(0.0, 0.0, 14.0, 5.0),
                Cursor = Cursors.Hand)
        label.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont")
        let foregroundKey = if String.IsNullOrEmpty item.ForegroundKey then "TextBrush" else item.ForegroundKey
        label.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey)
        label.MouseLeftButtonUp.Add(fun args ->
            model.Choose(index)
            args.Handled <- true)
        label :> FrameworkElement

    let build (model: SegmentedSelectorModel) =
        options.Clear()
        let row = StackPanel(Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom)
        model.Items
        |> Seq.iteri (fun index item ->
            let option = buildOption model index item
            row.Children.Add option |> ignore
            options.Add option)
        underlineShift <- TranslateTransform()
        underline <-
            Border(
                Height = 2.0,
                Width = 0.0,
                Background = Colors.clavis,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Visibility = Visibility.Collapsed,
                RenderTransform = underlineShift)
        let grid = Grid()
        grid.Children.Add row |> ignore
        grid.Children.Add underline |> ignore
        rootGrid <- grid
        grid :> obj

    let subscribe () =
        match currentModel with
        | Some model when isNull subscription ->
            subscription <-
                model.SelectionChanged
                |> Observable.subscribe (fun index ->
                    this.Dispatcher.Invoke(fun () -> positionUnderline index true))
        | _ -> ()

    let unsubscribe () =
        if not (isNull subscription) then
            subscription.Dispose()
            subscription <- null

    do
        this.Loaded.Add(fun _ ->
            subscribe ()
            match currentModel with
            | Some model -> positionUnderline model.SelectedIndex false
            | None -> ())

        this.Unloaded.Add(fun _ -> unsubscribe ())

        this.DataContextChanged.Add(fun args ->
            unsubscribe ()
            match args.NewValue with
            | :? SegmentedSelectorModel as model ->
                currentModel <- Some model
                this.Content <- build model
                if this.IsLoaded then
                    subscribe ()
                    positionUnderline model.SelectedIndex false
            | _ ->
                currentModel <- None
                this.Content <- null)
