using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// Builders for the window's static chrome: the prompt input box, the title bar (drag grip, CLAVIS label,
/// close glyph), the input row, and the status bar. Pure view construction with no per-window state, kept
/// out of WindowHost so that file stays focused on lifecycle, regions, slide-ins, and focus.
internal static class WindowChromeViews
{
    public static TextBox CreateInputBox()
    {
        var inputBox = new TextBox
        {
            FontSize = 10,
            Foreground = Brush("#E8E8EC"),
            // A non-frozen caret brush so the host can tint it to the session's permission mode (animated).
            CaretBrush = new SolidColorBrush(Colors.White),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,

            // No fixed line cap: the input grows with its content (the host caps it at 60% of the chat
            // height) and scrolls internally once it reaches that ceiling.
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(0)
        };
        inputBox.SetResourceReference(Control.FontFamilyProperty, "AgentFont");
        return inputBox;
    }

    // Returns the title bar plus its status dot, which the window breathes while active and dims when not,
    // so the focused window reads at a glance across a multi-window workspace. The drag grip and the static
    // "CLAVIS" label were removed (the whole bar is draggable, and the word carried no information); the
    // left holds a small active-window dot beside the contextual title region, the right an info region.
    public static (Border TitleBar, Ellipse StatusDot) CreateTitleBar(
        FrameworkElement titleBarLeft, ContentPresenter titleBarRight, Action onClose)
    {
        // An activity indicator is a circle, never a square - square corners are for chrome, dots are round.
        var statusDot = new Ellipse
        {
            Width = 6,
            Height = 6,
            Margin = new Thickness(0, 0, 9, 0),
            Opacity = 0.3,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusDot.SetResourceReference(Shape.FillProperty, "ClavisBrush");

        titleBarLeft.VerticalAlignment = VerticalAlignment.Center;

        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        headerPanel.Children.Add(statusDot);
        headerPanel.Children.Add(titleBarLeft);

        var closeButton = CreateCloseButton(onClose);
        DockPanel.SetDock(closeButton, Dock.Right);

        DockPanel.SetDock(titleBarRight, Dock.Right);
        titleBarRight.VerticalAlignment = VerticalAlignment.Center;
        titleBarRight.Margin = new Thickness(0, 0, 10, 0);

        var dockPanel = new DockPanel();
        dockPanel.Children.Add(closeButton);
        dockPanel.Children.Add(titleBarRight);
        dockPanel.Children.Add(headerPanel);

        var border = new Border
        {
            Height = 28,
            Padding = new Thickness(14, 0, 4, 0),
            Cursor = Cursors.Hand
        };
        border.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        border.SetResourceReference(Border.BackgroundProperty, "BlackBrush");
        border.BorderThickness = new Thickness(0, 0, 0, 1);
        border.Child = dockPanel;

        // With the OS caption disabled (see MainWindow.xaml) the title bar drives window drag itself:
        // press-drag anywhere on the bar moves the window and a double-click toggles maximize. A press that
        // lands on the close button or the right-content region is left alone so its own click still fires.
        border.MouseLeftButtonDown += (_, args) =>
        {
            if (IsWithin(args.OriginalSource as DependencyObject, closeButton)
                || IsWithin(args.OriginalSource as DependencyObject, titleBarRight))
            {
                return;
            }

            if (Window.GetWindow(border) is not { } window)
            {
                return;
            }

            if (args.ClickCount == 2)
            {
                window.WindowState = window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                return;
            }

            window.DragMove();
        };

        return (border, statusDot);
    }

    // Walk up the visual tree to see whether the pressed element lives inside a chrome region that owns its
    // own click (the close button, the right content), so the title-bar drag leaves it alone.
    private static bool IsWithin(DependencyObject? node, DependencyObject region)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, region))
            {
                return true;
            }

            node = node is Visual visual ? VisualTreeHelper.GetParent(visual) : LogicalTreeHelper.GetParent(node);
        }

        return false;
    }

    /// The window's close affordance: the shared close cross (white at rest, easing to clavis blue and
    /// growing a touch on hover, no background fill) so window chrome and panel tabs close identically.
    /// Given a fixed hit box; ordinary client content now the OS caption is disabled, so its own click fires
    /// and the title-bar drag skips it (see CreateTitleBar).
    private static Border CreateCloseButton(Action onClose)
    {
        var button = CloseButton.create(onClose);
        button.Width = 30;
        button.Height = 28;
        return button;
    }

    // A translucent dark veil so the input/status overlay floats over the chat output, which reads faintly
    // through it. Opaque enough that the input text and status stay legible.
    private static SolidColorBrush OverlayVeil() => Brush("#D80A0A10");

    /// The prompt input row, returned with the two elements the host animates to reflect the session's
    /// permission mode: ModeEdge (a thin left rule tinted to the mode) and ModeLabel (a small tag at the
    /// right). Both start neutral/hidden; SetSessionMode drives them. The outer border keeps the top framing
    /// line the host recolours on focus.
    public static (Border Row, Border ModeEdge, TextBlock ModeLabel) CreateInputRow(TextBox inputBox)
    {
        // A thin vertical rule at the very left edge, tinted to the permission mode (transparent in the
        // default mode). A Border (a rule), not a dot - square corners like all chrome.
        var modeEdge = new Border
        {
            Width = 3,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Colors.Transparent)
        };

        // The mode tag: a small uppercase label at the right of the input, shown only for a mode that has an
        // accent. Starts collapsed and transparent; SetSessionMode fades it in.
        var modeLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 9,
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(12, 0, 0, 0)
        };
        modeLabel.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");

        // Content sits inset from the edges (the 3px rule plus a gap makes up the left inset); the tag docks
        // right, the input fills the rest.
        var content = new DockPanel { Margin = new Thickness(25, 8, 28, 8) };
        DockPanel.SetDock(modeLabel, Dock.Right);
        content.Children.Add(modeLabel);
        content.Children.Add(inputBox);

        var grid = new Grid();
        grid.Children.Add(content);
        grid.Children.Add(modeEdge);

        // Tighter top/bottom via the content margin so the framing line sits close to the text; the host
        // recolours the top line (and the status bar's top line) to clavis while the input is focused.
        var border = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background = OverlayVeil(),
            Child = grid
        };
        border.SetResourceReference(Border.BorderBrushProperty, "FrameBrush");
        return (border, modeEdge, modeLabel);
    }

    public static Border CreateStatusBar(ContentPresenter statusContent, ContentPresenter rightContent)
    {
        statusContent.VerticalAlignment = VerticalAlignment.Center;
        rightContent.VerticalAlignment = VerticalAlignment.Center;
        // Gap so the plugin glyph (e.g. the usage limit-plane) does not butt against the status text/version.
        rightContent.Margin = new Thickness(16, 0, 0, 0);

        // Status text fills from the left; a plugin-owned indicator (e.g. the usage pace glyph) docks
        // right. The right region stays empty until a plugin contributes to it.
        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(rightContent, Dock.Right);
        layout.Children.Add(rightContent);
        layout.Children.Add(statusContent);

        var border = new Border
        {
            Padding = new Thickness(28, 5, 28, 5),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background = OverlayVeil()
        };
        border.SetResourceReference(Border.BorderBrushProperty, "FrameBrush");
        border.Child = layout;
        return border;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
