using System;
using System.Collections.Generic;
using System.Linq;

using FabioSoft.Contracts.Session;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.Conversation;

public static partial class ConversationUpdate
{
    private static readonly string[] PermissionChoices = ["allow", "deny", "allow_always"];
    private static readonly ConversationEffect[] NoEffects = [];

    // --- List helpers ---

    private static IReadOnlyList<Turn> UpdateTurnById(
        IReadOnlyList<Turn> turns, Guid turnId, Func<Turn, Turn> updater)
        => turns.Select(t => t.Id == turnId ? updater(t) : t).ToList();

    private static IReadOnlyList<Turn> AppendItemToTurn(
        IReadOnlyList<Turn> turns, Guid turnId, TurnItem item)
        => UpdateTurnById(turns, turnId, t => t.WithItems([.. t.Items, item]));

    private static Turn UpdateItemInTurn(
        Turn turn, Func<TurnItem, bool> predicate, Func<TurnItem, TurnItem> updater)
        => turn.WithItems(turn.Items.Select(i => predicate(i) ? updater(i) : i).ToList());

    private static bool IsToolItem(string toolUseId, TurnItem item)
        => item is ToolItem ti && ti.Tool.ToolUseId == toolUseId;

    private static bool IsActivePhase(TurnItem item)
        => item is PhaseItem pi && pi.Phase.IsActive;

    // --- Pure helpers ---

    public static bool IsToolResultDenied(string summary)
    {
        var lower = summary.ToLowerInvariant();
        return lower.Contains("denied")
            || lower.Contains("not permitted")
            || lower.Contains("rejected")
            || lower.Contains("cancelled");
    }

    private static IReadOnlyList<Turn> CompleteActivePhase(IReadOnlyList<Turn> turns, Guid? turnId)
    {
        if (turnId is not { } id)
        {
            return turns;
        }

        var now = DateTime.UtcNow;
        return UpdateTurnById(turns, id, turn =>
            UpdateItemInTurn(turn, IsActivePhase, item =>
                item is PhaseItem pi
                    ? new PhaseItem(pi.Phase with
                    {
                        IsActive = false,
                        HasSucceeded = true,
                        Duration = now - pi.Phase.StartedAt
                    })
                    : item));
    }

    private static SessionState FinishInitTurn(SessionState session)
    {
        if (session.InitTurnId is not { } id)
        {
            return session;
        }

        var now = DateTime.UtcNow;
        var turns = UpdateTurnById(session.Turns, id, turn =>
        {
            if (turn.Status is not Running)
            {
                return turn;
            }

            var completedItems = turn.Items.Select(item =>
                                                                   item is PhaseItem pi && pi.Phase.IsActive
                                                                                   ? new PhaseItem(pi.Phase with
                                                                                   {
                                                                                       IsActive = false,
                                                                                       HasSucceeded = true,
                                                                                       Duration = now - pi.Phase.StartedAt
                                                                                   })
                                                                                   : item).ToList();
            return turn with
            {
                Status = new Succeeded(),
                Duration = now - turn.StartedAt,
                Items = completedItems
            };
        });
        return session with { Turns = turns, LastTurnId = id };
    }

    private static (SessionState, ConversationEffect[]) PromoteQueuedTurn(SessionState session)
    {
        if (session.QueuedTurnIds.Count == 0)
        {
            return (session, NoEffects);
        }

        var next = session.QueuedTurnIds[0];
        var activatedTurns = UpdateTurnById(session.Turns, next.Id, turn =>
            turn with { Status = new Running(), StartedAt = DateTime.UtcNow });

        var newSession = session with
        {
            CurrentTurnId = next.Id,
            QueuedTurnIds = session.QueuedTurnIds.Skip(1).ToList(),
            Turns = activatedTurns
        };

        return (newSession, [new SendPromptEffect(session.Id, next.Prompt)]);
    }

    // --- Stream event handlers ---

    private static (SessionState, ConversationEffect[]) HandleInit(SessionState session, string model)
    {
        var afterPhase = session;
        if (session.IsInitActive)
        {
            var turns = CompleteActivePhase(session.Turns, session.InitTurnId);
            afterPhase = FinishInitTurn(session with { Turns = turns });
        }

        var ready = afterPhase with
        {
            Model = model,
            ContextSize = ContextWindow.ForModel(model),
            Status = SessionStatus.Ready
        };

        // A prompt typed while the session was still initialising was held as a Queued turn; now that the
        // agent is ready, send it. AgentInit is the readiness signal (it completes the MCP-loading phase),
        // and an idle boot never produces an AgentResult to trigger the promotion HandleResult does.
        return ready.QueuedTurnIds.Count > 0 && !ready.IsCurrentTurnActive
            ? PromoteQueuedTurn(ready)
            : (ready, NoEffects);
    }

    private static (SessionState, ConversationEffect[]) HandleCapabilities(
        SessionState session, AgentCapabilities capabilities)
    {
        var model = !string.IsNullOrEmpty(capabilities.Model) ? capabilities.Model : session.Model;

        return (session with
        {
            Model = model,
            ContextSize = ContextSizeFor(session, capabilities.Models, model),
            Mode = capabilities.Mode,
            Effort = capabilities.Effort,
            Models = capabilities.Models,
            Modes = capabilities.Modes,
            Efforts = capabilities.Efforts
        }, NoEffects);
    }

    // The context window for a model: the bridge's rich catalog entry when it knows the model, else the
    // local per-family fallback table.
    private static int ContextSizeFor(
        SessionState session, IReadOnlyList<AgentModelInfo> models, string? model)
    {
        if (model is null)
        {
            return session.ContextSize;
        }

        var info = models.FirstOrDefault(m => string.Equals(m.Id, model, StringComparison.OrdinalIgnoreCase));
        return info?.ContextSize ?? ContextWindow.ForModel(model);
    }

    private static (SessionState, ConversationEffect[]) HandleModelChanged(SessionState session, string model) =>
        (session with
        {
            Model = model,
            ContextSize = ContextSizeFor(session, session.Models, model)
        }, NoEffects);

    private static (SessionState, ConversationEffect[]) HandleModeChanged(SessionState session, string mode) =>
        (session with { Mode = mode }, NoEffects);

    private static (SessionState, ConversationEffect[]) HandleEffortChanged(SessionState session, string effort) =>
        (session with { Effort = effort }, NoEffects);

    private static (SessionState, ConversationEffect[]) HandleAborted(SessionState session) => (session with { Status = SessionStatus.Aborting }, NoEffects);

    private static (SessionState, ConversationEffect[]) HandleSessionEnded(
        SessionState session, int exitCode, string detail)
    {
        if (exitCode == 0)
        {
            return (session with { Status = SessionStatus.Ended }, NoEffects);
        }

        var message = string.IsNullOrEmpty(detail) ? $"exit {exitCode}" : $"exit {exitCode}: {detail}";
        var errorMessage = $"Session ended ({message})";

        if (session.CurrentTurnId is { } turnId)
        {
            var now = DateTime.UtcNow;
            var turns = UpdateTurnById(session.Turns, turnId, turn => turn with
            {
                Status = new Failed(errorMessage),
                Duration = now - turn.StartedAt
            });
            return (session with
            {
                Status = SessionStatus.Ended,
                Turns = turns,
                LastTurnId = turnId,
                CurrentTurnId = null
            }, NoEffects);
        }

        var targetTurnId = session.InitTurnId ?? session.LastTurnId;
        if (targetTurnId is { } targetId)
        {
            var turns = UpdateTurnById(session.Turns, targetId, turn =>
                turn with { Status = new Failed(errorMessage) });
            return (session with { Status = SessionStatus.Ended, Turns = turns }, NoEffects);
        }

        var errorTurn = new Turn
        {
            Status = new Failed(errorMessage),
            Items = []
        };
        return (session with
        {
            Status = SessionStatus.Ended,
            Turns = [.. session.Turns, errorTurn]
        }, NoEffects);
    }

    private static (SessionState, ConversationEffect[]) HandleLogMessage(
        SessionState session, string text)
    {
        if (!session.IsInitActive || session.InitTurnId is not { } id)
        {
            return (session, NoEffects);
        }

        var turns = UpdateTurnById(session.Turns, id, turn => turn.WithStatusText(text));
        return (session with { Turns = turns }, NoEffects);
    }

    private static (SessionState, ConversationEffect[]) HandleApiCallRetry(SessionState session)
    {
        if (session.IsInitActive)
        {
            if (session.InitTurnId is not { } id)
            {
                return (session, NoEffects);
            }

            var turns = UpdateTurnById(session.Turns, id, turn =>
                                                       turn.WithStatusText("Retrying API call..."));
            return (session with { Turns = turns }, NoEffects);
        }

        return (session with { Status = SessionStatus.Retrying }, NoEffects);
    }

    private static (SessionState, ConversationEffect[]) HandleCompacting(SessionState session)
    {
        if (session.IsInitActive)
        {
            if (session.InitTurnId is not { } id)
            {
                return (session, NoEffects);
            }

            var turns = UpdateTurnById(session.Turns, id, turn =>
                                                       turn.WithStatusText("Compacting context..."));
            return (session with { Turns = turns }, NoEffects);
        }

        return (session with { Status = SessionStatus.Compacting }, NoEffects);
    }

    private static (SessionState, ConversationEffect[]) HandleThinking(SessionState session, string summary)
    {
        var afterInit = session;
        if (session.IsInitActive)
        {
            var turns = CompleteActivePhase(session.Turns, session.InitTurnId);
            afterInit = FinishInitTurn(session with { Turns = turns }) with { InitState = null };
        }

        var thinking = afterInit with { Status = SessionStatus.Thinking };

        // Persist the reasoning block as an interleaved item (shown dimmed, collapsed by default).
        if (!string.IsNullOrWhiteSpace(summary)
            && thinking.IsCurrentTurnActive
            && thinking.CurrentTurnId is { } turnId)
        {
            var turns = AppendItemToTurn(thinking.Turns, turnId, new ThinkingItem(summary));
            thinking = thinking with { Turns = turns };
        }

        return (thinking, NoEffects);
    }

    private static (SessionState, ConversationEffect[]) HandleThinkingTokens(
        SessionState session, int estimatedTokens)
        => (session with { ThinkingTokens = estimatedTokens }, NoEffects);

    private static (SessionState, ConversationEffect[]) HandleHookStart(
        SessionState session,
        string hookId,
        string hookName,
        bool isSessionStart)
    {
        if (!isSessionStart)
        {
            return (session, NoEffects);
        }

        // SessionStart hooks can arrive after the init turn was already closed (the provider boots
        // slowly when many MCP servers load, so the init timeout may fire first). Revive the init turn
        // instead of dropping them, so the hook rows and the MCP-loading phase still render truthfully.
        if (session.InitState is not { } initState)
        {
            if (session.InitTurnId is not { } reviveTurnId)
            {
                return (session, NoEffects);
            }

            var revivedTurns = UpdateTurnById(session.Turns, reviveTurnId, turn =>
                turn with { Status = new Running() });
            initState = InitState.Default with { FirstEventReceived = true };
            session = session with { InitState = initState, Turns = revivedTurns };
        }

        var initId = session.InitTurnId;
        var turns = !initState.FirstEventReceived
            ? CompleteActivePhase(session.Turns, initId)
            : session.Turns;

        var newPending = initState.PendingSessionStartHooks + 1;

        var headerTurns = turns;
        if (!initState.HookHeaderShown && initId is { } headerTurnId)
        {
            var headerItem = new HookItem(new Hook
            {
                HookId = "header",
                DisplayName = "SESSION START HOOKS",
                IsHeader = true
            });
            headerTurns = AppendItemToTurn(turns, headerTurnId, headerItem);
        }

        var hookItem = new HookItem(new Hook
        {
            HookId = hookId,
            DisplayName = hookName,
            IsActive = true
        });

        var finalTurns = initId is { } id
            ? AppendItemToTurn(headerTurns, id, hookItem)
            : headerTurns;

        return (session with
        {
            InitState = initState with
            {
                FirstEventReceived = true,
                PendingSessionStartHooks = newPending,
                HookHeaderShown = true
            },
            Turns = finalTurns
        }, NoEffects);
    }

    private static (SessionState, ConversationEffect[]) HandleHookComplete(
        SessionState session, string hookId, string hookEvent, string outcome)
    {
        var succeeded = outcome == "success";
        var now = DateTime.UtcNow;
        var initId = session.InitTurnId;

        var turns = initId is { } id
            ? UpdateTurnById(session.Turns, id, turn =>
                turn.WithItems(turn.Items.Select(UpdateHook).ToList()))
            : session.Turns;

        if (session.InitState is { } initState && hookEvent == "SessionStart")
        {
            var newPending = Math.Max(0, initState.PendingSessionStartHooks - 1);
            var finalTurns = turns;
            if (newPending == 0 && initId is { } turnId)
            {
                var phaseItem = new PhaseItem(new Phase
                {
                    DisplayName = "Loading MCPs and plugins",
                    IsActive = true
                });
                finalTurns = AppendItemToTurn(turns, turnId, phaseItem);
            }

            return (session with
            {
                InitState = initState with { PendingSessionStartHooks = newPending },
                Turns = finalTurns
            }, NoEffects);
        }

        return (session with { Turns = turns }, NoEffects);

        TurnItem UpdateHook(TurnItem item) =>
                        item is HookItem hi && hi.Hook.HookId == hookId
                                        ? new HookItem(hi.Hook with
                                        {
                                            IsActive = false,
                                            Duration = now - hi.Hook.StartedAt,
                                            HasSucceeded = succeeded
                                        })
                                        : item;
    }

    private static (SessionState, ConversationEffect[]) HandlePermissionRequest(
        SessionState session,
        string requestId,
        string? toolUseId,
        string matchedRulePattern,
        string matchedRuleScope,
        string reasonText)
    {
        // The bridge already resolved the decision to neutral terms: a non-empty pattern means an ask
        // rule matched (so no free-text reason); otherwise the reason text explains the prompt.
        var rulePattern = string.IsNullOrEmpty(matchedRulePattern) ? null : matchedRulePattern;
        var ruleScope = string.IsNullOrEmpty(matchedRuleScope) ? null : matchedRuleScope;
        var resolvedReasonText = rulePattern is not null ? null : reasonText;

        var permissionItem = new PermissionItem(new Permission
        {
            ReasonText = resolvedReasonText,
            IsResolved = false,
            MatchedRulePattern = rulePattern,
            MatchedRuleScope = ruleScope,
            ToolUseId = toolUseId,
            RequestId = requestId
        });

        var baseTurns = session.Turns;
        if (rulePattern is not null && ruleScope is not null
            && toolUseId is not null && session.CurrentTurnId is { } currentId)
        {
            baseTurns = UpdateTurnById(baseTurns, currentId,
                turn => UpdateItemInTurn(turn,
                    item => IsToolItem(toolUseId, item),
                    item => item is ToolItem ti
                        ? new ToolItem(ti.Tool with
                        {
                            ShowDuration = false,
                            ShowWarning = true,
                            WarningText = $"ask rule {rulePattern} matched",
                            ScopeBadgeText = ruleScope.ToUpperInvariant()
                        })
                        : item));
        }

        // A permission request must always surface a row the user can act on - dropping it silently leaves
        // the agent blocked forever waiting on a control_response that never comes. Fall back to the init or
        // most-recent turn when no turn is current (matching HandleParsingError's target resolution).
        var targetTurnId = session.CurrentTurnId ?? session.InitTurnId ?? session.LastTurnId;
        var finalTurns = targetTurnId is { } turnId
            ? AppendItemToTurn(baseTurns, turnId, permissionItem)
            : baseTurns;

        return (session with { Turns = finalTurns }, NoEffects);
    }

    private static (SessionState, ConversationEffect[]) HandleToolUse(
        SessionState session, string toolUseId, string name, string input, string fullInput)
    {
        var toolItem = new ToolItem(new Tool
        {
            ToolUseId = toolUseId,
            Name = name,
            Arguments = input,
            FullArguments = fullInput,
            IsActive = true
        });

        var turns = session.CurrentTurnId is { } turnId
            ? AppendItemToTurn(session.Turns, turnId, toolItem)
            : session.Turns;

        var knownIds = new HashSet<string>(session.KnownToolUseIds) { toolUseId };
        return (session with { KnownToolUseIds = knownIds, Turns = turns }, NoEffects);
    }

    private static (SessionState, ConversationEffect[]) HandleToolResult(
        SessionState session, string toolUseId, string summary, string fullOutput, TimeSpan duration)
    {
        if (!session.KnownToolUseIds.Contains(toolUseId))
        {
            return (session, NoEffects);
        }

        var isDenied = IsToolResultDenied(summary);

        var turns = session.CurrentTurnId is { } turnId
            ? UpdateTurnById(session.Turns, turnId, turn =>
                turn.WithItems(turn.Items.Select(UpdateTool).ToList()))
            : session.Turns;

        return (session with { Turns = turns }, NoEffects);

        TurnItem UpdateTool(TurnItem item)
        {
            if (item is not ToolItem ti || ti.Tool.ToolUseId != toolUseId)
            {
                return item;
            }

            if (isDenied)
            {
                return new ToolItem(ti.Tool with
                {
                    IsActive = false,
                    ShowDuration = false,
                    StatusText = "DENIED",
                    IsDenied = true,
                    Output = summary,
                    FullOutput = fullOutput
                });
            }

            var effectiveDuration = duration == TimeSpan.Zero
                            ? DateTime.UtcNow - ti.Tool.StartedAt
                            : duration;
            return new ToolItem(ti.Tool with
            {
                IsActive = false,
                Duration = effectiveDuration,
                Output = summary,
                FullOutput = fullOutput
            });
        }
    }

    private static (SessionState, ConversationEffect[]) HandleTextDelta(
        SessionState session, string text)
    {
        if (!session.IsCurrentTurnActive || session.CurrentTurnId is not { } turnId)
        {
            return (session, NoEffects);
        }

        var turns = UpdateTurnById(session.Turns, turnId, turn => turn.WithStatusText(text));
        return (session with { Turns = turns }, NoEffects);
    }

    private static (SessionState, ConversationEffect[]) HandleAssistant(
        SessionState session, string text)
    {
        // Assistant text no longer ends the turn. In stream-json the model frequently emits a narration
        // message ("Let me look into X") with no tool call and a null stop_reason; the old heuristic
        // (final == "no tool use") wrongly treated that as the final message, closed the turn, and dropped
        // every tool, result and answer that followed - the "it said it would do something then never came
        // back" bug. The authoritative end-of-turn signal is AgentResult (see HandleResult). Here we only
        // keep the latest assistant text as the turn's response, so the answer shows as it streams in; the
        // final text block and the result summary agree on the same text, so this is not lost.
        if (!session.IsCurrentTurnActive
            || session.CurrentTurnId is not { } turnId
            || string.IsNullOrEmpty(text))
        {
            return (session, NoEffects);
        }

        // Persist the completed text block as an interleaved item (detailed-output mode shows every block in
        // order), clear the live streaming line, and keep Response as the latest-text fallback for callers
        // that still read it. The streamed StatusText held this block's partials until now.
        var turns = UpdateTurnById(session.Turns, turnId, turn => turn with
        {
            Response = text,
            StatusText = "",
            Items = [.. turn.Items, new TextItem(text)]
        });
        return (session with { Turns = turns }, NoEffects);
    }

    private static (SessionState, ConversationEffect[]) HandleUsage(
        SessionState session, int inputTokens, int outputTokens, int cacheReadTokens)
    {
        var turns = session.Turns;
        if (session.IsCurrentTurnActive && session.CurrentTurnId is { } turnId)
        {
            turns = UpdateTurnById(turns, turnId, turn =>
                turn.WithTotalTokens(turn.TotalTokens + outputTokens));
        }

        // Each turn's input + cache-read IS the current context occupancy (the whole prompt sent that turn
        // already includes the prior conversation), so set it to the latest total rather than accumulating -
        // accumulating grew the figure every turn and pushed the percentage past 100%.
        var total = inputTokens + cacheReadTokens;
        var newContextFilled = total > 0 ? total : session.ContextFilled;

        return (session with { Turns = turns, ContextFilled = newContextFilled }, NoEffects);
    }

    private static (SessionState, ConversationEffect[]) HandleResult(
        SessionState session, string model, string resultText, bool isError)
    {
        var afterInit = session;
        if (session.IsInitActive)
        {
            var initTurns = CompleteActivePhase(session.Turns, session.InitTurnId);
            afterInit = FinishInitTurn(session with { Turns = initTurns }) with { InitState = null };
        }

        var withModelOnly = !string.IsNullOrEmpty(model)
            ? afterInit with { Model = model, ContextSize = ContextWindow.ForModel(model) }
            : afterInit;
        // The thinking-token estimate belongs to the turn that just ended; clear it so it does not linger.
        var withModel = withModelOnly with { ThinkingTokens = 0 };

        // The result message terminates the turn. Finalize the active turn here: the provider's result text
        // is the final summary (fall back to the streamed response when a result subtype carries none), and
        // is_error decides success vs failure. Then promote any queued prompt.
        if (withModel.IsCurrentTurnActive && withModel.CurrentTurnId is { } turnId)
        {
            var now = DateTime.UtcNow;
            var turns = UpdateTurnById(withModel.Turns, turnId, turn =>
            {
                // Normally every assistant text block already became a TextItem; if a result subtype carried
                // a summary but no assistant block ever arrived, materialise it so the answer still shows.
                var items = turn.Items;
                if (!string.IsNullOrEmpty(resultText) && !items.OfType<TextItem>().Any())
                {
                    items = [.. items, new TextItem(resultText)];
                }

                return turn with
                {
                    Response = !string.IsNullOrEmpty(resultText) ? resultText : turn.Response,
                    StatusText = "",
                    Status = isError
                        ? new Failed(string.IsNullOrEmpty(resultText) ? "Turn failed" : resultText)
                        : new Succeeded(),
                    Duration = now - turn.StartedAt,
                    Items = items
                };
            });

            var finalized = withModel with
            {
                Turns = turns,
                LastTurnId = turnId,
                CurrentTurnId = null,
                Status = SessionStatus.Ready
            };

            return finalized.QueuedTurnIds.Count > 0
                ? PromoteQueuedTurn(finalized)
                : (finalized, NoEffects);
        }

        if (withModel.QueuedTurnIds.Count > 0)
        {
            return PromoteQueuedTurn(withModel);
        }

        var finalStatus = withModel.CurrentTurnId is not null
            ? withModel.Status
            : SessionStatus.Idle;
        return (withModel with { Status = finalStatus }, NoEffects);
    }

    // --- Top-level handlers ---

    public static (ConversationState State, ConversationEffect[] Effects) HandleStreamEvent(
        ConversationState state,
        AgentStreamEvent streamEvent)
    {
        var sessionId = streamEvent.SessionId;
        if (state.Sessions.All(s => s.Id != sessionId))
        {
            return (state, NoEffects);
        }

        var session = state.Sessions.First(s => s.Id == sessionId);

        var (updatedSession, effects) = streamEvent switch
        {
            AgentInit e => HandleInit(session, e.Model),
            AgentCapabilities e => HandleCapabilities(session, e),
            AgentModelChanged e => HandleModelChanged(session, e.Model),
            AgentModeChanged e => HandleModeChanged(session, e.Mode),
            AgentEffortChanged e => HandleEffortChanged(session, e.Effort),
            AgentAborted => HandleAborted(session),
            AgentSessionEnded e when session.Status == SessionStatus.Aborting =>
                (session, new ConversationEffect[] { new DispatchFullRestartEffect() }),
            AgentSessionEnded e => HandleSessionEnded(session, e.ExitCode, e.Detail),
            AgentSessionAlreadyExited => (session, NoEffects),
            AgentLogMessage e => HandleLogMessage(session, e.Text),
            AgentApiCallRetry => HandleApiCallRetry(session),
            AgentCompacting => HandleCompacting(session),
            AgentThinking e => HandleThinking(session, e.Summary),
            AgentThinkingTokens e => HandleThinkingTokens(session, e.EstimatedTokens),
            // Rate-limit notifications are surfaced by the account-global usage indicator, not the
            // per-turn conversation; ignore them here so they neither error nor disturb turn state.
            AgentRateLimit => (session, NoEffects),
            AgentHookStart e => HandleHookStart(session, e.HookId, e.HookName, e.IsSessionStart),
            AgentHookComplete e => HandleHookComplete(session, e.HookId, e.HookEvent, e.Outcome),
            AgentPermissionRequest e => HandlePermissionRequest(
                session, e.RequestId, e.ToolUseId, e.MatchedRulePattern, e.MatchedRuleScope, e.ReasonText),
            AgentToolUse e => HandleToolUse(session, e.ToolUseId, e.ToolName, e.Input, e.FullInput),
            AgentToolResult e => HandleToolResult(session, e.ToolUseId, e.Summary, e.FullOutput, e.Duration),
            AgentTextDelta e => HandleTextDelta(session, e.Text),
            AgentAssistant e => HandleAssistant(session, e.Text),
            AgentUsage e => HandleUsage(session, e.InputTokens, e.OutputTokens, e.CacheReadTokens),
            AgentResult e => HandleResult(session, e.Model, e.ResultText, e.IsError),
            _ => (session, NoEffects)
        };

        // Handle DispatchFullRestartEffect by inlining the full restart
        if (effects.Length == 1 && effects[0] is DispatchFullRestartEffect)
        {
            var updatedState = state.WithSessionById(sessionId, _ => updatedSession);
            return HandleFullRestart(updatedState);
        }

        return (state.WithSessionById(sessionId, _ => updatedSession), effects);
    }

    public static (ConversationState State, ConversationEffect[] Effects) HandleParsingError(
        ConversationState state, Guid sessionId, string errorMessage, bool isIgnorable)
    {
        if (isIgnorable)
        {
            return (state, NoEffects);
        }

        if (state.Sessions.All(s => s.Id != sessionId))
        {
            return (state, NoEffects);
        }

        var session = state.Sessions.First(s => s.Id == sessionId);
        var errorItem = new ErrorItem(errorMessage);

        var targetTurnId = session.CurrentTurnId ?? session.InitTurnId ?? session.LastTurnId;
        if (targetTurnId is { } turnId)
        {
            var turns = AppendItemToTurn(session.Turns, turnId, errorItem);
            return (state.WithSessionById(sessionId, _ => session with { Turns = turns }), NoEffects);
        }

        var errorTurn = new Turn
        {
            Status = new Failed(""),
            Items = [errorItem]
        };
        return (state.WithSessionById(sessionId,
            _ => session with { Turns = [.. session.Turns, errorTurn] }), NoEffects);
    }

    public static (ConversationState State, ConversationEffect[] Effects) HandleUserSubmitted(
        ConversationState state, string prompt)
    {
        if (state.ActiveSession is not { } session)
        {
            return (state, NoEffects);
        }

        var estimatedTokens = PromptAnalysis.EstimateTokens(prompt);
        // Queue the prompt when a turn is already running, or when the agent session has not finished
        // initialising yet (still on the init turn, not yet Ready). A prompt typed during boot is held as a
        // Queued turn and sent once the session reports ready, rather than pushed to a session mid-init.
        var sessionInitialising = session.IsInitActive && session.Status == SessionStatus.Idle;
        var queued = session.IsCurrentTurnActive || sessionInitialising;
        var newTurn = new Turn
        {
            Prompt = prompt,
            EstimatedTokens = estimatedTokens,
            TotalTokens = estimatedTokens,
            Status = queued ? new Queued() : new Running()
        };

        var newSession = session with
        {
            Turns = [.. session.Turns, newTurn],
            CurrentTurnId = queued ? session.CurrentTurnId : newTurn.Id,
            QueuedTurnIds = queued
                ? [.. session.QueuedTurnIds, new QueuedTurn(newTurn.Id, prompt)]
                : session.QueuedTurnIds
        };

        var effects = queued
            ? NoEffects
            : new ConversationEffect[] { new SendPromptEffect(session.Id, prompt) };

        return (state.WithActiveSession(_ => newSession), effects);
    }

    public static (ConversationState State, ConversationEffect[] Effects) HandleUserAborted(
        ConversationState state)
    {
        if (state.ActiveSession is not { } session)
        {
            return (state, NoEffects);
        }

        // Queued prompts are removed newest-first before the running turn is touched, so Esc walks back
        // the queue one prompt at a time and only aborts the active turn once nothing is left queued.
        if (session.QueuedTurnIds.Count > 0)
        {
            return HandleUserCancelledQueued(state);
        }

        if (!session.IsCurrentTurnActive)
        {
            return (state, NoEffects);
        }

        var now = DateTime.UtcNow;
        var turns = session.CurrentTurnId is { } turnId
            ? UpdateTurnById(session.Turns, turnId, turn => turn with
            {
                Status = new Aborted(),
                Duration = now - turn.StartedAt
            })
            : session.Turns;

        var newSession = session with
        {
            Status = SessionStatus.Aborted,
            Turns = turns,
            LastTurnId = session.CurrentTurnId,
            CurrentTurnId = null
        };

        return (state.WithActiveSession(_ => newSession),
            [new InterruptSessionEffect(session.Id)]);
    }

    public static (ConversationState State, ConversationEffect[] Effects) HandleUserCancelledQueued(
        ConversationState state)
    {
        if (state.ActiveSession is not { } session)
        {
            return (state, NoEffects);
        }

        if (session.QueuedTurnIds.Count == 0)
        {
            return (state, NoEffects);
        }

        var newest = session.QueuedTurnIds[^1];
        var turns = session.Turns.Where(t => t.Id != newest.Id).ToList();
        var newSession = session with
        {
            QueuedTurnIds = session.QueuedTurnIds.Take(session.QueuedTurnIds.Count - 1).ToList(),
            Turns = turns
        };

        return (state.WithActiveSession(_ => newSession), NoEffects);
    }

    public static (ConversationState State, ConversationEffect[] Effects) HandlePermissionDecided(
        ConversationState state, string requestId, string decision)
    {
        if (state.ActiveSession is not { } session)
        {
            return (state, NoEffects);
        }

        var allow = decision != "deny";

        var permissionOpt = session.Turns
            .SelectMany(t => t.Items)
            .OfType<PermissionItem>()
            .FirstOrDefault(pi => pi.Permission.RequestId == requestId && !pi.Permission.IsResolved);

        if (permissionOpt is null)
        {
            return (state, NoEffects);
        }

        var toolUseId = permissionOpt.Permission.ToolUseId;

        var turns = session.Turns.Select(turn =>
        {
            var updatedItems = turn.Items.Select(item =>
            {
                if (item is PermissionItem pi && pi.Permission.RequestId == requestId)
                {
                    return new PermissionItem(pi.Permission with { IsResolved = true });
                }

                if (item is ToolItem ti && toolUseId is not null && ti.Tool.ToolUseId == toolUseId && ti.Tool.ShowWarning)
                {
                    var statusText = decision switch
                    {
                        "deny" => "DENIED",
                        "allow" => "ALLOWED ONCE",
                        "allow_always" => "ALWAYS ALLOWED",
                        _ => ti.Tool.StatusText
                    };
                    return new ToolItem(ti.Tool with
                    {
                        ShowWarning = false,
                        ShowDuration = true,
                        WarningText = "",
                        ScopeBadgeText = "",
                        StatusText = statusText,
                        IsDenied = decision == "deny"
                    });
                }

                return item;
            }).ToList();

            return turn.WithItems(updatedItems);
        }).ToList();

        var newSession = session with { Turns = turns };
        return (state.WithActiveSession(_ => newSession),
            [new SendPermissionResponseEffect(session.Id, requestId, allow)]);
    }

    // A plugin that fails while the init phase is still active lands as an error row in the init turn,
    // so a missing agent is explained in the chat instead of leaving an eternal spinner.
    public static (ConversationState State, ConversationEffect[] Effects) HandlePluginFailure(
        ConversationState state, string pluginId, string reason)
    {
        if (state.ActiveSession is not { } session || !session.IsInitActive || session.InitTurnId is not { } initTurnId)
        {
            return (state, NoEffects);
        }

        var updated = state.WithActiveSession(s =>
            s.WithTurns(AppendItemToTurn(
                s.Turns, initTurnId, new ErrorItem($"{pluginId} failed to start: {reason}"))));

        return (updated, NoEffects);
    }

    public static (ConversationState State, ConversationEffect[] Effects) HandleTick(
        ConversationState state, DateTime now)
    {
        if (state.ActiveSession is not { } session)
        {
            return (state, NoEffects);
        }

        // Only a running turn ticks. Once a turn finishes its duration is frozen at completion, and any
        // item still flagged active inside a finished turn (e.g. a hook whose completion never matched)
        // must not keep counting up - tying the tick to the turn's Running status guarantees that.
        var turns = session.Turns.Select(turn =>
        {
            if (turn.Status is not Running)
            {
                return turn;
            }

            var items = turn.Items.Select(item => item switch
            {
                PhaseItem pi when pi.Phase.IsActive =>
                                new PhaseItem(pi.Phase with { Duration = now - pi.Phase.StartedAt }),
                HookItem hi when hi.Hook.IsActive =>
                                new HookItem(hi.Hook with { Duration = now - hi.Hook.StartedAt }),
                ToolItem ti when ti.Tool.IsActive =>
                                new ToolItem(ti.Tool with { Duration = now - ti.Tool.StartedAt }),
                _ => item
            }).ToList();

            return turn with { Duration = now - turn.StartedAt, Items = items };
        }).ToList();

        return (state.WithActiveSession(_ => session with { Turns = turns }), NoEffects);
    }

    public static (ConversationState State, ConversationEffect[] Effects) HandleInitTimedOut(
        ConversationState state, Guid sessionId)
    {
        if (state.Sessions.All(s => s.Id != sessionId))
        {
            return (state, NoEffects);
        }

        var session = state.Sessions.First(s => s.Id == sessionId);
        if (!session.IsInitActive)
        {
            return (state, NoEffects);
        }

        var turns = CompleteActivePhase(session.Turns, session.InitTurnId);
        var afterInit = FinishInitTurn(session with { Turns = turns }) with
        {
            InitState = null,
            Status = SessionStatus.Ready
        };

        if (afterInit.IsCurrentTurnActive)
        {
            return (state.WithSessionById(sessionId, _ => afterInit), NoEffects);
        }

        var (promoted, effects) = PromoteQueuedTurn(afterInit);
        return (state.WithSessionById(sessionId, _ => promoted), effects);
    }

    public static (ConversationState State, ConversationEffect[] Effects) HandleFullRestart(
        ConversationState state)
    {
        var newSession = SessionState.Create();
        var oldSessionId = state.ActiveSessionId;

        var endedState = state.WithActiveSession(s => s with { Status = SessionStatus.Ended });

        var newState = endedState with
        {
            Sessions = [.. endedState.Sessions, newSession],
            ActiveSessionId = newSession.Id
        };

        var effects = new List<ConversationEffect>
        {
            new StartNewSessionEffect(newSession.Id),
            new ScheduleInitTimeoutEffect(newSession.Id)
        };

        if (oldSessionId is { } oldId)
        {
            effects.Insert(0, new DisposeSessionEffect(oldId));
        }

        return (newState, effects.ToArray());
    }

    public static bool HasActiveItems(ConversationState state)
    {
        if (state.ActiveSession is not { } session)
        {
            return false;
        }

        return session.Turns.Any(turn =>
                                                 turn.Status is Running ||
                                                 turn.Items.Any(item =>
                                                                                (item is ToolItem ti && ti.Tool.IsActive) ||
                                                                                (item is HookItem hi && hi.Hook.IsActive && !hi.Hook.IsHeader) ||
                                                                                (item is PhaseItem pi && pi.Phase.IsActive)));
    }

    // Internal effect for dispatching full restart from within HandleStreamEvent
    private sealed record DispatchFullRestartEffect : ConversationEffect;
}
