using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using FabioSoft.Clavis.Placeholders;
using FabioSoft.Common;

namespace FabioSoft.Nucleus.Plugins.Conversation.ViewModels;

public sealed class TurnViewModel : ObservableObject
{
    private static readonly PlaceholderEngine Engine = new();

    // The configurable stats-column template (set from the StatusLine config). Static because it is the same
    // for every turn; each turn resolves it against its own turn.* values.
    public static string StatsTemplate { get; set; } = StatusLineTemplates.DefaultStatsColumn;

    private          Turn                   _state;
    private readonly Action<string, string> _publishPermission;
    private          bool                   _ignited;

    public TurnViewModel(Turn state, Action<string, string> publishPermission)
    {
        _state = state;
        _publishPermission = publishPermission;
        SyncItems();
        SyncStats();
        ArmRailCharge();
    }

    // The rail-charge animations (the streak racing up the rail, the dot ignite flare, the breathing pulse)
    // are driven off IsActive's false->true transition via the template's DataTrigger EnterActions. A prompt
    // sent to an idle agent is born Running, so IsActive would already be true the instant the row
    // materializes - there is no transition for the triggers to catch and the charge silently never plays
    // (it only fired in the rarer case of a prompt queued while busy, which transitions Queued->Running after
    // the row exists). Hold IsActive false until the next dispatcher beat - DispatcherPriority.Loaded, after
    // the row has been generated, bound, and laid out - then flip it true so the live row sees a genuine
    // transition and the charge fires every time. The init turn is not a fresh send, so it stays active
    // immediately and keeps its current look (no streak).
    private void ArmRailCharge()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || _state.Kind == TurnKind.InitTurn)
        {
            _ignited = true;
            return;
        }

        dispatcher.InvokeAsync(
            () =>
            {
                _ignited = true;
                OnPropertyChanged(nameof(IsActive));
            },
            DispatcherPriority.Loaded);
    }

    public void Update(Turn state)
    {
        _state = state;
        SyncItems();
        SyncStats();
        RefreshAll();
    }

    public ObservableCollection<MicroStatViewModel> Stats { get; } = [];

    // Resolve the stats template against this turn's turn.* values and reconcile into Stats. When the icon
    // set is unchanged (the steady-state across ticks), values are updated in place; otherwise rebuilt.
    private void SyncStats()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["turn.runtime"] = Formatting.duration(_state.Duration),
            ["turn.tokens"] = ShowTokens ? Formatting.tokens(_state.TotalTokens) : "",
            ["turn.status"] = _state.Status.GetType().Name,
            ["turn.tools"] = ToolCount() > 0 ? ToolCount().ToString() : ""
        };

        var entries = Engine.Resolve(StatsTemplate, values)
            .OfType<ResolvedComponent>()
            .Where(component => string.Equals(component.Component, "microstat", StringComparison.OrdinalIgnoreCase)
                                && component.Value.Length > 0)
            .Select(component => (Icon: component.Arg ?? "", component.Value))
            .ToList();

        var sameShape = Stats.Count == entries.Count
            && entries.Select((entry, index) => Stats[index].Icon == entry.Icon).All(matches => matches);

        if (sameShape)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                Stats[index].Value = entries[index].Value;
            }
            return;
        }

        Stats.Clear();
        foreach (var entry in entries)
        {
            Stats.Add(new MicroStatViewModel(entry.Icon, entry.Value));
        }
    }

    private int ToolCount() => _state.Items.OfType<ToolItem>().Count();

    private void SyncItems() =>
                    CollectionSync.Reconcile(
                        Items,
                        _state.Items,
                        CollectionSync.GetItemKey,
                        CollectionSync.GetItemViewModelKey,
                        item => CollectionSync.CreateItemViewModel(item, _publishPermission),
                        CollectionSync.UpdateItemViewModel);

    public Guid TurnId => _state.Id;
    public TurnKind Kind => _state.Kind;
    public string Prompt => _state.Prompt;
    public bool IsActive => _ignited && _state.Status is Running;
    public bool IsQueued => _state.Status is Queued;
    public string DurationText => Formatting.duration(_state.Duration);
    public string TokensText => Formatting.tokens(_state.TotalTokens);

    // The initialization section consumes no tokens, so its token stat is meaningless - hide it there.
    public bool ShowTokens => _state.Kind != TurnKind.InitTurn;
    public string StatusText => _state.StatusText;
    public string Response => _state.Response;
    public bool HasResponse => _state.Response != "";

    public bool HasError => _state.Status is Failed;

    public string ErrorText => _state.Status is Failed f ? f.ErrorMessage : "";

    public bool ShowDoneIcon => _state.Status is Succeeded or Failed or Aborted;

    public string DoneIconText => _state.Status switch
    {
        Succeeded => "✓",
        Failed => "!",
        Aborted => "×",
        _ => ""
    };

    public bool IsSucceeded => _state.Status is Succeeded;

    public bool IsAborted => _state.Status is Aborted;

    public ObservableCollection<INotifyPropertyChanged> Items { get; } = [];
}
