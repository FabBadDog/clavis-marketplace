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
            CaretBrush = Brushes.White,
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

    public static Border CreateInputRow(TextBox inputBox)
    {
        // Tighter top/bottom padding so the framing lines sit close to the text; the host recolours the top
        // line (and the status bar's top line) to clavis while the input is focused.
        var border = new Border
        {
            Padding = new Thickness(28, 8, 28, 8),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background = OverlayVeil()
        };
        border.SetResourceReference(Border.BorderBrushProperty, "FrameBrush");
        border.Child = inputBox;
        return border;
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
