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
open System.Windows.Media.Effects

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

/// The view for a SegmentedSelectorModel: a wrapping row of option labels with a single clavis underline that
/// slides and grows/shrinks to the selected option - tracking it across wrapped lines (X and Y both animate) -
/// while the selected label lifts to the accent colour with a soft glow. Options flow onto further lines when
/// the host is too narrow, so none is clipped. Reads its model from DataContext, so it drops into XAML (the
/// permission prompt) or code (the events panel). Lives in the never-unloaded Default ALC, so it roots no
/// plugin types. Set AutoFocus in XAML to have it take keyboard focus when it loads (the permission prompt).
[<ExcludeFromCodeCoverage>] // WPF control construction
type SegmentedSelector() as this =
    inherit ContentControl()

    // Keys whose colour is "neutral" (a plain text tone), so a selected option lifts to the accent instead;
    // any other key (e.g. ErrorBrush on DENY) is a deliberate colour and is kept when selected.
    let neutralKeys = set [ ""; "TextBrush"; "TextDimBrush" ]
    let selectedGlowOpacity = 0.85

    let labels = ResizeArray<TextBlock>()
    let mutable rootGrid: Grid = null
    let mutable underline: Border = null
    let mutable underlineTransform: TranslateTransform = null
    let mutable currentModel: SegmentedSelectorModel option = None
    let mutable subscription: IDisposable = null
    let mutable autoFocus = false

    // Colors.clavis is a Brush; the glow (DropShadowEffect) needs its Color.
    let clavisColour =
        match Colors.clavis :> Brush with
        | :? SolidColorBrush as brush -> brush.Color
        | _ -> Color.FromRgb(0x4Fuy, 0xC3uy, 0xF7uy)

    let normalKey (item: SegmentItem) =
        if String.IsNullOrEmpty item.ForegroundKey then "TextBrush" else item.ForegroundKey

    let isNeutral (item: SegmentItem) =
        neutralKeys.Contains(if isNull item.ForegroundKey then "" else item.ForegroundKey)

    // Resolve a theme brush's colour for the glow tint, falling back to the clavis accent.
    let glowColour (key: string) =
        match this.TryFindResource(key) with
        | :? SolidColorBrush as brush -> brush.Color
        | _ -> clavisColour

    let fade (glow: DropShadowEffect) target =
        glow.BeginAnimation(
            DropShadowEffect.OpacityProperty, DoubleAnimation(target, Motion.Standard, EasingFunction = Motion.easeOut()))

    // Recolour every label for the current selection: the selected one lifts to the accent (unless it is
    // deliberately coloured) and its glow fades in; the rest return to their normal tone with the glow faded out.
    let applyHighlight (model: SegmentedSelectorModel) (selected: int) =
        labels |> Seq.iteri (fun index label ->
            let item = model.Items[index]
            let key = if index = selected && isNeutral item then "ClavisBrush" else normalKey item
            label.SetResourceReference(TextBlock.ForegroundProperty, key)
            match label.Effect with
            | :? DropShadowEffect as glow -> fade glow (if index = selected then selectedGlowOpacity else 0.0)
            | _ -> ())

    // Slide the single underline to the selected option, animating X, Y (so it follows onto wrapped lines) and
    // Width. It needs a laid-out row, so it no-ops until loaded with a measured target; SizeChanged reflows it.
    let positionUnderline (index: int) (animate: bool) =
        if isNull underline || isNull rootGrid then
            ()
        elif index < 0 || index >= labels.Count then
            underline.Visibility <- Visibility.Collapsed
        elif this.IsLoaded then
            let target = labels[index]
            if target.ActualWidth > 0.0 then
                let topLeft = target.TransformToAncestor(rootGrid).Transform(Point(0.0, 0.0))
                let x = topLeft.X
                let y = topLeft.Y + target.ActualHeight
                let width = target.ActualWidth
                let firstShow = underline.Visibility <> Visibility.Visible
                underline.Visibility <- Visibility.Visible
                if animate && not firstShow then
                    underlineTransform.BeginAnimation(
                        TranslateTransform.XProperty, DoubleAnimation(x, Motion.Standard, EasingFunction = Motion.easeOut()))
                    underlineTransform.BeginAnimation(
                        TranslateTransform.YProperty, DoubleAnimation(y, Motion.Standard, EasingFunction = Motion.easeOut()))
                    underline.BeginAnimation(
                        FrameworkElement.WidthProperty, DoubleAnimation(width, Motion.Standard, EasingFunction = Motion.easeOut()))
                else
                    underlineTransform.BeginAnimation(TranslateTransform.XProperty, null)
                    underlineTransform.BeginAnimation(TranslateTransform.YProperty, null)
                    underline.BeginAnimation(FrameworkElement.WidthProperty, null)
                    underlineTransform.X <- x
                    underlineTransform.Y <- y
                    underline.Width <- width

    let buildOption (index: int) (item: SegmentItem) =
        let label =
            TextBlock(
                Text = item.Label,
                FontSize = 10.0,
                FontWeight = FontWeights.Medium,
                Margin = Thickness(0.0, 0.0, 14.0, 6.0),
                Cursor = Cursors.Hand)
        label.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont")
        label.SetResourceReference(TextBlock.ForegroundProperty, normalKey item)
        let tint = if isNeutral item then clavisColour else glowColour (normalKey item)
        label.Effect <- DropShadowEffect(ShadowDepth = 0.0, BlurRadius = 12.0, Opacity = 0.0, Color = tint)
        label.MouseLeftButtonUp.Add(fun args ->
            currentModel |> Option.iter (fun model -> model.Choose index)
            args.Handled <- true)
        labels.Add label
        label :> UIElement

    let build (model: SegmentedSelectorModel) =
        labels.Clear()
        let row = WrapPanel(Orientation = Orientation.Horizontal)
        model.Items |> Seq.iteri (fun index item -> row.Children.Add(buildOption index item) |> ignore)
        underlineTransform <- TranslateTransform()
        underline <-
            Border(
                Height = 2.0,
                Width = 0.0,
                Background = Colors.clavis,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                RenderTransform = underlineTransform)
        let grid = Grid()
        grid.Children.Add row |> ignore
        grid.Children.Add underline |> ignore
        rootGrid <- grid
        grid :> obj

    let refresh () =
        currentModel |> Option.iter (fun model ->
            applyHighlight model model.SelectedIndex
            positionUnderline model.SelectedIndex false)

    let subscribe () =
        match currentModel with
        | Some model when isNull subscription ->
            subscription <-
                model.SelectionChanged
                |> Observable.subscribe (fun index ->
                    this.Dispatcher.Invoke(fun () ->
                        applyHighlight model index
                        positionUnderline index true))
        | _ -> ()

    let unsubscribe () =
        if not (isNull subscription) then
            subscription.Dispose()
            subscription <- null

    do
        this.Focusable <- true

        this.Loaded.Add(fun _ ->
            subscribe ()
            refresh ()
            if autoFocus then
                this.Focus() |> ignore)

        this.Unloaded.Add(fun _ -> unsubscribe ())

        this.SizeChanged.Add(fun _ ->
            currentModel |> Option.iter (fun model -> positionUnderline model.SelectedIndex false))

        this.DataContextChanged.Add(fun args ->
            unsubscribe ()
            match args.NewValue with
            | :? SegmentedSelectorModel as model ->
                currentModel <- Some model
                this.Content <- build model
                if this.IsLoaded then
                    subscribe ()
                    refresh ()
            | _ ->
                currentModel <- None
                this.Content <- null)

    /// When true, the control takes keyboard focus as it loads. The permission prompt sets this so the
    /// selection control is focused while a decision is pending; the events-panel filter leaves it false.
    member _.AutoFocus
        with get () = autoFocus
        and set value = autoFocus <- value
