using System.Collections.Generic;

namespace FabioSoft.Nucleus.Plugins.Conversation;

/// Recall of previously submitted prompts, as a pure value: which prompts were sent, where in that list the
/// user currently is, and the draft they were typing before they started walking back. Extracted from the
/// prompt input's key handler so the navigation rules are testable without a TextBox.
///
/// Index -1 means "not recalling" - the box holds the live draft. Up from there stashes the draft and lands
/// on the newest entry; walking back down past the newest restores the stashed draft.
public sealed record PromptHistory(IReadOnlyList<string> Entries, int Index, string Draft)
{
    public static PromptHistory Empty { get; } = new([], -1, "");

    /// Record a submitted prompt and leave recall (so the next Up starts from the newest entry again).
    public PromptHistory Added(string entry) => new([.. Entries, entry], -1, "");

    /// Step one entry towards the oldest. Returns the text to show, or null when there is nothing to recall
    /// (an empty history) so the caller leaves the box alone.
    public (PromptHistory History, string? Text) Up(string current)
    {
        if (Entries.Count == 0)
        {
            return (this, null);
        }

        var (index, draft) = Index < 0
            ? (Entries.Count - 1, current)
            : (Index > 0 ? Index - 1 : Index, Draft);

        return (this with { Index = index, Draft = draft }, Entries[index]);
    }

    /// Step one entry towards the newest, falling out of recall past the end and restoring the stashed draft.
    /// Returns null while not recalling, so a plain Down in a fresh box does nothing.
    public (PromptHistory History, string? Text) Down()
    {
        if (Index < 0)
        {
            return (this, null);
        }

        var next = Index + 1;
        return next >= Entries.Count
            ? (this with { Index = -1 }, Draft)
            : (this with { Index = next }, Entries[next]);
    }
}
