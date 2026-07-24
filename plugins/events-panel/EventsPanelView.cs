using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.EventsPanel;

/// Builds the events panel and wires keyboard-first interaction. There is no search box: typing anywhere
/// in the panel drives the search (shown in the footer next to the X-of-Y count), Backspace deletes, and
/// Esc clears it (then hands focus back to the prompt). The severity floor is a shared SegmentedSelector
/// navigated with Left/Right; Ctrl+Up/Down scroll the (selection-free) list, which sticks to the newest
/// entry unless the user scrolls up.
[ExcludeFromCodeCoverage] // WPF view construction and wiring
internal static class EventsPanelView
{
    // How many rows Ctrl+Up/Down moves (the list scrolls in item units while virtualizing).
    private const double ScrollStep = 3;

    public static FrameworkElement Create(EventsPanelViewModel viewModel, IBus bus, PanelInstanceContext context)
    {
        // Seed the filter from the saved blob before wiring change-persistence, so restoring does not echo
        // straight back as a save.
        viewModel.RestoreState(context.SavedState);

        var list = CreateEntriesList();
        var scroller = new EntriesScroller(list);

        var dockPanel = new DockPanel { LastChildFill = true };

        var severityBar = CreateSeverityBar(viewModel);
        DockPanel.SetDock(severityBar, Dock.Top);
        dockPanel.Children.Add(severityBar);

        var footer = CreateFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        dockPanel.Children.Add(footer);

        dockPanel.Children.Add(list);

        var border = new Border
        {
            // A dockable panel fills its tile. Focusable (and not a text input) so the host's PreviewKeyDown
            // resolver still owns Left/Right and Ctrl+Up/Down, while typed characters reach PreviewTextInput.
            Focusable = true,
            ClipToBounds = true,
            // Tags this view's focus subtree as the "events" panel kind so the host resolves panel-scoped
            // key bindings against it.
            Tag = "events",
            Child = dockPanel
        };
        border.SetResourceReference(Border.BackgroundProperty, "BlackBrush");
        border.DataContext = viewModel;

        WireKeyboard(border, viewModel, bus);
        WirePanelCommands(border, viewModel, bus, scroller);
        WireStatePersistence(border, viewModel, context);

        return border;
    }

    // Persist the filter (severity floor + search) through the panel's per-instance state whenever it
    // changes. Subscribed across the view's loaded lifetime so a reparent (docking move) keeps it wired and
    // a torn-down view stops writing.
    private static void WireStatePersistence(Border root, EventsPanelViewModel viewModel, PanelInstanceContext context)
    {
        Action? handler = null;

        root.Loaded += (_, _) => handler ??= Subscribe();
        root.Unloaded += (_, _) =>
        {
            if (handler is not null)
            {
                viewModel.FilterChanged -= handler;
                handler = null;
            }
        };

        Action Subscribe()
        {
            void OnFilterChanged() => context.OnStateChanged.Invoke(viewModel.CaptureState());
            viewModel.FilterChanged += OnFilterChanged;
            return OnFilterChanged;
        }
    }

    // Type-to-search plus the intrinsic, non-remappable keys (Backspace edits the search, Esc clears it or
    // hands focus back to the prompt). The remappable commands (severity, scroll) flow through the keymap.
    private static void WireKeyboard(Border root, EventsPanelViewModel viewModel, IBus bus)
    {
        // The root holds keyboard focus so typing searches; a click anywhere in the panel returns focus to
        // it (the list and rows are not focusable, so a click would otherwise leave focus nowhere useful).
        root.PreviewMouseDown += (_, _) =>
        {
            if (!root.IsKeyboardFocusWithin)
            {
                root.Focus();
            }
        };

        root.PreviewTextInput += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Text))
            {
                viewModel.AppendSearch(e.Text);
                e.Handled = true;
            }
        };

        root.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Back:
                    viewModel.Backspace();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (viewModel.IsSearchActive)
                    {
                        viewModel.SetSearch("");
                    }
                    else
                    {
                        bus.Send(new FocusInputRequested());
                    }

                    e.Handled = true;
                    break;
            }
        };
    }

    // Executes the panel-scoped commands the host resolved against the focused events panel. Subscribed only
    // while the view is loaded so the firehose of bus messages it ignores stops when the panel is torn down.
    private static void WirePanelCommands(Border root, EventsPanelViewModel viewModel, IBus bus, EntriesScroller scroller)
    {
        ISubscription? subscription = null;

        void Dispatch(string command)
        {
            switch (command)
            {
                case "events.severity.left": viewModel.SeverityModel.MoveSelection(-1); break;
                case "events.severity.right": viewModel.SeverityModel.MoveSelection(1); break;
                case "events.scroll.up": scroller.ScrollByItems(-ScrollStep); break;
                case "events.scroll.down": scroller.ScrollByItems(ScrollStep); break;
            }
        }

        root.Loaded += (_, _) => subscription ??= bus.Subscribe<RunPanelCommand>(message =>
        {
            if (message.Command.StartsWith("events.", StringComparison.Ordinal))
            {
                Application.Current?.Dispatcher.InvokeAsync(() => Dispatch(message.Command));
            }

            return Task.CompletedTask;
        });

        root.Unloaded += (_, _) =>
        {
            subscription?.Dispose();
            subscription = null;
        };
    }

    private static Border CreateSeverityBar(EventsPanelViewModel viewModel)
    {
        var selector = new SegmentedSelector
        {
            DataContext = viewModel.SeverityModel,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        var border = new Border
        {
            Padding = new Thickness(16, 7, 12, 7),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = selector
        };
        border.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        return border;
    }

    private static Border CreateFooter()
    {
        var count = new TextBlock { FontSize = 8.5, VerticalAlignment = VerticalAlignment.Center };
        count.SetResourceReference(TextBlock.FontFamilyProperty, "AgentFont");
        count.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        count.SetBinding(TextBlock.TextProperty, new Binding(nameof(EventsPanelViewModel.CounterLabel)));
        DockPanel.SetDock(count, Dock.Right);

        var search = new TextBlock
        {
            FontSize = 9,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 12, 0)
        };
        search.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        search.SetBinding(TextBlock.TextProperty, new Binding(nameof(EventsPanelViewModel.SearchLabel)));
        search.Style = SearchLabelStyle();

        var layout = new DockPanel { LastChildFill = true };
        layout.Children.Add(count);
        layout.Children.Add(search);

        var border = new Border
        {
            Padding = new Thickness(16, 5, 16, 5),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = layout
        };
        border.SetResourceReference(Border.BorderBrushProperty, "FaintBrush");
        return border;
    }

    // Dim while the search is empty (the "type to search" hint), clavis once the user is searching.
    private static Style SearchLabelStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("SecondaryBrush")));

        var active = new DataTrigger
        {
            Binding = new Binding(nameof(EventsPanelViewModel.IsSearchActive)),
            Value = true
        };
        active.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("ClavisBrush")));
        style.Triggers.Add(active);
        return style;
    }

    private static ListBox CreateEntriesList()
    {
        var list = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            // Focus stays on the panel root (so typing searches); the list is a passive, scrollable view.
            Focusable = false
        };
        list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        list.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        list.SetValue(VirtualizingStackPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(EventsPanelViewModel.FilteredEntryViewModels)));
        list.ItemContainerStyle = NonInteractiveItemStyle();
        return list;
    }

    // Rows just show their content - no selection, no hover chrome, no focus. A bare ContentPresenter
    // template strips the stock ListBoxItem selection/hover visuals entirely.
    private static Style NonInteractiveItemStyle()
    {
        var template = new ControlTemplate(typeof(ListBoxItem))
        {
            VisualTree = new FrameworkElementFactory(typeof(ContentPresenter))
        };

        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    // Owns the list's ScrollViewer: keeps it pinned to the newest row unless the user has scrolled up, and
    // scrolls it by the keymap's scroll commands. The ScrollViewer is resolved once the list is templated.
    private sealed class EntriesScroller
    {
        private readonly ListBox _list;
        private ScrollViewer? _scrollViewer;
        private bool _stickToBottom = true;

        public EntriesScroller(ListBox list)
        {
            _list = list;
            list.Loaded += (_, _) => Attach();
        }

        public void ScrollByItems(double delta)
        {
            if (_scrollViewer is null)
            {
                return;
            }

            var target = Math.Clamp(_scrollViewer.VerticalOffset + delta, 0, _scrollViewer.ScrollableHeight);
            _scrollViewer.ScrollToVerticalOffset(target);
        }

        private void Attach()
        {
            _scrollViewer ??= FindScrollViewer(_list);
            if (_scrollViewer is null)
            {
                return;
            }

            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.ScrollChanged += OnScrollChanged;

            // The panel opens with accumulated history already present and the newest events at the bottom,
            // so start scrolled all the way down (which also arms the stick-to-bottom auto-scroll). Deferred
            // to Loaded priority so the items have laid out and ExtentHeight is real.
            _scrollViewer.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => _scrollViewer?.ScrollToEnd()));
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_scrollViewer is null)
            {
                return;
            }

            // No content growth means a user scroll - re-arm sticking only when they are back at the bottom.
            if (e.ExtentHeightChange == 0)
            {
                _stickToBottom = _scrollViewer.VerticalOffset >= _scrollViewer.ScrollableHeight - 1.0;
            }
            else if (_stickToBottom)
            {
                _scrollViewer.ScrollToVerticalOffset(_scrollViewer.ExtentHeight);
            }
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer found)
            {
                return found;
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var result = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (result is not null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
