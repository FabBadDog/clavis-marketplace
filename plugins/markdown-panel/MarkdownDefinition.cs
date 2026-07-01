namespace FabioSoft.Nucleus.Plugins.MarkdownPanel;

/// One user-authored markdown panel definition: a stable id, a display title (shown on the panel's tab
/// and in the manager list), and a markdown body that may embed placeholder tokens like {git.branch}. The
/// body is the single source of truth for every open panel bound to this definition, so editing it updates
/// all of them.
public sealed record MarkdownDefinition(string Id, string Title, string Body);
