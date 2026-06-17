using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.EventsPanel;

public sealed class EventsPanelPlugin : IPlugin<EventsPanelConfig>
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);

    private const string PanelKind = "events";
    private const double MinPanelWidth = 280;
    private const double MinPanelHeight = 180;

    public string Id => "EventsPanel";

    public EventsPanelConfig DefaultConfig => new();

    // The panel-scoped commands this panel executes, surfaced to the keymap so they can be bound, shown
    // in the help overlay, and listed in the shortcut-management panel. Default bindings ship from KeyMap.
    private static readonly IReadOnlyList<CommandDescriptor> PanelCommands =
    [
        Command("events.severity.left", "Lower the severity floor"),
        Command("events.severity.right", "Raise the severity floor"),
        Command("events.scroll.up", "Scroll up"),
        Command("events.scroll.down", "Scroll down")
    ];

    private static CommandDescriptor Command(string name, string description) =>
        new(name, name, "Panel", "events", description, true);

    public Task<ConfigValidationResult> ValidateConfigAsync(EventsPanelConfig config)
    {
        var errors = new List<string>();
        if (config.MaxEntries < 100)
        {
            errors.Add("MaxEntries must be at least 100");
        }

        return Task.FromResult<ConfigValidationResult>(
            errors.Count > 0 ? new ConfigInvalid(errors) : new ConfigValid());
    }

    public Task<IDisposable> ActivateAsync(IBus bus, EventsPanelConfig config)
    {
        // The bus delivers activity on the publishing thread; we batch through a queue and flush on a
        // dispatcher timer so the firehose never floods the UI thread one InvokeAsync at a time.
        var queue = new ConcurrentQueue<BusActivity>();
        EventsPanelViewModel? viewModel = null;
        DispatcherTimer? timer = null;

        if (Application.Current is not null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                viewModel = new EventsPanelViewModel { MaxEntries = config.MaxEntries };
                viewModel.SetSeverityFloor(config.DefaultMinLevel);

                Application.Current.Resources.MergedDictionaries.Add(LoadTemplates());

                timer = new DispatcherTimer { Interval = FlushInterval };
                timer.Tick += (_, _) => Drain(queue, viewModel);
                timer.Start();
            });
        }

        var activitySubscription = bus.Activity.Subscribe(new ActivityObserver(queue));

        // The events panel is a full dockable panel: it announces its kind so the host can place, move, and
        // close it like any other. The view binds to the single long-lived view model (one events panel per
        // window), so closing and re-opening keeps the accumulated history. The factory ignores its instance
        // context - the panel keeps no per-instance state.
        void Announce() =>
            bus.Send(new PanelKindRegistration(
                PanelKind, "Events", MinPanelWidth, MinPanelHeight, "", true,
                context => EventsPanelView.Create(viewModel!, bus, context)));

        var kindRequest = bus.Subscribe<PanelKindsRequested>(_ =>
        {
            Announce();
            return Task.CompletedTask;
        });

        Announce();

        // Register panel commands now and on request, so order relative to the command palette never matters.
        bus.Send(new PanelCommandsRegistered(PanelCommands));
        var panelCommandsRequest = bus.Subscribe<RequestPanelCommands>(_ =>
        {
            bus.Send(new PanelCommandsRegistered(PanelCommands));
            return Task.CompletedTask;
        });

        bus.LogInfo("EventsPanel", "Events panel plugin activated");

        return Task.FromResult<IDisposable>(
            new PluginDisposable(activitySubscription, panelCommandsRequest, kindRequest, timer));
    }

    private static void Drain(ConcurrentQueue<BusActivity> queue, EventsPanelViewModel? viewModel)
    {
        if (viewModel is null)
        {
            return;
        }

        while (queue.TryDequeue(out var activity))
        {
            viewModel.AddEntry(EventEntryFactory.FromBusActivity(activity));
        }
    }

    private static ResourceDictionary LoadTemplates()
    {
        var assemblyName = typeof(EventsPanelPlugin).Assembly.GetName().Name;
        var uri = new Uri($"pack://application:,,,/{assemblyName};component/Views/EventEntryRowTemplate.xaml");
        return new ResourceDictionary { Source = uri };
    }

    private sealed class ActivityObserver(ConcurrentQueue<BusActivity> queue) : IObserver<BusActivity>
    {
        public void OnNext(BusActivity value) => queue.Enqueue(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    private sealed class PluginDisposable(
        IDisposable activitySubscription,
        IDisposable panelCommandsSubscription,
        IDisposable kindSubscription,
        DispatcherTimer? timer) : IDisposable
    {
        public void Dispose()
        {
            try { activitySubscription.Dispose(); }
            catch { /* cleanup best-effort */ }

            try { panelCommandsSubscription.Dispose(); }
            catch { /* cleanup best-effort */ }

            try { kindSubscription.Dispose(); }
            catch { /* cleanup best-effort */ }

            if (timer is not null)
            {
                try { timer.Dispatcher.Invoke(timer.Stop); }
                catch { /* cleanup best-effort */ }
            }
        }
    }
}
