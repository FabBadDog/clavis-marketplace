using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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

    public TurnViewModel(Turn state, Action<string, string> publishPermission)
    {
        _state = state;
        _publishPermission = publishPermission;
        SyncItems();
        SyncStats();
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
    public bool IsActive => _state.Status is Running;
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
