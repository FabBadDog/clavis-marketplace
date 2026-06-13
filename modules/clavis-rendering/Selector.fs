namespace FabioSoft.Clavis.Rendering

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Input
open System.Windows.Media
open System.Windows.Media.Animation

/// The outcome of a client's validation callback: either the input is accepted (with the canonical text
/// that gets recorded to history and handed to OnAccept), or it is rejected with a message the popup
/// shows while staying open.
[<Sealed>]
type SelectorValidation private (isValid: bool, canonicalText: string, message: string) =
    member _.IsValid = isValid
    member _.CanonicalText = canonicalText
    member _.Message = message

    static member Valid(canonicalText: string) = SelectorValidation(true, canonicalText, "")
    static member Invalid(message: string) = SelectorValidation(false, "", message)

/// Configuration of a SelectorWindow. The client owns content and meaning: it supplies the suggestion
/// provider and the item DataTemplate (any row layout it wants), and reacts to the accepted choice. The
/// window owns presentation and interaction: search-as-you-type filtering, keyboard navigation,
/// validation display, optional free-text editing with input history, and the open/close animations.
[<Sealed>]
type SelectorOptions() =
    /// Popup width in DIPs.
    member val Width = 600.0 with get, set

    /// Optional one-line heading rendered above the input (e.g. the question an agent asked). Empty hides it.
    member val Prompt = "" with get, set

    /// When true the typed text is a value of its own: it may deviate from the list (e.g. to add
    /// arguments) and input history (Up at the top of the list) is available. When false only list items
    /// can be accepted and typing only filters.
    member val FreeText = false with get, set

    /// The filtered items for the current input. Items are opaque to the window; the ItemTemplate renders them.
    member val GetSuggestions: Func<string, IReadOnlyList<obj>> = null with get, set

    /// How a suggestion row is rendered. Required.
    member val ItemTemplate: DataTemplate = null with get, set

    /// Optional validation, called on accept with the current text and the selected item (null when the
    /// text matches no row). Invalid keeps the popup open and shows the message. When absent, acceptance
    /// is unconditional.
    member val Validate: Func<string, obj, SelectorValidation> = null with get, set

    /// The accepted selection: the canonical text (from validation, else the raw input) and the selected
    /// item (null in free-text mode when nothing was highlighted).
    member val OnAccept: Action<string, obj> = null with get, set

    /// Called when the popup closes without an acceptance (Esc or focus loss). Optional.
    member val OnDismiss: Action = null with get, set

    /// Optional Tab completion: maps the highlighted item and the current text to the new input text.
    /// When absent, Tab does nothing.
    member val CompleteText: Func<obj, string, string> = null with get, set

    /// Optional first-chance key hook (e.g. the command palette's Alt+Enter shortcut capture). Runs
    /// before the built-in key handling; return true to mark the key handled.
    member val OnUnhandledKey: Func<KeyEventArgs, bool> = null with get, set

/// The free-text input history of a selector: recorded acceptances, recalled with Up past the top of the
/// list, cancelled with Esc, committed with Enter. Pure - the window applies the returned state and text.
type SelectorHistoryState = {
    Entries: string list
    Index: int
    Draft: string
    InHistory: bool
}

[<RequireQualifiedAccess>]
module SelectorHistory =

    let empty = { Entries = []; Index = -1; Draft = ""; InHistory = false }

    /// Record an accepted entry (consecutive duplicates collapse) and leave history mode.
    let record (entry: string) state =

        let entries =
            match List.tryLast state.Entries with
            | Some last when last = entry -> state.Entries
            | _ -> state.Entries @ [ entry ]
        { Entries = entries; Index = -1; Draft = ""; InHistory = false }

    /// Enter history mode at the newest entry, stashing the current input as the draft.
    /// No-op when there is nothing to recall.
    let enter (currentText: string) state =

        if state.Entries.IsEmpty then
            state, None
        else
            let index = state.Entries.Length - 1
            { state with InHistory = true; Draft = currentText; Index = index }, Some state.Entries[index]

    /// One step towards older entries (already in history mode).
    let up state =

        if state.InHistory && state.Index > 0 then
            let index = state.Index - 1
            { state with Index = index }, Some state.Entries[index]
        else
            state, None

    /// Leave history mode and restore the stashed draft.
    let cancel state =

        { state with InHistory = false; Index = -1 }, state.Draft

    /// One step towards newer entries; stepping past the newest cancels back to the draft.
    let down state =

        if not state.InHistory then
            state, None
        elif state.Index + 1 >= state.Entries.Length then
            let next, draft = cancel state
            next, Some draft
        else
            let index = state.Index + 1
            { state with Index = index }, Some state.Entries[index]

    /// Leave history mode keeping the recalled text as the live input.
    let commit state =

        { state with InHistory = false; Index = -1 }

/// The borderless, owner-centred selection popup extracted from the command palette: a single text input
/// above a filtered list. Search-as-you-type is always on; Up/Down navigate; Enter accepts (validated when
/// the client supplies a validator); Esc or focus loss dismisses. With FreeText enabled the typed text may
/// deviate from the list (arguments, ad-hoc answers) and pressing Up on the topmost item recalls input
/// history. All view concern - meaning lives in the client's SelectorOptions callbacks.
[<ExcludeFromCodeCoverage>] // WPF window; the history logic is the unit-tested SelectorHistory module
type SelectorWindow(options: SelectorOptions) as this =
    inherit Window()

    let mutable history = SelectorHistory.empty
    let mutable suppressTextChanged = false
    let mutable closing = false
    let mutable accepted = false
    let mutable dismissNotified = false

    let rootTransform = TranslateTransform()

    let input =
        TextBox(
            FontSize = 17.0,
            BorderThickness = Thickness(0.0),
            Background = Brushes.Transparent,
            CaretBrush = Brushes.White,
            Padding = Thickness(0.0, 0.0, 0.0, 10.0),
            Margin = Thickness(0.0, 0.0, 0.0, 6.0))

    let prompt =
        TextBlock(
            FontSize = 13.0,
            Margin = Thickness(0.0, 0.0, 0.0, 8.0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed)

    let message =
        TextBlock(
            FontSize = 13.0,
            Margin = Thickness(0.0, 0.0, 0.0, 6.0),
            Visibility = Visibility.Collapsed)

    let list =
        ListBox(
            MaxHeight = 360.0,
            BorderThickness = Thickness(0.0),
            Background = Brushes.Transparent)

    let root = Border(BorderThickness = Thickness(1.0), Padding = Thickness(16.0, 14.0, 16.0, 12.0))

    // Match the events panel: a chrome-free row whose only selection cue is a faint background fill. The
    // selected fill is stronger than hover because the popup is keyboard-driven.
    static let frozenFill (hex: string) =
        let brush = SolidColorBrush(ColorConverter.ConvertFromString(hex) :?> Color)
        brush.Freeze()
        brush :> Brush

    static let selectedFill = frozenFill "#16FFFFFF"
    static let hoverFill = frozenFill "#06FFFFFF"

    static let buildItemContainerStyle () =

        let border = FrameworkElementFactory(typeof<Border>, Name = "Bd")
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent)
        border.SetValue(Border.PaddingProperty, Thickness(8.0, 0.0, 8.0, 0.0))
        border.AppendChild(FrameworkElementFactory(typeof<ContentPresenter>))

        let template = ControlTemplate(typeof<ListBoxItem>, VisualTree = border)

        let hover = Trigger(Property = UIElement.IsMouseOverProperty, Value = true)
        hover.Setters.Add(Setter(Border.BackgroundProperty, hoverFill, "Bd"))
        template.Triggers.Add hover

        let selected = Trigger(Property = ListBoxItem.IsSelectedProperty, Value = true)
        selected.Setters.Add(Setter(Border.BackgroundProperty, selectedFill, "Bd"))
        template.Triggers.Add selected

        let style = Style(typeof<ListBoxItem>)
        style.Setters.Add(Setter(Control.TemplateProperty, template))
        style.Setters.Add(Setter(Control.PaddingProperty, Thickness(0.0)))
        style.Setters.Add(Setter(FrameworkElement.FocusVisualStyleProperty, null))
        style.Setters.Add(Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch))
        style

    let setInput (text: string) =

        suppressTextChanged <- true
        input.Text <- text
        input.CaretIndex <- input.Text.Length
        suppressTextChanged <- false

    let clearMessage () =

        message.Text <- ""
        message.Visibility <- Visibility.Collapsed

    let showMessage (text: string) =

        message.Text <- text
        message.Visibility <- Visibility.Visible

    let refresh () =

        let suggestions =
            match options.GetSuggestions with
            | null -> [] :> IReadOnlyList<obj>
            | get -> get.Invoke(input.Text)
        list.ItemsSource <- suggestions
        if suggestions.Count > 0 then
            list.SelectedIndex <- 0

    let positionOverOwner () =

        match this.Owner with
        | owner when not (isNull owner) && owner.WindowState <> WindowState.Minimized && owner.ActualWidth > 0.0 ->
            this.Left <- owner.Left + ((owner.ActualWidth - this.Width) / 2.0)
            this.Top <- owner.Top + 80.0
        | _ ->
            this.Left <- (SystemParameters.PrimaryScreenWidth - this.Width) / 2.0
            this.Top <- SystemParameters.PrimaryScreenHeight / 6.0

    // Fade in with a small downward settle (the overlay arriving over the workspace).
    let animateIn () =

        root.Opacity <- 0.0
        rootTransform.Y <- -8.0
        root.BeginAnimation(UIElement.OpacityProperty, DoubleAnimation(0.0, 1.0, Motion.Quick, EasingFunction = Motion.easeOut ()))
        rootTransform.BeginAnimation(TranslateTransform.YProperty, DoubleAnimation(-8.0, 0.0, Motion.Quick, EasingFunction = Motion.easeOut ()))

    // Fade out, then hide for real - so closing on Esc, accept, or focus loss never blinks away.
    let hideAnimated () =

        if this.IsVisible && not closing then
            closing <- true
            if not accepted && not dismissNotified then
                dismissNotified <- true
                match options.OnDismiss with
                | null -> ()
                | dismiss -> dismiss.Invoke()
            let fade = DoubleAnimation(root.Opacity, 0.0, Motion.Quick, EasingFunction = Motion.easeOut ())
            fade.Completed.Add(fun _ ->
                closing <- false
                this.Hide())
            root.BeginAnimation(UIElement.OpacityProperty, fade)

    let accept (canonicalText: string) (item: obj) =

        accepted <- true
        match options.OnAccept with
        | null -> ()
        | onAccept -> onAccept.Invoke(canonicalText, item)
        if options.FreeText && canonicalText.Trim().Length > 0 then
            history <- SelectorHistory.record (canonicalText.Trim()) history
        hideAnimated ()

    let tryAccept () =

        let text = input.Text
        let selected = list.SelectedItem
        if options.FreeText && text.Trim().Length = 0 then
            // Free text needs a value; an empty line is a no-op (matches the old palette).
            ()
        elif not options.FreeText && isNull selected then
            // Strict selection needs a highlighted row.
            ()
        else
            match options.Validate with
            | null ->
                accept text selected
            | validate ->
                let outcome = validate.Invoke(text, selected)
                if outcome.IsValid then
                    // Reflect the canonical text (e.g. a completed command name) so it is what the
                    // user sees recorded and what history recalls.
                    if options.FreeText && outcome.CanonicalText <> text then
                        setInput outcome.CanonicalText
                    accept outcome.CanonicalText selected
                else
                    showMessage outcome.Message

    let completeSelected () =

        match options.CompleteText, list.SelectedItem with
        | null, _ | _, null -> ()
        | complete, item -> setInput (complete.Invoke(item, input.Text))

    let navigateUp () =

        if history.InHistory then
            let next, text = SelectorHistory.up history
            history <- next
            text |> Option.iter setInput
        elif list.SelectedIndex > 0 then
            list.SelectedIndex <- list.SelectedIndex - 1
            list.ScrollIntoView(list.SelectedItem)
        elif options.FreeText then
            let next, text = SelectorHistory.enter input.Text history
            history <- next
            text |> Option.iter setInput

    let navigateDown () =

        if history.InHistory then
            let wasInHistory = history.InHistory
            let next, text = SelectorHistory.down history
            history <- next
            text |> Option.iter setInput
            if wasInHistory && not history.InHistory then
                refresh ()
        elif list.SelectedIndex >= 0 && list.SelectedIndex < list.Items.Count - 1 then
            list.SelectedIndex <- list.SelectedIndex + 1
            list.ScrollIntoView(list.SelectedItem)

    let cancelHistory () =

        let next, draft = SelectorHistory.cancel history
        history <- next
        setInput draft
        refresh ()

    do
        this.WindowStyle <- WindowStyle.None
        this.AllowsTransparency <- true
        this.Background <- Brushes.Transparent
        this.ShowInTaskbar <- false
        this.ResizeMode <- ResizeMode.NoResize
        this.SizeToContent <- SizeToContent.Height
        this.WindowStartupLocation <- WindowStartupLocation.Manual
        this.Width <- options.Width

        input.SetResourceReference(Control.ForegroundProperty, "TextBrightBrush")
        input.SetResourceReference(Control.FontFamilyProperty, "UiFont")
        input.TextChanged.Add(fun _ ->
            if not suppressTextChanged then
                history <- SelectorHistory.commit history
                clearMessage ()
                refresh ())

        prompt.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush")
        prompt.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont")
        if options.Prompt.Length > 0 then
            prompt.Text <- options.Prompt
            prompt.Visibility <- Visibility.Visible

        message.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush")
        message.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont")

        list.ItemTemplate <- options.ItemTemplate
        list.ItemContainerStyle <- buildItemContainerStyle ()
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled)
        list.PreviewMouseDoubleClick.Add(fun _ ->
            if not (isNull list.SelectedItem) then
                tryAccept ())

        let inputBorder = Border(BorderThickness = Thickness(0.0, 0.0, 0.0, 1.0))
        inputBorder.SetResourceReference(Border.BorderBrushProperty, "LineBrush")
        inputBorder.Child <- input

        let stack = StackPanel()
        stack.Children.Add prompt |> ignore
        stack.Children.Add inputBorder |> ignore
        stack.Children.Add message |> ignore
        stack.Children.Add list |> ignore

        root.SetResourceReference(Border.BackgroundProperty, "BlackBrush")
        root.SetResourceReference(Border.BorderBrushProperty, "FrameBrush")
        root.RenderTransform <- rootTransform
        root.Child <- stack
        this.Content <- root

    /// The current input text (read by clients implementing custom key handling).
    member _.Text = input.Text

    /// The currently highlighted item, or null.
    member _.SelectedItem = list.SelectedItem

    /// Show an inline message under the input (validation errors, capture hints) without closing.
    member _.ShowMessage(text: string) = showMessage text

    member _.ClearMessage() = clearMessage ()

    /// Replace the input text without re-entering history mode.
    member _.SetText(text: string) =
        setInput text
        refresh ()

    member _.Dismiss() = hideAnimated ()

    /// Open (or re-open) the popup over the application's main window, reset to an empty filter.
    member this.ShowSelector() =

        match Application.Current.MainWindow with
        | owner when not (isNull owner) && not (obj.ReferenceEquals(owner, this)) -> this.Owner <- owner
        | _ -> ()

        setInput ""
        history <- { history with Index = -1; Draft = ""; InHistory = false }
        closing <- false
        accepted <- false
        dismissNotified <- false
        clearMessage ()
        refresh ()
        positionOverOwner ()
        this.Show()
        animateIn ()
        this.Activate() |> ignore
        input.Focus() |> ignore

    override _.OnDeactivated(e) =
        base.OnDeactivated(e)
        hideAnimated ()

    override this.OnPreviewKeyDown(e) =

        let interceptedByClient =
            match options.OnUnhandledKey with
            | null -> false
            | hook -> hook.Invoke(e)

        if interceptedByClient then
            e.Handled <- true
        else
            match e.Key with
            | Key.Escape when history.InHistory ->
                cancelHistory ()
                e.Handled <- true
            | Key.Escape ->
                hideAnimated ()
                e.Handled <- true
            | Key.Up ->
                navigateUp ()
                e.Handled <- true
            | Key.Down ->
                navigateDown ()
                e.Handled <- true
            | Key.Tab ->
                completeSelected ()
                e.Handled <- true
            | Key.Enter when history.InHistory ->
                history <- SelectorHistory.commit history
                clearMessage ()
                refresh ()
                e.Handled <- true
            | Key.Enter ->
                tryAccept ()
                e.Handled <- true
            | _ ->
                base.OnPreviewKeyDown(e)
