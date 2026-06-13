using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.AgentGateway;

/// A bounded, thread-safe record of recent bus activity, fed from IBus.Activity. The bus already replays
/// a window to late subscribers, but the gateway keeps its own ring so the recent_activity tool can read
/// a snapshot without re-subscribing per call. Oldest entries fall off once Capacity is reached.
///
/// Public (not internal) only so it is unit-testable without InternalsVisibleTo, which the repo forbids.
public sealed class ActivityRing
{
    public readonly record struct Entry(DateTimeOffset Timestamp, string Type, string Source, string? Reason);

    private readonly int _capacity;
    private readonly Queue<Entry> _entries;
    private readonly Lock _gate = new();

    public ActivityRing(int capacity)
    {
        _capacity = capacity;
        _entries = new Queue<Entry>(capacity);
    }

    public void Record(BusActivity activity)
    {
        var entry = new Entry(
            activity.Metadata.Timestamp,
            activity.PayloadType.Name,
            activity.Metadata.Source ?? "",
            activity.Reason?.GetType().Name);

        lock (_gate)
        {
            if (_entries.Count >= _capacity)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(entry);
        }
    }

    /// The most recent entries (newest last), optionally filtered to those whose type or source contains
    /// the given text, capped at limit.
    public IReadOnlyList<Entry> Recent(string? contains, int limit)
    {
        lock (_gate)
        {
            IEnumerable<Entry> query = _entries;
            if (!string.IsNullOrEmpty(contains))
            {
                query = query.Where(entry =>
                    entry.Type.Contains(contains, StringComparison.OrdinalIgnoreCase)
                    || entry.Source.Contains(contains, StringComparison.OrdinalIgnoreCase));
            }

            return query.TakeLast(limit).ToList();
        }
    }
}
