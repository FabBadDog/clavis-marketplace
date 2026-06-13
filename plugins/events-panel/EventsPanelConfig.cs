using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.EventsPanel;

public sealed record EventsPanelConfig
{
    public int MaxEntries { get; init; } = 10_000;

    // Default severity floor. Trace means "ALL" - nothing is hidden until the user raises the floor, so an
    // unfiltered panel shows every message (X of Y reads N of N).
    public LogLevel DefaultMinLevel { get; init; } = LogLevel.Trace;
}
