using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioSoft.Nucleus.Plugins.Selection;

/// Which session's capability snapshot the pickers should offer. Capabilities used to be a single
/// last-writer-wins field, which cannot survive more than one session: with two workspaces open the model
/// picker would show whichever session happened to report most recently, and a pick would be applied to it
/// rather than to the one you are looking at.
///
/// Pure, so the fallback ladder is testable without a bus.
public static class SessionCapabilities
{
    /// The snapshot to offer, or null when there is nothing to show yet.
    ///
    /// The visible session wins. Failing that - no visible session announced yet, or its capabilities have not
    /// arrived - a sole snapshot is used, which keeps the single-workspace case behaving exactly as it did
    /// before sessions were distinguished. With several snapshots and no visible session there is no honest
    /// answer, so nothing is offered rather than an arbitrary one.
    public static AgentCapabilities? Resolve(
        IReadOnlyDictionary<Guid, AgentCapabilities> bySession, Guid visibleSessionId)
    {
        if (visibleSessionId != Guid.Empty && bySession.TryGetValue(visibleSessionId, out var visible))
        {
            return visible;
        }

        return bySession.Count == 1 ? bySession.Values.First() : null;
    }
}
