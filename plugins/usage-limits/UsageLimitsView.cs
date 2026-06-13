using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using FabioSoft.Contracts.Session;
using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.UsageLimits;

/// Owns the live limits-plane views (the status-bar glyph and any open detail panels), maps the neutral
/// AgentLimitWindow contract onto the renderer's LimitWindow, and fans the latest report out to every
/// view on the UI thread. The plotted values come from the unit-tested LimitWindow module, so the WPF
/// wiring here is excluded from coverage.
[ExcludeFromCodeCoverage]
internal sealed class UsageIndicator : IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<LimitsPlaneView> _views = [];
    private LimitWindow[] _windows = [];
    private DispatcherTimer? _timer;

    // The plane carries no meaning before the first usage report arrives (no dots to plot), so views start
    // hidden and fade in once windows are known. Touched only on the dispatcher thread.
    private bool _revealed;

    public void SetReport(IReadOnlyList<AgentLimitWindow> windows)
    {
        var mapped = windows
            .Select(window => new LimitWindow(
                window.Name, window.Used, window.Total, window.Unit, window.WindowStart, window.ResetsAt))
            .ToArray();

        lock (_gate)
        {
            _windows = mapped;
        }

        Dispatch(() =>
        {
            PushAll();
            RevealIfReady();
        });
    }

    public FrameworkElement CreateGlyph(Action onClick)
    {
        var view = LimitsPlane.CreateGlyph();
        Register(view);

        var host = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = view.Element,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Usage limits - click for detail"
        };
        host.MouseLeftButtonUp += (_, _) => onClick();
        return host;
    }

    public FrameworkElement CreatePanel()
    {
        var view = LimitsPlane.CreatePanel();
        Register(view);
        return view.Element;
    }

    public void StartRefreshTimer(int seconds)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        // Re-pushing the same windows redraws against the current time, advancing the countdown and the
        // time-elapsed axis between reports. A failed redraw is cosmetic, so a bad tick is traced and
        // skipped rather than escalating to the dispatcher's fatal handler.
        _timer.Tick += (_, _) =>
        {
            try
            {
                PushAll();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceWarning($"UsageLimits refresh failed: {exception.Message}");
            }
        };
        _timer.Start();
    }

    public void Dispose() => Dispatch(() => _timer?.Stop());

    private void Register(LimitsPlaneView view)
    {
        LimitWindow[] current;
        lock (_gate)
        {
            _views.Add(view);
            current = _windows;
        }

        // A view created after the first report (e.g. opening the panel later) already has data, so show it
        // straight away; one created before sits invisible until RevealIfReady fades the set in together.
        view.Element.Opacity = _revealed ? 1.0 : 0.0;
        view.Update(current);
    }

    private void RevealIfReady()
    {
        if (_revealed)
        {
            return;
        }

        LimitWindow[] current;
        LimitsPlaneView[] views;
        lock (_gate)
        {
            current = _windows;
            views = [.. _views];
        }

        if (current.Length == 0)
        {
            return;
        }

        _revealed = true;
        foreach (var view in views)
        {
            Motion.fadeTo(view.Element, 1.0);
        }
    }

    private void PushAll()
    {
        LimitWindow[] current;
        LimitsPlaneView[] views;
        lock (_gate)
        {
            current = _windows;
            views = [.. _views];
        }

        foreach (var view in views)
        {
            view.Update(current);
        }
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
