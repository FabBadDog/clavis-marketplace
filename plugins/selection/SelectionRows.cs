using System.Windows;

namespace FabioSoft.Nucleus.Plugins.Selection;

/// One row in the model selector: display data only - the internal id is carried for the accept
/// message but never rendered.
public sealed record ModelRow(string Id, string Name, string Version, string Context, string Description);

/// One row in the effort selector. ColorKey is the host theme brush key resolved from the provider's
/// neutral color hint; Brush resolves it against the live application resources for the row template.
public sealed record EffortRow(string Id, string Name, string Description, string ColorKey)
{
    public object? Brush => Application.Current?.TryFindResource(ColorKey);
}

public sealed record ModeRow(string Id, string Name, string Description);

public sealed record PanelRow(string Kind, string Title);

/// One row in an agent-driven selection (SelectionRequested): Value is returned on accept, Label/
/// Description are shown.
public sealed record OptionRow(string Value, string Label, string Description);

/// Pure projection of the provider's capability catalog (and the panel registry) onto selector rows,
/// plus the shared substring filter. Deterministic and unit-tested; the plugin shell owns the popups.
public static class SelectionRows
{
    public static IReadOnlyList<ModelRow> BuildModels(IEnumerable<AgentModelInfo> models) =>
        models
            .Select(model => new ModelRow(
                model.Id, model.DisplayName, model.Version, FormatContext(model.ContextSize), model.Description))
            .ToList();

    /// Only the effort levels the current model supports, in catalog order.
    public static IReadOnlyList<EffortRow> BuildEfforts(
        IEnumerable<AgentEffortInfo> efforts, IEnumerable<string> supportedIds)
    {
        var supported = supportedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return efforts
            .Where(effort => supported.Contains(effort.Id))
            .Select(effort => new EffortRow(
                effort.Id, effort.DisplayName, effort.Description, BrushKeyFor(effort.Color)))
            .ToList();
    }

    public static IReadOnlyList<ModeRow> BuildModes(IEnumerable<AgentModeInfo> modes) =>
        modes.Select(mode => new ModeRow(mode.Id, mode.DisplayName, mode.Description)).ToList();

    public static IReadOnlyList<PanelRow> BuildPanels(IEnumerable<KeyValuePair<string, string>> kinds) =>
        kinds
            .Select(pair => new PanelRow(pair.Key, pair.Value.Length > 0 ? pair.Value : pair.Key))
            .OrderBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<OptionRow> BuildOptions(IEnumerable<SelectionOption> options) =>
        options.Select(option => new OptionRow(option.Value, option.Label, option.Description)).ToList();

    /// Case-insensitive substring filter over each row's searchable fields; an empty filter passes all.
    /// Rows keep their build order - for models and efforts that order is meaningful (newest model first,
    /// lowest effort first).
    public static IReadOnlyList<object> Filter<TRow>(
        IReadOnlyList<TRow> rows, string filter, Func<TRow, IEnumerable<string>> searchableFields)
        where TRow : class
    {
        var needle = filter.Trim();
        if (needle.Length == 0)
        {
            return rows.Cast<object>().ToList();
        }

        return rows
            .Where(row => searchableFields(row)
                .Any(field => field.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            .Cast<object>()
            .ToList();
    }

    public static IEnumerable<string> SearchableFields(ModelRow row) =>
        [row.Name, row.Version, row.Context, row.Description];

    public static IEnumerable<string> SearchableFields(EffortRow row) => [row.Name, row.Description];

    public static IEnumerable<string> SearchableFields(ModeRow row) => [row.Name, row.Description];

    public static IEnumerable<string> SearchableFields(PanelRow row) => [row.Title, row.Kind];

    public static IEnumerable<string> SearchableFields(OptionRow row) => [row.Label, row.Description];

    /// A context window in tokens as the short display form ("200k", "1M"). Invariant culture, so the
    /// decimal separator never follows the OS locale.
    public static string FormatContext(int tokens)
    {
        if (tokens >= 1_000_000)
        {
            return tokens % 1_000_000 == 0
                ? $"{tokens / 1_000_000}M"
                : (tokens / 1_000_000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M";
        }

        return tokens >= 1_000 ? $"{tokens / 1_000}k" : tokens.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// Maps the provider's neutral color hint onto the host theme's brush key. Unknown hints read as
    /// plain text.
    public static string BrushKeyFor(string colorHint) => colorHint switch
    {
        "accent" => "ClavisBrush",
        "green" => "GreenBrush",
        "yellow" => "YellowBrush",
        "purple" => "HumanBrush",
        "red" => "RedBrush",
        "dim" => "TextDimBrush",
        _ => "TextBrush",
    };
}
