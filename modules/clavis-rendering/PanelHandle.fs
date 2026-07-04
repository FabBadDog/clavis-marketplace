namespace FabioSoft.Clavis.Rendering

open System
open System.Diagnostics
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Windows
open System.Windows.Controls
open System.Windows.Documents
open System.Windows.Input
open System.Windows.Media

/// The cursor's screen position in physical pixels. Captured the instant a drag ends so the host can map
/// it to whichever window sits under the pointer; WPF exposes no managed screen-cursor query.
[<RequireQualifiedAccess>]
[<ExcludeFromCodeCoverage>] // thin P/Invoke wrapper
module internal NativeCursor =

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type private NativePoint =
        val mutable X: int
        val mutable Y: int

    [<DllImport("user32.dll")>]
    extern bool private GetCursorPos(NativePoint& point)

    let position () =
        let mutable point = NativePoint()
        GetCursorPos(&point) |> ignore
        Point(float point.X, float point.Y)

/// The draggable panel handle shared by the docking surface's lone panels and the edge slide-ins, so both
/// carry the same slim, hover-revealed tab (title + close + drag) and drag with one identical gesture.
/// Lives in the never-unloaded Default ALC, so it roots no plugin types.
[<ExcludeFromCodeCoverage>] // WPF construction + OLE drag
[<RequireQualifiedAccess>]
module PanelHandle =

    /// The drag data-format key a panel is dragged under. The host registers a window-level drop target with
    /// the same key so a cross-window drop routes even to an owned, transparent window's HWND.
    [<Literal>]
    let DragFormat = "ClavisPanelId"

    /// A handle reveals only while the cursor is within this top band of its host (or over the handle
    /// itself), so the affordance appears where it lives rather than on a hover anywhere in the panel.
    [<Literal>]
    let hoverBandHeight = 24.0

    // The slim hover handle bar backdrop: translucent dark so it reads as a thin strip floating over the
    // panel's own content rather than a chrome band.
    let private handleBarBrush =
        SolidColorBrush(Color.FromArgb(0xE0uy, 0x14uy, 0x14uy, 0x1Cuy)) |> fun brush -> brush.Freeze(); brush

    /// The handle's content: the panel title (upper-cased chrome label) beside the shared close cross.
    let header (title: string) (onClose: unit -> unit) : FrameworkElement =
        let titleBlock =
            TextBlock(
                Text = title.ToUpperInvariant(),
                FontSize = 10.5,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = Thickness(0.0, 0.0, 7.0, 0.0))
        titleBlock.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont")

        let closeButton = CloseButton.create (Action onClose)
        closeButton.VerticalAlignment <- VerticalAlignment.Center

        let panel = StackPanel(Orientation = Orientation.Horizontal)
        panel.Children.Add(titleBlock) |> ignore
        panel.Children.Add(closeButton) |> ignore
        panel

    /// Wrap handle content in the slim bar: parked invisible (Opacity 0) and not hit-testable until the
    /// hover reveal shows it, top-right of the panel, over the window's own dark surface.
    let buildBar (content: FrameworkElement) : Border =
        TextElement.SetForeground(content, Colors.textBright)
        Border(
            Background = handleBarBrush,
            Padding = Thickness(6.0, 1.0, 4.0, 1.0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = 0.0,
            // Not hit-testable until revealed: an invisible (Opacity 0) element still hit-tests in WPF, so
            // without this a press in the panel's content under the parked handle would arm a drag. Gated on
            // the shown state, the drag starts only from the visible panel tab.
            IsHitTestVisible = false,
            Child = content)

    /// Reveal the bar once the cursor reaches the host's top band, then keep it shown while the cursor stays
    /// anywhere over the panel so it can be navigated to and grabbed - hiding only when the cursor leaves.
    /// Hit-testing follows the reveal so the handle is a drag target only while shown. Hiding on every
    /// out-of-band move (the earlier behaviour) also cleared IsHitTestVisible, which latched the handle
    /// unreachable: once non-hit-testable it could never re-detect the cursor, so moving toward it hid it.
    let attachHoverReveal (hoverSource: FrameworkElement) (bar: FrameworkElement) =
        let mutable shown = false
        let setShown value =
            if value <> shown then
                shown <- value
                bar.IsHitTestVisible <- value
                Motion.fadeTo bar (if value then 1.0 else 0.0)

        hoverSource.MouseMove.Add(fun args ->
            let y = args.GetPosition(hoverSource).Y
            let overBar = bar.IsMouseOver
            if y <= hoverBandHeight || overBar then
                setShown true)
        hoverSource.MouseLeave.Add(fun _ -> setShown false)

    /// Arm a panel drag on element (a whole tab item, a lone panel's handle, or a slide-in's handle), so the
    /// gesture starts wherever on the tab the press lands. onMoving reports the cursor's screen position each
    /// tick (for the cross-window drop hint); onFellThrough fires when no window accepted the OLE drop yet the
    /// panel is still owned here (the cross-window / tear-off fallback); onCompleted fires once the drag ends.
    /// isOwned answers whether this element's panel still lives here after the drag - if it moved, no
    /// fall-through. A re-entrancy guard stops a stray move re-issued mid-drag from starting a second drag.
    let attachDrag
        (element: FrameworkElement)
        (panelId: Guid)
        (onMoving: Point -> unit)
        (onFellThrough: Point -> unit)
        (onCompleted: unit -> unit)
        (isOwned: unit -> bool)
        =
        // The OS shows the no-drop cursor whenever no target sets an effect - which is always when dragging
        // over a window that never registered as a drop target. Suppress it so a cross-window drag reads as
        // valid; the host paints the actual drop-zone overlay on the window under the cursor.
        element.GiveFeedback.Add(fun args ->
            if args.Effects = DragDropEffects.None then
                args.UseDefaultCursors <- false
                Mouse.SetCursor(Cursors.Hand) |> ignore
                args.Handled <- true)

        // Fires continuously through the drag (on the source, which always gets these). Each tick reports the
        // cursor's screen position so the host can drive the drop-zone overlay across windows.
        element.QueryContinueDrag.Add(fun _ -> onMoving (NativeCursor.position ()))

        let mutable dragOrigin: Point option = None
        let mutable dragging = false
        element.PreviewMouseLeftButtonDown.Add(fun args -> dragOrigin <- Some(args.GetPosition(element)))
        element.PreviewMouseLeftButtonUp.Add(fun _ -> dragOrigin <- None)
        element.PreviewMouseMove.Add(fun args ->
            match dragOrigin with
            | Some origin when args.LeftButton = MouseButtonState.Pressed && not dragging ->
                let current = args.GetPosition(element)
                let moved =
                    abs (current.X - origin.X) > SystemParameters.MinimumHorizontalDragDistance
                    || abs (current.Y - origin.Y) > SystemParameters.MinimumVerticalDragDistance
                if moved then
                    dragOrigin <- None
                    dragging <- true
                    try
                        let result =
                            DragDrop.DoDragDrop(element, DataObject(DragFormat, panelId.ToString()), DragDropEffects.Move)
                        onCompleted ()
                        // A None result means no window accepted the OLE drop. If the panel still lives here it
                        // was not moved locally either, so fall back to a cursor-resolved cross-window move - an
                        // owned transparent target window never registers as a drop target for the OS.
                        if result = DragDropEffects.None && isOwned () then
                            onFellThrough (NativeCursor.position ())
                    with ex ->
                        // OLE drag is a shared-resource operation that can throw when another process holds the
                        // OLE lock; a failed drag is cosmetic. Clear any in-progress drag visuals and drop it
                        // rather than attempting the cross-window fallback on an unknown failure.
                        Trace.TraceWarning($"PanelHandle: drag operation failed: {ex.Message}")
                        onCompleted ()
                    dragging <- false
            | _ -> ())
