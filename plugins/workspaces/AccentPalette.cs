using System.Collections.Generic;
using System.Linq;

namespace FabioSoft.Nucleus.Plugins.Workspaces;

/// Which accent a new workspace gets. Accents are **theme resource keys**, never baked colours, so an accent
/// assigned once survives a restart and stays re-themable.
///
/// The palette is drawn from the design language's **identity** family (the periwinkle/violet/blue range), not
/// the signal colours: green, yellow and red mean something, and a workspace's accent means only "this is
/// which one". That is also why the accent and the activity dot are separate marks - tinting the dot with the
/// workspace accent would destroy the activity signal.
public static class AccentPalette
{
    /// Four keys, defined in the host theme. Four is deliberate: enough that neighbouring workspaces read as
    /// distinct, few enough that every one stays clearly inside the identity family.
    public static IReadOnlyList<string> Keys { get; } =
        ["Accent1Brush", "Accent2Brush", "Accent3Brush", "Accent4Brush"];

    /// The accent for a new workspace: the least-used key, earliest in the palette on a tie. Deterministic
    /// rather than random - the plan called for a random pick, but least-used guarantees no two workspaces
    /// collide until there are more than four of them, which is the outcome random assignment was reaching for,
    /// and it is testable.
    public static string Assign(IEnumerable<string> inUse)
    {
        var counts = Keys.ToDictionary(key => key, _ => 0);
        foreach (var key in inUse)
        {
            if (counts.ContainsKey(key))
            {
                counts[key]++;
            }
        }

        return Keys.OrderBy(key => counts[key]).First();
    }

    /// The next accent after the current one, wrapping - so an accent can be re-rolled by command without a
    /// picker (a per-workspace theme is deliberately not a colour chooser).
    public static string Next(string current)
    {
        var index = Keys.ToList().IndexOf(current);
        return index < 0 ? Keys[0] : Keys[(index + 1) % Keys.Count];
    }

    /// Read a persisted accent back, falling back to the first key so an unknown or empty value still renders
    /// as an accent rather than as nothing.
    public static string OrDefault(string? key) =>
        key is not null && Keys.Contains(key) ? key : Keys[0];
}
