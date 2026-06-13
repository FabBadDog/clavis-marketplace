namespace FabioSoft.Clavis.Controls

open System
open System.Collections
open System.Diagnostics.CodeAnalysis
open System.Windows
open System.Windows.Controls
open System.Windows.Data

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

/// A generic lazy tree view: a dark, borderless TreeView whose rows are monospace labels, expanding nodes
/// on demand. Nodes expose `Name`/`Children`/`IsExpanded` (bound by convention) and implement ITreeNode;
/// onActivate fires when the user selects a leaf. The editor's file tree is one consumer - any hierarchy
/// (an outline, a settings tree) can reuse it by supplying nodes of its own.
[<ExcludeFromCodeCoverage>] // WPF control construction
[<RequireQualifiedAccess>]
module TreeBrowser =

    [<Literal>]
    let private rowFontSize = 11.0

    let private rowTemplate () =
        let template = HierarchicalDataTemplate()
        template.ItemsSource <- Binding("Children")
        let text = FrameworkElementFactory(typeof<TextBlock>)
        text.SetBinding(TextBlock.TextProperty, Binding("Name"))
        text.SetValue(TextBlock.FontSizeProperty, rowFontSize)
        text.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont")
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush")
        template.VisualTree <- text
        template

    let private containerStyle () =
        let style = Style(typeof<TreeViewItem>)
        style.Setters.Add(Setter(TreeViewItem.IsExpandedProperty, Binding("IsExpanded", Mode = BindingMode.TwoWay)))
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
