namespace FabioSoft.Nucleus.Plugins.UsageLimits;

public sealed record UsageLimitsConfig
{
    // How often the glyph and panel re-evaluate the countdown and over/under standing against the wall
    // clock between reports. The reported utilization only changes when a fresh AgentUsageReport arrives;
    // this tick keeps the "resets in" countdown and the time-elapsed axis moving.
    public int RefreshSeconds { get; init; } = 30;
}
