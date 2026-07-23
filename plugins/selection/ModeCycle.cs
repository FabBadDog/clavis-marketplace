using System;
using System.Collections.Generic;

namespace FabioSoft.Nucleus.Plugins.Selection;

/// The pure next-mode arithmetic behind the Shift+Tab cycle: given the mode catalog (in the order the
/// provider offers it) and the current mode, return the one that follows, wrapping at the end. Kept
/// separate from the plugin so it is unit-tested without the bus or the dispatcher (public because the
/// test suite is a separate assembly and the codebase forbids InternalsVisibleTo).
public static class ModeCycle
{
    /// The id of the mode after <paramref name="currentId"/>, wrapping to the first at the end. An unknown
    /// current id advances to the first mode; an empty catalog returns null (nothing to switch to).
    public static string? Next(IReadOnlyList<string> modeIds, string currentId)
    {
        if (modeIds.Count == 0)
        {
            return null;
        }

        var index = -1;
        for (var i = 0; i < modeIds.Count; i++)
        {
            if (string.Equals(modeIds[i], currentId, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        return modeIds[(index + 1) % modeIds.Count];
    }
}
