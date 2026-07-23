using System;
using System.Collections.Generic;
using System.Linq;

using FabioSoft.Contracts.Session;

namespace FabioSoft.Nucleus.Plugins.ClaudeBridge;

/// The provider catalog behind the agent facade: every model Claude Code can run (including older
/// generations), the reasoning-effort levels, and the operation modes - each with the display data the
/// selection UI shows instead of internal names. Pure and deterministic; the plugin shell publishes it
/// through AgentCapabilities so no UI plugin ever names Claude.
public static class ClaudeCatalog
{
    public const string DefaultModeId = "default";

    private const int StandardContext = 200_000;
    private const int ExtendedContext = 1_000_000;

    private static readonly IReadOnlyList<string> NoEfforts = [];
    private static readonly IReadOnlyList<string> LegacyEfforts = ["low", "medium", "high"];
    private static readonly IReadOnlyList<string> FullEfforts = ["low", "medium", "high", "xhigh", "max"];
    private static readonly IReadOnlyList<string> FrontierEfforts =
        ["low", "medium", "high", "xhigh", "max", "ultracode"];

    /// Every reasoning-effort level the provider knows. Which of them apply to a given model is carried
    /// per model (AgentModelInfo.SupportedEfforts); this list defines display name, description, and the
    /// neutral color hint.
    public static IReadOnlyList<AgentEffortInfo> Efforts { get; } =
    [
        new("low", "Low", "Fastest responses with shallow reasoning", "dim"),
        new("medium", "Medium", "Balanced speed and reasoning depth", "green"),
        new("high", "High", "Deep reasoning for most work (default)", "yellow"),
        new("xhigh", "Extra High", "Very deep reasoning for hard problems", "accent"),
        new("max", "Max", "Unbounded reasoning depth; slowest and costliest", "purple"),
        new("ultracode", "Ultracode", "Extra-high effort plus standing multi-agent workflow orchestration", "red"),
    ];

    // Listed in the order Shift+Tab cycles through them: Plan -> None -> Auto -> Edit -> back to Plan. The
    // resting "None" mode sits next to Plan so the cycle reads naturally, and its indicator shows no accent.
    public static IReadOnlyList<AgentModeInfo> Modes { get; } =
    [
        new("plan", "Plan", "Plan first - read-only until the plan is approved"),
        new("default", "None", "Every privileged action asks for permission"),
        new("auto", "Auto", "Edits and commands run without asking"),
        new("acceptEdits", "Edit", "File edits are auto-approved; commands still ask"),
    ];

    public static IReadOnlyList<AgentModelInfo> Models { get; } =
    [
        new("claude-fable-5", "Fable 5", "5.0", StandardContext,
            "Current frontier model: strongest coding and agentic performance", FrontierEfforts),
        new("claude-fable-5[1m]", "Fable 5 (1M)", "5.0", ExtendedContext,
            "Fable 5 with the extended one-million-token context window", FrontierEfforts),
        new("claude-opus-4-8", "Opus 4.8", "4.8", StandardContext,
            "Most capable Opus: deep reasoning and long-horizon agentic work", FullEfforts),
        new("claude-opus-4-7", "Opus 4.7", "4.7", StandardContext,
            "Previous Opus generation; strong reasoning", FullEfforts),
        new("claude-opus-4-6", "Opus 4.6", "4.6", StandardContext,
            "Older Opus generation, kept available for compatibility", FullEfforts),
        new("claude-opus-4-6[1m]", "Opus 4.6 (1M)", "4.6", ExtendedContext,
            "Opus 4.6 with the extended one-million-token context window", FullEfforts),
        new("claude-opus-4-5", "Opus 4.5", "4.5", StandardContext,
            "First effort-aware Opus generation", LegacyEfforts),
        new("claude-sonnet-4-6", "Sonnet 4.6", "4.6", StandardContext,
            "Fast general-purpose model balancing capability and cost", NoEfforts),
        new("claude-sonnet-4-5", "Sonnet 4.5", "4.5", StandardContext,
            "Previous Sonnet generation", NoEfforts),
        new("claude-haiku-4-5", "Haiku 4.5", "4.5", StandardContext,
            "Near-instant lightweight model for simple tasks", NoEfforts),
        new("claude-haiku-4-4", "Haiku 4.4", "4.4", StandardContext,
            "Older lightweight model, kept available for compatibility", NoEfforts),
    ];

    /// Resolve a model string the provider reported (which may carry a date suffix, e.g.
    /// "claude-opus-4-8-20260115", or be a bare alias like "opus") to a catalog entry. Exact id first,
    /// then the longest catalog id the reported value starts with, then an alias match on the family name
    /// of the newest generation. Null when nothing matches.
    public static AgentModelInfo? ResolveModel(string reported)
    {
        if (string.IsNullOrWhiteSpace(reported))
        {
            return null;
        }

        var exact = Models.FirstOrDefault(model => string.Equals(model.Id, reported, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var byPrefix = Models
            .Where(model => reported.StartsWith(model.Id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(model => model.Id.Length)
            .FirstOrDefault();
        if (byPrefix is not null)
        {
            return byPrefix;
        }

        // Bare aliases ("opus", "sonnet", "haiku", "fable") resolve to the newest catalog entry whose id
        // contains the family token. Catalog order is newest-first per family, so the first hit wins.
        return Models.FirstOrDefault(model =>
            model.Id.Contains($"-{reported}-", StringComparison.OrdinalIgnoreCase)
            || model.Id.Contains($"-{reported}", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsKnownModel(string id) =>
        Models.Any(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase));

    public static bool IsKnownMode(string id) =>
        Modes.Any(mode => string.Equals(mode.Id, id, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> SupportedEffortsFor(string modelId) =>
        ResolveModel(modelId)?.SupportedEfforts ?? NoEfforts;

    public static bool SupportsEffort(string modelId, string effort) =>
        SupportedEffortsFor(modelId).Any(id => string.Equals(id, effort, StringComparison.OrdinalIgnoreCase));

    /// The effort a fresh session runs at: the provider default ("high") when the model supports effort
    /// at all, empty otherwise (the axis does not apply).
    public static string DefaultEffortFor(string modelId) =>
        SupportsEffort(modelId, "high") ? "high" : "";

    /// The effort to fall back to when a model switch drops the previously selected level: keep it when
    /// still supported, otherwise the new model's default.
    public static string CoerceEffort(string modelId, string currentEffort) =>
        currentEffort.Length > 0 && SupportsEffort(modelId, currentEffort)
            ? currentEffort
            : DefaultEffortFor(modelId);
}
