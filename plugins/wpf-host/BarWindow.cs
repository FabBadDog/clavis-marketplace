using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// The chromeless strip across the top of the screen. The **host** owns it, not the Workspaces plugin: the host
/// already owns every HWND, the summon/hide flow, `Application.Current.MainWindow`, the maximize constraint and
/// the physical-to-DIP conversion, and a second plugin minting a top-level window would fork window ownership.
/// So the host owns the window and defines one region, `workspace-bar`; Workspaces contributes the strip into
/// it and the host stays free of workspace vocabulary.
///
/// Deliberately **not** a `WindowHost`: it is not in the window ring, never takes focus, has no docking surface,
/// no panels and no layout. It is also never `Application.Current.MainWindow` - MainWindow is load-bearing for
/// popup placement (`SelectorWindow` centres on it, `ConfirmDialog` uses CenterOwner), so a 30px strip as
/// MainWindow would jam the command palette and every picker against the top edge, sized against the bar. And it
/// is never `Owner`-linked to the primary, because owned windows hide and minimize with their owner - the bar
/// must survive `Ctrl+Shift+V` hiding everything else, which is the whole point of it.
[ExcludeFromCodeCoverage] // a bare WPF window; the placement arithmetic is BarPlacement
internal sealed class BarWindow
{
    public const string RegionId = "workspace-bar";

    private readonly ContentPresenter _content = new();

    public BarWindow(double height)
    {
        Height = height;

        Window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.Manual,
            IsHitTestVisible = true,
            Focusable = false,
            Owner = null,
            Background = Brushes.Transparent,
            Height = height
        };

        // The strip is the only thing painting this window, and the window allows transparency - so a background
        // that does not resolve leaves the desktop showing through wherever there is no tab. `PanelDeepBrush` was
        // such a key: it exists in neither the theme file nor the XAML fallback, and SetResourceReference resolves
        // at runtime, so nothing reported it.
        var strip = new Border { Child = _content };
        strip.SetResourceReference(Border.BackgroundProperty, "BlackBrush");
        strip.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        strip.BorderThickness = new Thickness(0, 0, 0, 1);

        Window.Content = strip;

        // Clicking the bar must never move the caret out of the prompt; ShowActivated alone does not cover a
        // click, so the window is made non-activatable at the Win32 level.
        NoActivateWindow.Apply(Window);

        Regions = new RegionManager();
        Regions.DefineRegion(RegionId, _content);
    }

    public Window Window { get; }

    public RegionManager Regions { get; }

    public double Height { get; }

    /// Position the bar across the top of a monitor's work area.
    public void PlaceOn(ScreenRectangle workArea, double dpiFactor)
    {
        var rect = BarPlacement.Compute(workArea, Height, dpiFactor);
        Window.Left = rect.Left;
        Window.Top = rect.Top;
        Window.Width = rect.Width;
        Window.Height = rect.Height;
    }

    /// Re-assert topmost. `Summon` kicks the primary's z-order with `Topmost = true; Topmost = false;`, which can
    /// momentarily lift it above the bar, so the bar reclaims the top afterwards.
    public void ReassertTopmost()
    {
        Window.Topmost = false;
        Window.Topmost = true;
    }
}
