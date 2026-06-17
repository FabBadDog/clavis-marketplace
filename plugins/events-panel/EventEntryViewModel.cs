using FabioSoft.Nucleus.Contracts;
using FabioSoft.Clavis.Rendering;

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

    // The severity badge for the shared BadgeTemplate: the level word plus its palette resource key.
    public BadgeViewModel LevelBadge => new(LevelLabel, entry.Level switch
    {
        LogLevel.Trace => "LevelTraceBrush",
        LogLevel.Debug => "LevelDebugBrush",
        LogLevel.Info => "LevelInfoBrush",
        LogLevel.Warn => "LevelWarnBrush",
        LogLevel.Error => "LevelErrorBrush",
        _ => "TextBrush"
    });

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
