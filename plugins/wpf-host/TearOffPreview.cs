using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// A translucent, non-activating preview shown during a panel drag when the cursor is clear of every
/// existing window: it sketches the outline of the new window the drop will tear off, so the gesture's
/// outcome is visible rather than reading as a "drop not allowed" cursor. Created lazily and reused across
/// drags; the host hides it when the cursor returns over a window (where the dock-zone hint takes over).
[ExcludeFromCodeCoverage] // WPF window construction
internal sealed class TearOffPreview
{
    // The sketch roughly matches a freshly torn-off window's footprint so the preview reads as "a window
    // lands here", not a generic marker.
    private const double PreviewWidth = 260;
    private const double PreviewHeight = 170;

    private static readonly Color ClavisColor = Color.FromRgb(0x9F, 0xD5, 0xF0);

    private Window? _window;

    private Window Ensure()
    {
        if (_window is not null)
        {
            return _window;
        }

        var label = new TextBlock
        {
            Text = "NEW WINDOW",
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = Frozen(ClavisColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");

        // Square corners (the design forbids rounded rectangles): a faint clavis wash inside a clavis edge.
        var frame = new Border
        {
            Background = Frozen(Color.FromArgb(0x22, 0x9F, 0xD5, 0xF0)),
            BorderBrush = Frozen(ClavisColor),
            BorderThickness = new Thickness(1.5),
            Child = label
        };

        _window = new Window
        {
            Width = PreviewWidth,
            Height = PreviewHeight,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false, // never steal activation/focus from the in-progress drag
            Topmost = true,
            IsHitTestVisible = false,
            Focusable = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = frame
        };
        return _window;
    }

    /// Show the preview centred near the cursor. screenPoint is in physical pixels (from the OS cursor
    /// query); map it to device-independent units using a live window's DPI before positioning.
    public void ShowAt(Point screenPoint, Window dpiReference)
    {
        var window = Ensure();
        var source = PresentationSource.FromVisual(dpiReference);
        var point = source is null
            ? screenPoint
            : source.CompositionTarget.TransformFromDevice.Transform(screenPoint);

        window.Left = point.X - 40;
        window.Top = point.Y - 14;
        if (!window.IsVisible)
        {
            window.Show();
        }
    }

    public void Hide() => _window?.Hide();

    public void Close()
    {
        _window?.Close();
        _window = null;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
