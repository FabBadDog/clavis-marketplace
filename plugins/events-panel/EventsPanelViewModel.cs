using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.EventsPanel;

public sealed class EventsPanelViewModel : INotifyPropertyChanged
{
    // The persisted filter state: which severity floor and what is being searched. Restored across launches
    // through the panel's per-instance saved-state blob.
    private sealed record FilterState(int SeverityIndex, string Search);

    // The severity floor options. Index 0 ("ALL") and 1 ("TRACE") both floor at Trace - ALL means "all log
    // levels", which is why it leads and is the default. The trailing "MESSAGES" option is not a level: it
    // isolates the non-log bus firehose (see PassesCategory).
    private static readonly string[] SeverityLabels = ["ALL", "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "MESSAGES"];

    // The MESSAGES filter sits last, after the log-level ladder. Non-log bus messages show ONLY under it;
    // every other option (ALL and each level floor) shows only real logs.
    private const int MessageIndex = 6;

    private readonly List<EventEntry> _allEntries = [];
    private string _searchText = "";
    private DateTime? _sessionStart;

    public EventsPanelViewModel()
    {
        SeverityModel = new SegmentedSelectorModel(BuildSeverityItems());
        SeverityModel.SelectionChanged += (_, _) =>
        {
            ApplyFilter();
            OnPropertyChanged(nameof(SeverityLabel));
            FilterChanged?.Invoke();
        };
        SeverityModel.SelectedIndex = 0; // ALL: nothing is hidden until the user raises the floor.
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// Raised when the severity floor or search text changes - the panel persists its filter state on this.
    public event Action? FilterChanged;

    public ObservableCollection<EventEntryViewModel> FilteredEntryViewModels { get; } = [];

    /// The shared single-select control model driving the severity floor (ALL / TRACE / ... / ERROR).
    public SegmentedSelectorModel SeverityModel { get; }

    public int MaxEntries { get; set; } = 10_000;

    public string SearchText => _searchText;

    public bool IsSearchActive => _searchText.Length > 0;

    // The footer line: the live query once searching, a discoverability hint while empty (there is no
    // search box - typing anywhere searches).
    public string SearchLabel => IsSearchActive ? $"search: {_searchText}" : "type to search";

    public int TotalCount => _allEntries.Count;

    public string SeverityLabel => SeverityLabels[Math.Clamp(SeverityModel.SelectedIndex, 0, SeverityLabels.Length - 1)];

    public string SessionStartLabel => _sessionStart.HasValue
        ? $"started {_sessionStart.Value:yyyy-MM-dd HH:mm:ss}"
        : "";

    public string CounterLabel => $"{FilteredEntryViewModels.Count} of {_allEntries.Count}";

    // The minimum level shown: ALL and TRACE both floor at Trace (everything); the rest floor at their level.
    private LogLevel SeverityFloor =>
        SeverityModel.SelectedIndex <= 1 ? LogLevel.Trace : (LogLevel)(SeverityModel.SelectedIndex - 1);

    public void AddEntry(EventEntry entry)
    {
        if (!_sessionStart.HasValue)
        {
            _sessionStart = entry.Timestamp;
            OnPropertyChanged(nameof(SessionStartLabel));
        }

        _allEntries.Add(entry);

        if (Passes(entry))
        {
            FilteredEntryViewModels.Add(new EventEntryViewModel(entry, _sessionStart));
        }

        TrimToCapacity();
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CounterLabel));
    }

    /// Set the initial severity floor from config: a configured Trace floor shows as ALL (its equivalent).
    public void SetSeverityFloor(LogLevel level) =>
        SeverityModel.SelectedIndex = level == LogLevel.Trace ? 0 : (int)level + 1;

    /// Serialize the current filter (severity floor + search) for persistence.
    public string CaptureState() =>
        JsonSerializer.Serialize(new FilterState(SeverityModel.SelectedIndex, _searchText));

    /// Restore a previously persisted filter. A blank or malformed blob is ignored (the defaults stand).
    public void RestoreState(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var state = JsonSerializer.Deserialize<FilterState>(json);
            if (state is null)
            {
                return;
            }

            SeverityModel.SelectedIndex = state.SeverityIndex;
            SetSearch(state.Search ?? "");
        }
        catch (JsonException)
        {
            // A blob from an older/foreign format is discarded rather than failing the panel.
        }
    }

    public void SetSearch(string text)
    {
        var value = text ?? "";
        if (_searchText == value)
        {
            return;
        }

        _searchText = value;
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(IsSearchActive));
        OnPropertyChanged(nameof(SearchLabel));
        ApplyFilter();
        FilterChanged?.Invoke();
    }

    /// Append typed characters to the search (the panel drives search by typing, with no focusable input).
    public void AppendSearch(string text) => SetSearch(_searchText + (text ?? ""));

    /// Delete the last search character (Backspace).
    public void Backspace()
    {
        if (_searchText.Length > 0)
        {
            SetSearch(_searchText[..^1]);
        }
    }

    public void Clear()
    {
        _allEntries.Clear();
        FilteredEntryViewModels.Clear();
        _sessionStart = null;
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(SessionStartLabel));
        OnPropertyChanged(nameof(CounterLabel));
    }

    private void TrimToCapacity()
    {
        if (_allEntries.Count <= MaxEntries)
        {
            return;
        }

        var removed = _allEntries[0];
        _allEntries.RemoveAt(0);

        // Filtered entries preserve insertion order, so the trimmed entry - if shown - is the first row.
        if (Passes(removed) && FilteredEntryViewModels.Count > 0)
        {
            FilteredEntryViewModels.RemoveAt(0);
        }
    }

    private void ApplyFilter()
    {
        FilteredEntryViewModels.Clear();

        foreach (var entry in _allEntries)
        {
            if (Passes(entry))
            {
                FilteredEntryViewModels.Add(new EventEntryViewModel(entry, _sessionStart));
            }
        }

        OnPropertyChanged(nameof(CounterLabel));
    }

    private bool Passes(EventEntry entry)
    {
        if (!PassesCategory(entry))
        {
            return false;
        }

        return _searchText.Length == 0
            || Searchable(entry).Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    // The level/category gate (search is layered on top). MESSAGES shows only the non-log bus firehose; every
    // other option shows only real logs - ALL shows all levels, a specific level shows logs at or above that
    // floor - so a plain bus message never masquerades as a log or clutters a raised level floor.
    private bool PassesCategory(EventEntry entry)
    {
        var index = SeverityModel.SelectedIndex;
        if (index == MessageIndex)
        {
            return !entry.IsLog;
        }

        if (!entry.IsLog)
        {
            return false; // non-log messages appear only under the MESSAGES filter
        }

        if (index <= 0)
        {
            return true; // ALL log levels
        }

        return entry.Level >= SeverityFloor;
    }

    // Search matches against the whole row: its source, category, level, summary, and every continuation
    // line - so typing a source name, a category ("error"), or a level ("warn") filters by it, with no
    // separate badges or chips.
    private static string Searchable(EventEntry entry)
    {
        var parts = new List<string>
        {
            entry.Source,
            CategoryName(entry.Category),
            LevelName(entry.Level),
            SummaryOf(entry.Segments)
        };

        foreach (var line in entry.ContinuationLines)
        {
            parts.Add(line.Label);
            parts.Add(SummaryOf(line.Segments));
        }

        return string.Join(" ", parts);
    }

    private static string CategoryName(EventCategory category) => category switch
    {
        EventCategory.Output => "output",
        EventCategory.Input => "input",
        EventCategory.Error => "error",
        _ => ""
    };

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Info => "info",
        LogLevel.Warn => "warn",
        LogLevel.Error => "error",
        _ => ""
    };

    private static string SummaryOf(IReadOnlyList<TextSegment> segments) => string.Join("", segments.Select(segment => segment switch
    {
        LabelSegment l => l.Text,
        ValueSegment v => v.Text,
        SecondarySegment s => s.Text,
        ErrorLabelSegment e => e.Text,
        _ => ""
    }));

    private static IReadOnlyList<SegmentItem> BuildSeverityItems() =>
    [
        new SegmentItem("ALL", "TextBrightBrush"),
        new SegmentItem("TRACE", "LevelTraceBrush"),
        new SegmentItem("DEBUG", "LevelDebugBrush"),
        new SegmentItem("INFO", "LevelInfoBrush"),
        new SegmentItem("WARN", "LevelWarnBrush"),
        new SegmentItem("ERROR", "LevelErrorBrush"),
        new SegmentItem("MESSAGES", "MessageBrush")
    ];

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
