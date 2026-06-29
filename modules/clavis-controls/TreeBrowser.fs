namespace FabioSoft.Clavis.Controls

open System
open System.Collections
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Controls.Primitives
open System.Windows.Data
open System.Windows.Media

/// A node a TreeBrowser can show. The view binds each node's `Name` (label) and `Children` (sub-nodes) by
/// convention and its `IsExpanded` two-way (for lazy expansion); `IsLeaf` tells the selection logic whether
/// a node is an actionable leaf (e.g. a file) rather than an expandable group (a folder).
type ITreeNode =
    abstract member IsLeaf: bool

/// Pure selection logic for the tree browser, separated from the WPF view so it is unit-testable.
[<RequireQualifiedAccess>]
module TreeBrowserModel =

    /// The selected item is worth raising to the consumer only when it is a leaf node (a file, not a
    /// folder); a folder selection just expands. Returns it typed so the view hands a concrete node back.
    let activatable (selected: obj) : ITreeNode option =
        match selected with
        | :? ITreeNode as node when node.IsLeaf -> Some node
        | _ -> None

/// A generic lazy tree view: a dark, borderless TreeView whose rows are clean sans-serif labels, expanding
/// nodes on demand. Nodes expose `Name`/`Children`/`IsExpanded` (bound by convention) and implement
/// ITreeNode; onActivate fires when the user selects a leaf. The rows are fully re-templated - a custom
/// chevron expander plus a palette selection fill - so selection never falls back to the system "white"
/// highlight and the look is theme-driven (host resource keys) rather than baked. Any panel that needs a
/// hierarchy (a file tree, an outline, a settings tree) reuses this by supplying nodes of its own.
[<ExcludeFromCodeCoverage>] // WPF control construction
[<RequireQualifiedAccess>]
module TreeBrowser =

    [<Literal>]
    let private rowFontSize = 12.5

    [<Literal>]
    let private indentSize = 14.0

    let private frozenBrush (hex: string) =
        let brush = SolidColorBrush(ColorConverter.ConvertFromString hex :?> Color)
        brush.Freeze()
        brush :> Brush

    // Selection cue: a faint clavis-blue band, painted by our own template so the system highlight (which
    // washes the row white and makes the label unreadable) never shows. Hover is a barely-there white lift.
    let private selectedFill = frozenBrush "#339FD5F0"
    let private hoverFill = frozenBrush "#0CFFFFFF"

    let private rowTemplate () =
        let template = HierarchicalDataTemplate()
        template.ItemsSource <- Binding("Children")
        let text = FrameworkElementFactory(typeof<TextBlock>)
        text.SetBinding(TextBlock.TextProperty, Binding("Name"))
        text.SetValue(TextBlock.FontSizeProperty, rowFontSize)
        text.SetResourceReference(TextBlock.FontFamilyProperty, "AgentFont")
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush")
        template.VisualTree <- text
        template

    // A chevron toggle whose glyph flips between collapsed and expanded with its checked state.
    let private expanderTemplate () =
        let glyph = FrameworkElementFactory(typeof<TextBlock>, "Glyph")
        glyph.SetValue(TextBlock.TextProperty, "▸")
        glyph.SetValue(TextBlock.FontSizeProperty, 9.0)
        glyph.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center)
        glyph.SetValue(FrameworkElement.WidthProperty, indentSize)
        glyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center)
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush")

        let template = ControlTemplate(typeof<ToggleButton>, VisualTree = glyph)
        let expanded = Trigger(Property = ToggleButton.IsCheckedProperty, Value = true)
        expanded.Setters.Add(Setter(TextBlock.TextProperty, "▾", "Glyph"))
        template.Triggers.Add expanded
        template

    let private containerTemplate () =
        let stack = FrameworkElementFactory(typeof<StackPanel>)

        let headerRow = FrameworkElementFactory(typeof<DockPanel>)
        headerRow.SetValue(DockPanel.LastChildFillProperty, true)

        let expander = FrameworkElementFactory(typeof<ToggleButton>, "Expander")
        expander.SetValue(DockPanel.DockProperty, Dock.Left)
        expander.SetValue(Control.TemplateProperty, expanderTemplate ())
        expander.SetValue(UIElement.FocusableProperty, false)
        expander.SetValue(Control.BackgroundProperty, Brushes.Transparent)
        expander.SetValue(Control.BorderThicknessProperty, Thickness(0.0))
        expander.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center)
        expander.SetBinding(
            ToggleButton.IsCheckedProperty,
            Binding("IsExpanded", RelativeSource = RelativeSource(RelativeSourceMode.TemplatedParent), Mode = BindingMode.TwoWay))
        headerRow.AppendChild expander

        let bd = FrameworkElementFactory(typeof<Border>, "Bd")
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent)
        bd.SetValue(Border.PaddingProperty, Thickness(2.0, 2.0, 8.0, 2.0))
        let header = FrameworkElementFactory(typeof<ContentPresenter>)
        header.SetValue(ContentPresenter.ContentSourceProperty, "Header")
        header.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center)
        bd.AppendChild header
        headerRow.AppendChild bd

        stack.AppendChild headerRow

        let items = FrameworkElementFactory(typeof<ItemsPresenter>, "ItemsHost")
        items.SetValue(FrameworkElement.MarginProperty, Thickness(indentSize, 0.0, 0.0, 0.0))
        stack.AppendChild items

        let template = ControlTemplate(typeof<TreeViewItem>, VisualTree = stack)

        let collapsed = Trigger(Property = TreeViewItem.IsExpandedProperty, Value = false)
        collapsed.Setters.Add(Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "ItemsHost"))
        template.Triggers.Add collapsed

        let leaf = Trigger(Property = ItemsControl.HasItemsProperty, Value = false)
        leaf.Setters.Add(Setter(UIElement.VisibilityProperty, Visibility.Hidden, "Expander"))
        template.Triggers.Add leaf

        let hover = Trigger(Property = UIElement.IsMouseOverProperty, Value = true)
        hover.Setters.Add(Setter(Border.BackgroundProperty, hoverFill, "Bd"))
        template.Triggers.Add hover

        // Added after hover so a selected row keeps its fill even while hovered.
        let selected = Trigger(Property = TreeViewItem.IsSelectedProperty, Value = true)
        selected.Setters.Add(Setter(Border.BackgroundProperty, selectedFill, "Bd"))
        template.Triggers.Add selected

        template

    let private containerStyle () =
        let style = Style(typeof<TreeViewItem>)
        style.Setters.Add(Setter(TreeViewItem.IsExpandedProperty, Binding("IsExpanded", Mode = BindingMode.TwoWay)))
        style.Setters.Add(Setter(Control.TemplateProperty, containerTemplate ()))
        style.Setters.Add(Setter(Control.PaddingProperty, Thickness(0.0)))
        style.Setters.Add(Setter(FrameworkElement.FocusVisualStyleProperty, null))
        style

    /// Build the tree over `roots` (a collection of nodes); onActivate runs when a leaf node is selected.
    let create (roots: IEnumerable) (onActivate: Action<ITreeNode>) : TreeView =
        let tree = TreeView(ItemsSource = roots, BorderThickness = Thickness(0.0))
        tree.SetResourceReference(Control.BackgroundProperty, "BlackBrush")
        tree.ItemTemplate <- rowTemplate ()
        tree.ItemContainerStyle <- containerStyle ()
        tree.SelectedItemChanged.Add(fun args ->
            match TreeBrowserModel.activatable args.NewValue with
            | Some node -> onActivate.Invoke node
            | None -> ())
        tree
