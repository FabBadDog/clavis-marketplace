module FabioSoft.Nucleus.EventsPanel.Tests.EventEntryFactoryTests

open System
open FabioSoft.Nucleus.Contracts
open FabioSoft.Contracts.Session
open FabioSoft.Nucleus.Plugins.EventsPanel
open Faqt
open Faqt.Operators
open Xunit

let private sessionId = Guid.NewGuid()
let private now = DateTime.UtcNow

type private Probe = { Value: int }

let private metadata () =
    MessageMetadata(Guid.NewGuid(), System.Nullable<Guid>(), DateTimeOffset.UtcNow, "", System.Nullable<DateTimeOffset>())

let private noReason = Unchecked.defaultof<DeadLetterReason>

let private logActivity level source message =
    let log = { Level = level; Source = source; Message = message; Timestamp = DateTimeOffset.UtcNow }
    BusActivity(metadata (), typeof<LogEntry>, box log, noReason)

let private logEntry level source message =
    EventEntryFactory.FromBusActivity(logActivity level source message)

module FromStreamEvent =

    [<Fact>]
    let ``Init event produces output entry with model`` () =

        // Act
        let entry = EventEntryFactory.FromStreamEvent(now, AgentInit(sessionId, "s1", "opus-4", Array.empty<string>))

        // Assert
        %entry.Category.Should().Be(EventCategory.Output)
        %(entry.Segments.Count > 0).Should().BeTrue()

    [<Fact>]
    let ``ToolUse event has continuation for input`` () =

        // Act
        let entry = EventEntryFactory.FromStreamEvent(now, AgentToolUse(sessionId, "Write", "tu1", "file content", "file content"))

        // Assert
        %entry.ContinuationLines.Count.Should().Be(1)
        %entry.ContinuationLines[0].Label.Should().Be("input")

    [<Fact>]
    let ``SessionEnded event includes exit code`` () =

        // Act
        let entry = EventEntryFactory.FromStreamEvent(now, AgentSessionEnded(sessionId, 1, "crash"))

        // Assert
        %entry.Category.Should().Be(EventCategory.Output)
        %(entry.ContinuationLines.Count > 0).Should().BeTrue()

    [<Fact>]
    let ``Usage event includes token counts`` () =

        // Act
        let entry = EventEntryFactory.FromStreamEvent(now, AgentUsage(sessionId, 100_000, 500, 50_000))

        // Assert
        %entry.Category.Should().Be(EventCategory.Output)
        %entry.ContinuationLines.Count.Should().Be(0)

    [<Fact>]
    let ``HookComplete event includes stdout and stderr continuations`` () =

        // Act
        let entry = EventEntryFactory.FromStreamEvent(now, AgentHookComplete(sessionId, "h1", "hook", "SessionStart", "success", Nullable 0, "output text", ""))

        // Assert
        %entry.ContinuationLines.Count.Should().Be(2)
        %entry.ContinuationLines[0].Label.Should().Be("stdout")
        %entry.ContinuationLines[1].Label.Should().Be("stderr")

    [<Fact>]
    let ``PermissionRequest event includes input continuation`` () =

        // Act
        let entry = EventEntryFactory.FromStreamEvent(now, AgentPermissionRequest(sessionId, "r1", "Bash", "tu1", "rm -rf /", "", "", "No matching permission rule"))

        // Assert
        %(entry.ContinuationLines.Count >= 1).Should().BeTrue()
        %entry.ContinuationLines[0].Label.Should().Be("input")

module FromParsingError =

    [<Fact>]
    let ``produces error entry`` () =

        // Act
        let entry = EventEntryFactory.FromParsingError(now, "Missing field: type")

        // Assert
        %entry.Category.Should().Be(EventCategory.Error)

module FromInput =

    [<Fact>]
    let ``produces input entry`` () =

        // Act
        let entry = EventEntryFactory.FromInput(now, "Prompt", "hello world")

        // Assert
        %entry.Category.Should().Be(EventCategory.Input)

module FormatDelta =

    [<Theory>]
    [<InlineData(500, "+ 500ms")>]
    [<InlineData(5000, "+ 5s")>]
    [<InlineData(65000, "+ 1m5s")>]
    [<InlineData(3600000, "+ 1h")>]
    let ``formats time delta correctly`` (milliseconds: int, expected: string) =

        // Arrange
        let start = DateTime(2024, 1, 1)
        let timestamp = start.AddMilliseconds(float milliseconds)

        // Act
        let result = EventEntryFactory.FormatDelta(start, timestamp)

        // Assert
        %result.Should().Be(expected)

module EventsPanelViewModel =

    [<Fact>]
    let ``AddEntry shows and counts the entry under the default ALL floor`` () =

        // Arrange
        let viewModel = EventsPanelViewModel()
        let entry = EventEntry(now, EventCategory.Output, [||], [||])

        // Act
        viewModel.AddEntry(entry)

        // Assert
        %viewModel.TotalCount.Should().Be(1)
        %viewModel.FilteredEntryViewModels.Count.Should().Be(1)

    [<Fact>]
    let ``severity floor shows only entries at or above it`` () =

        // Arrange
        let viewModel = EventsPanelViewModel()
        viewModel.AddEntry(logEntry LogLevel.Info "A" "info")
        viewModel.AddEntry(logEntry LogLevel.Error "A" "boom")

        // Act
        viewModel.SetSeverityFloor(LogLevel.Error)

        // Assert
        %viewModel.FilteredEntryViewModels.Count.Should().Be(1)
        %viewModel.TotalCount.Should().Be(2)

    [<Fact>]
    let ``default floor is ALL so nothing is hidden`` () =

        // Arrange
        let viewModel = EventsPanelViewModel()

        // Act
        viewModel.AddEntry(logEntry LogLevel.Trace "A" "trace")
        viewModel.AddEntry(logEntry LogLevel.Debug "A" "debug")
        viewModel.AddEntry(logEntry LogLevel.Info "A" "info")

        // Assert
        %viewModel.FilteredEntryViewModels.Count.Should().Be(3)
        %viewModel.CounterLabel.Should().Be("3 of 3")

    [<Fact>]
    let ``Clear empties entries and filtered rows`` () =

        // Arrange
        let viewModel = EventsPanelViewModel()
        viewModel.AddEntry(logEntry LogLevel.Info "A" "a")
        viewModel.AddEntry(logEntry LogLevel.Info "B" "b")

        // Act
        viewModel.Clear()

        // Assert
        %viewModel.TotalCount.Should().Be(0)
        %viewModel.FilteredEntryViewModels.Count.Should().Be(0)

    [<Fact>]
    let ``sets session start on first entry`` () =

        // Arrange
        let viewModel = EventsPanelViewModel()

        // Act
        viewModel.AddEntry(EventEntry(now, EventCategory.Output, [||], [||]))

        // Assert
        %(viewModel.SessionStartLabel.Contains("started")).Should().BeTrue()

module FromBusActivity =

    [<Fact>]
    let ``info log becomes an output entry at its level and source`` () =

        // Act
        let entry = logEntry LogLevel.Info "Kernel" "started"

        // Assert
        %entry.Category.Should().Be(EventCategory.Output)
        %entry.Level.Should().Be(LogLevel.Info)
        %entry.Source.Should().Be("Kernel")

    [<Fact>]
    let ``warn log becomes an error entry`` () =

        // Act
        let entry = logEntry LogLevel.Warn "Http" "write unsupported"

        // Assert
        %entry.Category.Should().Be(EventCategory.Error)

    [<Fact>]
    let ``no-subscriber dead letter becomes a benign info entry sourced from the bus`` () =

        // Arrange
        let activity = BusActivity(metadata (), typeof<Probe>, box { Value = 1 }, NoSubscriber())

        // Act
        let entry = EventEntryFactory.FromBusActivity(activity)

        // Assert
        %entry.Level.Should().Be(LogLevel.Info)
        %entry.Category.Should().Be(EventCategory.Output)
        %entry.Source.Should().Be("Bus")

    [<Fact>]
    let ``unknown message becomes a debug generic entry`` () =

        // Arrange
        let activity = BusActivity(metadata (), typeof<Probe>, box { Value = 2 }, noReason)

        // Act
        let entry = EventEntryFactory.FromBusActivity(activity)

        // Assert
        %entry.Level.Should().Be(LogLevel.Debug)
        %entry.Category.Should().Be(EventCategory.Output)

    [<Fact>]
    let ``stream event via activity is an output entry`` () =

        // Arrange
        let activity = BusActivity(metadata (), typeof<AgentStreamEvent>, box (AgentInit(sessionId, "s", "m", Array.empty<string>)), noReason)

        // Act
        let entry = EventEntryFactory.FromBusActivity(activity)

        // Assert
        %entry.Category.Should().Be(EventCategory.Output)

    [<Fact>]
    let ``prompt via activity is an input entry`` () =

        // Arrange
        let activity = BusActivity(metadata (), typeof<SendPrompt>, box (SendPrompt(sessionId, "hello")), noReason)

        // Act
        let entry = EventEntryFactory.FromBusActivity(activity)

        // Assert
        %entry.Category.Should().Be(EventCategory.Input)

    [<Fact>]
    let ``permission response via activity is an input entry`` () =

        // Arrange
        let activity = BusActivity(metadata (), typeof<SendPermissionResponse>, box (SendPermissionResponse(sessionId, "r1", true)), noReason)

        // Act
        let entry = EventEntryFactory.FromBusActivity(activity)

        // Assert
        %entry.Category.Should().Be(EventCategory.Input)

    [<Fact>]
    let ``dead letter level follows its reason`` () =

        // Arrange
        let cases: (DeadLetterReason * LogLevel) list =
            [ NoSubscriber(), LogLevel.Info
              Expired(), LogLevel.Info
              ChannelOverflow("plugin"), LogLevel.Warn
              RequestTimeout(), LogLevel.Warn
              HandlerFailed("plugin", InvalidOperationException("boom")), LogLevel.Error ]

        // Act & Assert
        for reason, expected in cases do
            let activity = BusActivity(metadata (), typeof<Probe>, box { Value = 0 }, reason)
            let entry = EventEntryFactory.FromBusActivity(activity)
            %entry.Level.Should().Be(expected)
            %entry.Source.Should().Be("Bus")

module CompositeFilter =

    [<Fact>]
    let ``raising the floor hides lower levels`` () =

        // Arrange
        let viewModel = EventsPanelViewModel()
        viewModel.AddEntry(logEntry LogLevel.Debug "A" "noisy")
        viewModel.AddEntry(logEntry LogLevel.Warn "A" "careful")

        // Act
        viewModel.SetSeverityFloor(LogLevel.Warn)

        // Assert
        %viewModel.FilteredEntryViewModels.Count.Should().Be(1)

    [<Fact>]
    let ``search filters by summary text`` () =

        // Arrange
        let viewModel = EventsPanelViewModel()
        viewModel.AddEntry(logEntry LogLevel.Info "A" "alpha")
        viewModel.AddEntry(logEntry LogLevel.Info "A" "beta")

        // Act
        viewModel.SetSearch("alph")

        // Assert
        %viewModel.FilteredEntryViewModels.Count.Should().Be(1)

    [<Fact>]
    let ``search matches the source name`` () =

        // Arrange
        let viewModel = EventsPanelViewModel()
        viewModel.AddEntry(logEntry LogLevel.Info "Kernel" "started")
        viewModel.AddEntry(logEntry LogLevel.Info "Http" "request")

        // Act
        viewModel.SetSearch("kernel")

        // Assert
        %viewModel.FilteredEntryViewModels.Count.Should().Be(1)

    [<Fact>]
    let ``search matches the category name`` () =

        // Arrange
        let viewModel = EventsPanelViewModel()
        viewModel.AddEntry(logEntry LogLevel.Info "A" "ok")      // category Output
        viewModel.AddEntry(logEntry LogLevel.Error "A" "broke")  // category Error

        // Act
        viewModel.SetSearch("error")

        // Assert
        %viewModel.FilteredEntryViewModels.Count.Should().Be(1)

    [<Fact>]
    let ``backspace shortens the search`` () =

        // Arrange
        let viewModel = EventsPanelViewModel()
        viewModel.AppendSearch("al")
        viewModel.AppendSearch("x")

        // Act
        viewModel.Backspace()

        // Assert
        %viewModel.SearchText.Should().Be("al")

    [<Fact>]
    let ``entries beyond capacity are trimmed oldest-first`` () =

        // Arrange
        let viewModel = EventsPanelViewModel(MaxEntries = 3)

        // Act
        for i in 1..5 do
            viewModel.AddEntry(logEntry LogLevel.Info "A" $"message {i}")

        // Assert
        %viewModel.TotalCount.Should().Be(3)
        %viewModel.FilteredEntryViewModels.Count.Should().Be(3)

module EventEntryViewModelLabels =

    [<Theory>]
    [<InlineData(LogLevel.Trace, "TRACE")>]
    [<InlineData(LogLevel.Debug, "DEBUG")>]
    [<InlineData(LogLevel.Info, "INFO")>]
    [<InlineData(LogLevel.Warn, "WARN")>]
    [<InlineData(LogLevel.Error, "ERROR")>]
    let ``level label maps each level`` (level: LogLevel, expected: string) =

        // Arrange
        let entry = logEntry level "Src" "message"

        // Act
        let viewModel = EventEntryViewModel(entry, System.Nullable<DateTime>())

        // Assert
        %viewModel.LevelLabel.Should().Be(expected)
        %viewModel.SourceLabel.Should().Be("Src")
