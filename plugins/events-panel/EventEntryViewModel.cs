using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.EventsPanel;

public sealed class EventEntryViewModel(EventEntry entry, DateTime? sessionStart)
{
    public string DeltaText => sessionStart.HasValue
        ? EventEntryFactory.FormatDelta(sessionStart.Value, entry.Timestamp)
        : "+ 0ms";

    public string CategoryLabel => entry.Category switch
    {
        EventCategory.Output => "OUTPUT",
        EventCategory.Input => "INPUT",
        EventCategory.Error => "ERROR",
        _ => ""
    };

    public EventCategory Category => entry.Category;

    // The log severity, exposed so the row template colours the badge by level (matching the filter palette).
    public LogLevel Level => entry.Level;

    public string LevelLabel => entry.Level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERROR",
        _ => ""
    };

    public string SourceLabel => entry.Source;

    public IReadOnlyList<TextSegment> Segments => entry.Segments;

    public IReadOnlyList<ContinuationLine> ContinuationLines => entry.ContinuationLines;

    public bool HasContinuations => entry.ContinuationLines.Count > 0;

    public string SummaryText => string.Join("", entry.Segments.Select(s => s switch
    {
        LabelSegment l => l.Text,
        ValueSegment v => v.Text,
        SecondarySegment sec => sec.Text,
        ErrorLabelSegment e => e.Text,
        _ => ""
    }));
}
