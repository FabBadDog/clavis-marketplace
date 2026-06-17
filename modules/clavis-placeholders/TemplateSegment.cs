namespace FabioSoft.Clavis.Placeholders;

/// A parsed template is a flat list of these. Literal runs are kept verbatim; the two token kinds capture
/// what the grammar distinguishes (a value vs. a component). Raw keeps the original "{...}" text so an
/// unknown token can be rendered verbatim.
public abstract record TemplateSegment;

public sealed record LiteralSegment(string Text) : TemplateSegment;

/// `{key}` or `{key:format}` - e.g. `{git.branch}`, `{agent.name:uppercase}`, `{time.now:HH:mm}`.
public sealed record ValueSegment(string Raw, string Key, string? Format) : TemplateSegment;

/// `{component}`, `{component:value}`, `{component(arg):value}` or `{component(arg):value:format}` -
/// e.g. `{bar:agent.contextPercent}`, `{badge:time.now:HH:mm}`, `{microstat(arrow-up):turn.runtime}`.
public sealed record ComponentSegment(
    string Raw, string Component, string? Arg, string? ValueKey, string? ValueFormat) : TemplateSegment;

/// The result of resolving a template against a value snapshot. Text is ready to display; Component carries
/// the resolved value (string + parsed number when numeric) for the WPF layer to turn into a control.
/// Unresolved marks a value token whose key was absent from the snapshot, so a consumer can choose between
/// rendering it verbatim (authoring feedback in the template editor) and hiding it (the status line while
/// provider plugins are still coming up).
public abstract record ResolvedSegment;

/// IsValue distinguishes a resolved value token (e.g. `{cwd.short}` -> "~\Repos\FS\clavis") from a verbatim
/// literal run (e.g. "ctx " or "/"). Key is the value token's source key when IsValue (null for literals),
/// so a responsive consumer can tell removable chrome literals from values worth keeping, and recognise a
/// path value to shorten it.
public sealed record ResolvedText(string Text, bool Unresolved = false, bool IsValue = false, string? Key = null)
    : ResolvedSegment;

public sealed record ResolvedComponent(
    string Component, string? Arg, string Value, double? Number, bool Unresolved = false) : ResolvedSegment;
