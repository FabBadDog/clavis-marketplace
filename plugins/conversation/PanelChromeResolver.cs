namespace FabioSoft.Nucleus.Plugins.Conversation;

/// The title-bar and status-bar templates the window chrome renders for one active panel kind.
public sealed record PanelChromeResolved(
    string TitleLeft, string TitleRight, string StatusLeft, string StatusCenter, string StatusRight);

/// Resolves which chrome templates a window shows for its active docked panel. The chat ("conversation")
/// shows its own configured title/status; every other panel shows its per-kind override, falling back to its
/// friendly name in the title and its registration's default status template. A panel that ships no default
/// status and has none configured resolves to an empty status bar (all three zones blank), which the host
/// collapses entirely so the panel fills the space. Pure, so it is unit-testable.
public static class PanelChromeResolver
{
    public const string ChatKind = "conversation";

    public static PanelChromeResolved Resolve(
        string activeKind, StatusLineTemplates chat, string friendlyName, string defaultStatus)
    {
        if (string.IsNullOrEmpty(activeKind) || activeKind == ChatKind)
        {
            return new PanelChromeResolved(
                chat.TitleLeft, chat.AgentCluster, chat.StatusLeft, chat.StatusCenter, chat.StatusRight);
        }

        chat.Panels.TryGetValue(activeKind, out var over);

        var titleLeft = !string.IsNullOrWhiteSpace(over?.TitleLeft) ? over!.TitleLeft : friendlyName;
        var titleRight = over?.TitleRight ?? "";

        var hasStatus =
            !string.IsNullOrWhiteSpace(over?.StatusLeft)
            || !string.IsNullOrWhiteSpace(over?.StatusCenter)
            || !string.IsNullOrWhiteSpace(over?.StatusRight);

        if (hasStatus)
        {
            return new PanelChromeResolved(
                titleLeft, titleRight, over!.StatusLeft, over.StatusCenter, over.StatusRight);
        }

        if (!string.IsNullOrWhiteSpace(defaultStatus))
        {
            return new PanelChromeResolved(titleLeft, titleRight, defaultStatus, "", "");
        }

        // No configured and no default status: an empty bar the host collapses so the panel fills the space.
        return new PanelChromeResolved(titleLeft, titleRight, "", "", "");
    }

    /// True when the resolved chrome has at least one non-empty status zone, so the host knows whether to
    /// show or collapse the window's status row for the active panel.
    public static bool HasStatusContent(PanelChromeResolved chrome) =>
        !string.IsNullOrWhiteSpace(chrome.StatusLeft)
        || !string.IsNullOrWhiteSpace(chrome.StatusCenter)
        || !string.IsNullOrWhiteSpace(chrome.StatusRight);
}
