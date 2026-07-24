using System;
using System.Linq;

using FabioSoft.Claude;
using FabioSoft.Contracts.Session;
using FabioSoft.Nucleus.Contracts;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace FabioSoft.Nucleus.Plugins.ClaudeBridge;

// Translates the Claude adapter's native StreamEvent DU into the provider-neutral AgentStreamEvent
// contract family. This mapping is the facade seam: a future provider ships its own adapter + bridge
// and emits the same Agent* messages, so every UI plugin keeps working unchanged.
public static class StreamEventMapper
{
    // The two resolver delegates are the provider-specific seam: resolveHookDisplayName turns a hook
    // event into its friendly name (advancing the per-session firing counter), and resolveReason
    // classifies a permission decision into neutral terms. Both are backed by FabioSoft.Claude in
    // production and faked in tests, keeping this translation pure and provider-knowledge-free.
    // Returns null for provider-internal chatter that must not reach the UI: synthetic assistant
    // messages (locally generated slash-command output) and non-error num_turns=0 results (the local
    // no-op acknowledgement of e.g. the session boot command or /effort) - publishing the latter as
    // AgentResult would terminate whatever real turn is active.
    public static AgentStreamEvent? Map(
        Guid sessionId,
        StreamEvent streamEvent,
        Func<string, string> resolveHookDisplayName,
        Func<string?, string?, string, string, ResolvedPermissionReason> resolveReason)
    {
        if (streamEvent is StreamEvent.Init init)
        {
            return new AgentInit(sessionId, init.sessionId.Item, init.model, init.slashCommands.ToArray());
        }

        if (streamEvent is StreamEvent.Commands commands)
        {
            var descriptors = commands.commands
                .Select(command => new AgentCommand(command.Name, command.Description, command.ArgumentHint))
                .ToArray();
            return new AgentCommandsAvailable(sessionId, descriptors);
        }

        if (streamEvent is StreamEvent.SessionEnded ended)
        {
            return new AgentSessionEnded(sessionId, ended.exitCode, ended.detail);
        }

        if (streamEvent.IsSessionAlreadyExited)
        {
            return new AgentSessionAlreadyExited(sessionId);
        }

        if (streamEvent is StreamEvent.LogMessage log)
        {
            return new AgentLogMessage(sessionId, log.text);
        }

        if (streamEvent.IsApiCallRetry)
        {
            return new AgentApiCallRetry(sessionId);
        }

        if (streamEvent.IsCompacting)
        {
            return new AgentCompacting(sessionId);
        }

        if (streamEvent is StreamEvent.Thinking thinking)
        {
            return new AgentThinking(sessionId, thinking.summary);
        }

        if (streamEvent is StreamEvent.ThinkingTokens thinkingTokens)
        {
            return new AgentThinkingTokens(sessionId, thinkingTokens.estimatedTokens);
        }

        if (streamEvent is StreamEvent.RateLimit rateLimit)
        {
            return new AgentRateLimit(
                sessionId,
                rateLimit.Item.LimitType,
                rateLimit.Item.Status,
                rateLimit.Item.ResetsAt,
                rateLimit.Item.IsUsingOverage);
        }

        if (streamEvent is StreamEvent.ToolUse toolUse)
        {
            return new AgentToolUse(
                sessionId, toolUse.Item.Name, toolUse.Item.ToolUseId, toolUse.Item.Input, toolUse.Item.FullInput);
        }

        if (streamEvent is StreamEvent.ToolResult toolResult)
        {
            return new AgentToolResult(
                sessionId, toolResult.Item.ToolUseId, toolResult.Item.Summary, toolResult.Item.FullOutput, toolResult.Item.Duration);
        }

        if (streamEvent is StreamEvent.TextDelta textDelta)
        {
            return new AgentTextDelta(sessionId, textDelta.text);
        }

        if (streamEvent is StreamEvent.Assistant assistant)
        {
            return assistant.Item.IsSynthetic
                ? null
                : new AgentAssistant(sessionId, assistant.Item.Text, assistant.Item.IsFinal);
        }

        if (streamEvent is StreamEvent.Usage usage)
        {
            return new AgentUsage(sessionId, usage.Item.InputTokens, usage.Item.OutputTokens, usage.Item.CacheReadTokens);
        }

        if (streamEvent is StreamEvent.Result result)
        {
            if (result.Item.NumTurns == 0 && !result.Item.IsError)
            {
                return null;
            }

            return new AgentResult(
                sessionId,
                result.Item.SessionId.Item,
                result.Item.CostUsd,
                result.Item.Duration,
                result.Item.Model,
                result.Item.ResultText,
                result.Item.IsError);
        }

        if (streamEvent is StreamEvent.HookStart hookStart)
        {
            // The provider knows its own hook configuration, so the friendly display name (and whether
            // this is a session-start hook) is resolved here; the neutral event carries the result.
            var hookEvent = hookStart.Item.HookEvent;
            return new AgentHookStart(
                sessionId,
                hookStart.Item.HookId,
                resolveHookDisplayName(hookEvent),
                hookEvent,
                hookEvent == "SessionStart");
        }

        if (streamEvent is StreamEvent.HookComplete hookComplete)
        {
            return new AgentHookComplete(
                sessionId,
                hookComplete.Item.HookId,
                hookComplete.Item.HookName,
                hookComplete.Item.HookEvent,
                hookComplete.Item.Outcome,
                FSharpOption<int>.get_IsSome(hookComplete.Item.ExitCode) ? hookComplete.Item.ExitCode.Value : null,
                hookComplete.Item.Stdout,
                hookComplete.Item.Stderr);
        }

        if (streamEvent is StreamEvent.PermissionRequest permReq)
        {
            var info = permReq.Item;

            // Classify Claude's decision reason - including reading the settings files to find the
            // matching ask-rule - here in the bridge, so the neutral event carries only resolved terms.
            var reason = resolveReason(
                FSharpOption<string>.get_IsSome(info.DecisionReasonType) ? info.DecisionReasonType.Value : null,
                FSharpOption<string>.get_IsSome(info.DecisionReason) ? info.DecisionReason.Value : null,
                info.ToolName,
                info.Input);

            return new AgentPermissionRequest(
                sessionId,
                info.RequestId,
                info.ToolName,
                FSharpOption<string>.get_IsSome(info.ToolUseId) ? info.ToolUseId.Value : null,
                info.Input,
                reason.MatchedRulePattern,
                reason.MatchedRuleScope,
                reason.ReasonText,
                BuildPermissionOptions(info.Suggestions));
        }

        if (streamEvent.IsAborted)
        {
            return new AgentAborted(sessionId);
        }

        if (streamEvent is StreamEvent.TaskStarted taskStarted)
        {
            return new AgentTaskStarted(
                sessionId, taskStarted.taskId, taskStarted.description, taskStarted.taskType);
        }

        if (streamEvent is StreamEvent.TaskCompleted taskCompleted)
        {
            return new AgentTaskCompleted(
                sessionId, taskCompleted.taskId, taskCompleted.status, taskCompleted.summary);
        }

        throw new ArgumentException($"Unknown StreamEvent: {streamEvent}");
    }

    public static AgentParsingError MapError(Guid sessionId, ParsingError error) =>
        new(sessionId, ParsingErrorModule.getMessage(error), ParsingErrorModule.isIgnorable(error));

    // The provider's suggested permission updates become the varying middle options of the prompt: one
    // terse, uppercase segment per suggestion, keyed "suggestion-{index}". The UI frames these between a
    // fixed leading ALLOW and trailing DENY, so an empty suggestion list yields the plain allow/deny prompt.
    public static AgentPermissionOption[] BuildPermissionOptions(FSharpList<PermissionUpdate> suggestions) =>
        suggestions
            .Select((suggestion, index) => new AgentPermissionOption($"suggestion-{index}", OptionLabel(suggestion)))
            .ToArray();

    // Maps a chosen option id back to the provider decision. "deny" denies; "allow" (and any unrecognized
    // id) allows once with no remembered rule; "suggestion-{index}" allows and echoes that one suggestion
    // back as updatedPermissions so the provider persists the rule. suggestions is the request's list,
    // recovered by the plugin; null or out-of-range indices fall back to a plain allow-once.
    public static PermissionDecision ResolvePermissionDecision(string optionId, PermissionUpdate[]? suggestions)
    {
        if (optionId == "deny")
        {
            return PermissionDecision.Deny;
        }

        if (optionId is not null
            && optionId.StartsWith("suggestion-", StringComparison.Ordinal)
            && int.TryParse(optionId["suggestion-".Length..], out var index)
            && suggestions is not null
            && index >= 0 && index < suggestions.Length)
        {
            return PermissionDecision.NewAllow(ListModule.OfArray(new[] { suggestions[index] }));
        }

        return PermissionDecision.NewAllow(FSharpList<PermissionUpdate>.Empty);
    }

    private static string OptionLabel(PermissionUpdate update) => update switch
    {
        PermissionUpdate.AddRules r => RuleLabel(r.behavior, r.destination),
        PermissionUpdate.ReplaceRules r => RuleLabel(r.behavior, r.destination),
        PermissionUpdate.RemoveRules r => "REMOVE RULE",
        PermissionUpdate.SetMode m => ModeLabel(m.mode),
        PermissionUpdate.AddDirectories d => DirectoryLabel(d.directories),
        PermissionUpdate.RemoveDirectories d => "REVOKE ACCESS",
        _ => "ALLOW"
    };

    // The tool and its arguments are already shown in the row above, so the option states the effect and how
    // far the choice reaches - just this session, this project, or every project - never the command itself.
    private static string RuleLabel(string behavior, string destination)
    {
        var verb = behavior switch { "deny" => "DENY", "ask" => "ASK", _ => "ALLOW" };
        return destination switch
        {
            "userSettings" => $"ALWAYS {verb}",
            "localSettings" or "projectSettings" => $"{verb} FOR PROJECT",
            _ => $"{verb} IN SESSION" // session, cliArg, or anything unrecognized: the transient scope
        };
    }

    private static string ModeLabel(string mode) => mode switch
    {
        "bypassPermissions" => "BYPASS PERMISSIONS",
        "acceptEdits" => "ACCEPT EDITS",
        "plan" => "PLAN MODE",
        "auto" => "AUTO MODE",
        "dontAsk" => "DON'T ASK AGAIN",
        "default" => "DEFAULT MODE",
        _ => mode.ToUpperInvariant()
    };

    private static string DirectoryLabel(FSharpList<string> directories)
    {
        var list = directories.ToList();
        return list.Count switch
        {
            0 => "ALLOW ACCESS",
            1 => $"ALLOW ACCESS: {list[0].ToUpperInvariant()}",
            _ => $"ALLOW ACCESS: {list.Count} DIRS"
        };
    }
}
