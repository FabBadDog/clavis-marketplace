using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.EventsPanel;

public enum EventCategory { Output, Input, Error }

public abstract record TextSegment;
public sealed record LabelSegment(string Text) : TextSegment;
public sealed record ValueSegment(string Text) : TextSegment;
public sealed record SecondarySegment(string Text) : TextSegment;
public sealed record ErrorLabelSegment(string Text) : TextSegment;

public sealed record ContinuationLine(string Label, IReadOnlyList<TextSegment> Segments)
{
    // The continuation's text, joined from its segments, so a row template can show it in full (wrapped)
    // without re-deriving the segment-to-text projection.
    public string Text => string.Join("", Segments.Select(segment => segment switch
    {
        LabelSegment l => l.Text,
        ValueSegment v => v.Text,
        SecondarySegment s => s.Text,
        ErrorLabelSegment e => e.Text,
        _ => ""
    }));
}

public sealed record EventEntry(
    DateTime Timestamp,
    EventCategory Category,
    IReadOnlyList<TextSegment> Segments,
    IReadOnlyList<ContinuationLine> ContinuationLines)
{
    // Defaulted so the 4-argument constructor still reads naturally; the factory fills these in.
    public LogLevel Level { get; init; } = LogLevel.Info;
    public string Source { get; init; } = "";
    public string MessageType { get; init; } = "";
}

public static class EventEntryFactory
{
    private static readonly TextSegment Dot = new LabelSegment(" · ");

    private const string ClaudeSource = "ClaudeBridge";
    private const string ConversationSource = "Conversation";
    private const string BusSource = "Bus";

    public static string FormatDelta(DateTime sessionStart, DateTime timestamp)
    {
        var span = timestamp - sessionStart;
        if (span.TotalMilliseconds < 1000)
        {
            return $"+ {(int)span.TotalMilliseconds}ms";
        }

        if (span.TotalSeconds < 60)
        {
            return $"+ {(int)span.TotalSeconds}s";
        }

        if (span.TotalMinutes < 60)
        {
            var minutes = (int)span.TotalMinutes;
            var seconds = span.Seconds;
            return seconds == 0 ? $"+ {minutes}min" : $"+ {minutes}m{seconds}s";
        }
        var hours = (int)span.TotalHours;
        var mins = span.Minutes;
        return mins == 0 ? $"+ {hours}h" : $"+ {hours}h{mins}m";
    }

    public static EventEntry FromStreamEvent(DateTime timestamp, AgentStreamEvent evt)
    {
        var (segments, continuations) = evt switch
        {
            AgentInit e => (
                new TextSegment[] { new LabelSegment("Init "), new ValueSegment(e.AgentSessionId), Dot, new ValueSegment(e.Model) },
                Array.Empty<ContinuationLine>()),

            AgentSessionEnded e => (
                new TextSegment[] { new LabelSegment("SessionEnded"), Dot, new LabelSegment("exit: "), new ValueSegment(e.ExitCode.ToString()) },
                string.IsNullOrWhiteSpace(e.Detail)
                    ? Array.Empty<ContinuationLine>()
                    : new[] { new ContinuationLine("detail", [new ValueSegment(e.Detail)]) }),

            AgentSessionAlreadyExited => (
                new TextSegment[] { new LabelSegment("SessionAlreadyExited") },
                Array.Empty<ContinuationLine>()),

            AgentLogMessage e => (
                new TextSegment[] { new LabelSegment("LogMessage "), new ValueSegment($"\"{e.Text}\"") },
                Array.Empty<ContinuationLine>()),

            AgentApiCallRetry => (
                new TextSegment[] { new LabelSegment("ApiCallRetry") },
                Array.Empty<ContinuationLine>()),

            AgentCompacting => (
                new TextSegment[] { new LabelSegment("Compacting") },
                Array.Empty<ContinuationLine>()),

            AgentThinking e => (
                new TextSegment[] { new LabelSegment("Thinking "), new ValueSegment($"\"{e.Summary}\"") },
                Array.Empty<ContinuationLine>()),

            AgentThinkingTokens e => (
                new TextSegment[] { new LabelSegment("ThinkingTokens "), new ValueSegment($"{e.EstimatedTokens:N0}") },
                Array.Empty<ContinuationLine>()),

            AgentRateLimit e => (
                new TextSegment[] { new LabelSegment("RateLimit "), new ValueSegment(e.LimitType), Dot, new ValueSegment(e.Status), Dot, new ValueSegment($"resets {e.ResetsAt:HH:mm}") },
                Array.Empty<ContinuationLine>()),

            AgentToolUse e => (
                new TextSegment[] { new LabelSegment("ToolUse "), new ValueSegment(e.ToolName), Dot, new SecondarySegment(e.ToolUseId) },
                new[] { new ContinuationLine("input", [new ValueSegment(e.Input)]) }),

            AgentToolResult e => (
                new TextSegment[] { new LabelSegment("ToolResult "), new SecondarySegment(e.ToolUseId), Dot, new ValueSegment(e.Summary), Dot, new ValueSegment($"{e.Duration.TotalMilliseconds:F0}ms") },
                Array.Empty<ContinuationLine>()),

            AgentTextDelta e => (
                new TextSegment[] { new LabelSegment("TextDelta "), new ValueSegment($"\"{e.Text}\"") },
                Array.Empty<ContinuationLine>()),

            AgentAssistant e => (
                new TextSegment[] { new LabelSegment("Assistant"), Dot, new LabelSegment("isFinal: "), new ValueSegment(e.IsFinal.ToString()) },
                new[] { new ContinuationLine("text", [new ValueSegment($"\"{e.Text}\"")]) }),

            AgentUsage e => (
                new TextSegment[] { new LabelSegment("Usage "), new ValueSegment($"in: {e.InputTokens:N0}"), Dot, new ValueSegment($"out: {e.OutputTokens:N0}"), Dot, new ValueSegment($"cache: {e.CacheReadTokens:N0}") },
                Array.Empty<ContinuationLine>()),

            AgentResult e => (
                new TextSegment[] { new LabelSegment("Result "), new ValueSegment(e.AgentSessionId), Dot, new ValueSegment($"${e.CostUsd:F4}"), Dot, new ValueSegment($"{e.Duration.TotalSeconds:F1}s"), Dot, new ValueSegment(e.Model) },
                Array.Empty<ContinuationLine>()),

            AgentHookStart e => (
                new TextSegment[] { new LabelSegment("HookStart "), new ValueSegment(e.HookName), Dot, new ValueSegment(e.HookEvent), Dot, new SecondarySegment(e.HookId) },
                Array.Empty<ContinuationLine>()),

            AgentHookComplete e => (
                new TextSegment[]
                {
                    new LabelSegment("HookComplete "), new ValueSegment(e.HookName), Dot,
                    new ValueSegment(e.HookEvent), Dot, new ValueSegment(e.Outcome)
                }.Concat(e.ExitCode.HasValue
                    ? [Dot, new LabelSegment("exit "), new ValueSegment(e.ExitCode.Value.ToString())]
                    : []).ToArray(),
                new[]
                {
                    new ContinuationLine("stdout", [new ValueSegment(string.IsNullOrEmpty(e.Stdout) ? "(empty)" : e.Stdout)]),
                    new ContinuationLine("stderr", [new ValueSegment(string.IsNullOrEmpty(e.Stderr) ? "(empty)" : e.Stderr)])
                }),

            AgentPermissionRequest e => (
                new TextSegment[] { new LabelSegment("PermissionRequest "), new ValueSegment(e.ToolName), Dot, new SecondarySegment(e.RequestId) }
                    .Concat(e.ToolUseId is not null ? [Dot, new SecondarySegment(e.ToolUseId)] : []).ToArray(),
                new[] { new ContinuationLine("input", [new ValueSegment(e.Input)]) }
                    .Concat(!string.IsNullOrEmpty(e.MatchedRulePattern)
                        ? [new ContinuationLine("reason",
                            [new ValueSegment($"ask rule {e.MatchedRulePattern}"), Dot, new SecondarySegment(e.MatchedRuleScope)])]
                        : !string.IsNullOrEmpty(e.ReasonText)
                            ? [new ContinuationLine("reason", [new ValueSegment(e.ReasonText)])]
                            : []).ToArray()),

            AgentAborted => (
                new TextSegment[] { new LabelSegment("Aborted") },
                Array.Empty<ContinuationLine>()),

            _ => (new TextSegment[] { new LabelSegment(evt.GetType().Name) }, Array.Empty<ContinuationLine>())
        };

        return new EventEntry(timestamp, EventCategory.Output, segments, continuations)
        {
            Level = LogLevel.Debug,
            Source = ClaudeSource,
            MessageType = evt.GetType().Name
        };
    }

    public static EventEntry FromParsingError(DateTime timestamp, string message) =>
                    new(
                        timestamp,
                        EventCategory.Error,
                        [new ErrorLabelSegment("ParsingError"), new LabelSegment(" "), new ValueSegment(message)],
                        [])
                    {
                        Level = LogLevel.Error,
                        Source = ClaudeSource,
                        MessageType = nameof(AgentParsingError)
                    };

    public static EventEntry FromInput(DateTime timestamp, string label, string detail) =>
                    new(
                        timestamp,
                        EventCategory.Input,
                        [new LabelSegment(label), new LabelSegment(" "), new ValueSegment($"\"{detail}\"")],
                        [])
                    {
                        Level = LogLevel.Info,
                        Source = ConversationSource,
                        MessageType = label
                    };

    /// Projects any bus occurrence onto a panel entry. Dead letters (activities carrying a reason),
    /// deliberate logs, the rich Claude stream events, user input, and otherwise-unknown messages each
    /// render distinctly, with a level that drives the default filter (the raw firehose sits at Debug).
    public static EventEntry FromBusActivity(BusActivity activity)
    {
        var timestamp = activity.Metadata.Timestamp.UtcDateTime;

        if (activity.Reason is not null)
        {
            return FromDeadLetter(timestamp, activity.PayloadType, activity.Reason);
        }

        return activity.Payload switch
        {
            LogEntry log => FromLog(timestamp, log),
            AgentParsingError error => FromParsingError(timestamp, error.Message),
            AgentStreamEvent streamEvent => FromStreamEvent(timestamp, streamEvent),
            SendPrompt prompt => FromInput(timestamp, "Prompt", prompt.Text),
            SendPermissionResponse response => FromInput(
                timestamp, "PermissionResponse", $"{response.RequestId} {(response.Allow ? "Allow" : "Deny")}"),
            _ => FromGeneric(timestamp, activity.PayloadType)
        };
    }

    private static EventEntry FromLog(DateTime timestamp, LogEntry log)
    {
        var category = log.Level >= LogLevel.Warn ? EventCategory.Error : EventCategory.Output;
        return new EventEntry(
            timestamp,
            category,
            [new ValueSegment(log.Message)],
            [])
        {
            Level = log.Level,
            Source = log.Source,
            MessageType = nameof(LogEntry)
        };
    }

    private static EventEntry FromDeadLetter(DateTime timestamp, Type payloadType, DeadLetterReason reason)
    {
        var level = DeadLetterLevel(reason);
        var category = level >= LogLevel.Warn ? EventCategory.Error : EventCategory.Output;
        return new EventEntry(
            timestamp,
            category,
            [new ErrorLabelSegment("DeadLetter"), new LabelSegment(" "), new ValueSegment(payloadType.Name), Dot, new SecondarySegment(DescribeReason(reason))],
            [])
        {
            Level = level,
            Source = BusSource,
            MessageType = payloadType.Name
        };
    }

    // A dead letter's severity is its reason, not its mere existence. A handler that threw is an error; a
    // full channel or an unanswered request is a warning; a fire-and-forget Send that simply had no
    // subscriber (the common WindowFocusChanged-style broadcast) and an expired message are benign info.
    private static LogLevel DeadLetterLevel(DeadLetterReason reason) => reason switch
    {
        HandlerFailed => LogLevel.Error,
        ChannelOverflow => LogLevel.Warn,
        RequestTimeout => LogLevel.Warn,
        _ => LogLevel.Info
    };

    private static EventEntry FromGeneric(DateTime timestamp, Type payloadType) =>
                    new(
                        timestamp,
                        EventCategory.Output,
                        [new LabelSegment(payloadType.Name)],
                        [])
                    {
                        Level = LogLevel.Debug,
                        Source = NamespaceLeaf(payloadType),
                        MessageType = payloadType.Name
                    };

    private static string DescribeReason(DeadLetterReason reason) => reason switch
    {
        NoSubscriber => "no-subscriber",
        ChannelOverflow overflow => $"channel-overflow ({overflow.PluginId})",
        HandlerFailed failed => $"handler-failed ({failed.PluginId})",
        Expired => "expired",
        RequestTimeout => "request-timeout",
        _ => reason.GetType().Name
    };

    private static string NamespaceLeaf(Type type)
    {
        var ns = type.Namespace;
        if (string.IsNullOrEmpty(ns))
        {
            return "";
        }

        var lastDot = ns.LastIndexOf('.');
        return lastDot >= 0 ? ns[(lastDot + 1)..] : ns;
    }
}
