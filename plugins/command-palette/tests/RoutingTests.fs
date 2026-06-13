module FabioSoft.Nucleus.CommandPalette.Tests.RoutingTests

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open FabioSoft.Nucleus.Bus
open FabioSoft.Nucleus.Contracts
open FabioSoft.Contracts.Session
open FabioSoft.Contracts.Workspace
open FabioSoft.Nucleus.Plugins.CommandPalette
open Faqt
open Faqt.Operators
open Xunit

let private catalog : IReadOnlyList<Type> =
    [| typeof<LogEntry>; typeof<ApplicationShutdown>; typeof<FullRestartRequested>; typeof<OpenPanel>; typeof<TogglePanel> |] :> _

let private noAliases : IReadOnlyDictionary<string, string> = readOnlyDict []
let private aliases (pairs: (string * string) list) : IReadOnlyDictionary<string, string> = readOnlyDict pairs
let private noClaude : IReadOnlyList<string> = Array.empty<string> :> _
let private claude (names: string list) : IReadOnlyList<string> = List.toArray names :> _
let private timestamp = "Timestamp=2020-01-01T00:00:00Z"

[<Fact>]
let ``route constructs a message by type name`` () =

    // Act
    let outcome =
        CommandRouter.Route(
            $"LogEntry Level=Debug Source=s Message=m {timestamp}", noAliases, catalog, noClaude, Placeholders.Default)

    // Assert
    %(outcome :? SendBusMessage).Should().BeTrue()
    %((outcome :?> SendBusMessage).Message :? LogEntry).Should().BeTrue()

[<Fact>]
let ``route expands an alias and applies the invocation's positional argument`` () =

    // Arrange
    let withAlias = aliases [ "log-debug", $"LogEntry Level=Debug Source=s {timestamp}" ]

    // Act
    let outcome = CommandRouter.Route("log-debug hello", withAlias, catalog, noClaude, Placeholders.Default)

    // Assert
    let message = (outcome :?> SendBusMessage).Message :?> LogEntry
    %message.Message.Should().Be("hello")
    %message.Level.Should().Be(LogLevel.Debug)

[<Fact>]
let ``route expands a built-in alias to a parameterless message`` () =

    // Act
    let outcome = CommandRouter.Route("exit", AliasCatalog.BuiltIns, catalog, noClaude, Placeholders.Default)

    // Assert
    %((outcome :?> SendBusMessage).Message :? ApplicationShutdown).Should().BeTrue()

[<Fact>]
let ``built-in log alias sets the user source and carries the typed message`` () =

    // Act
    let outcome = CommandRouter.Route("log-info hello", AliasCatalog.BuiltIns, catalog, noClaude, Placeholders.Default)

    // Assert
    let message = (outcome :?> SendBusMessage).Message :?> LogEntry
    %message.Level.Should().Be(LogLevel.Info)
    %message.Source.Should().Be("user")
    %message.Message.Should().Be("hello")

[<Fact>]
let ``open-kind alias resolves to an OpenPanel for that kind`` () =

    // Arrange
    let withAlias = aliases [ "open-git-log", "OpenPanel git-log" ]

    // Act
    let outcome = CommandRouter.Route("open-git-log", withAlias, catalog, noClaude, Placeholders.Default)

    // Assert
    let message = (outcome :?> SendBusMessage).Message :?> OpenPanel
    %message.Kind.Should().Be("git-log")

[<Fact>]
let ``toggle-kind alias resolves to a TogglePanel for that kind`` () =

    // Arrange
    let withAlias = aliases [ "toggle-git-log", "TogglePanel git-log" ]

    // Act
    let outcome = CommandRouter.Route("toggle-git-log", withAlias, catalog, noClaude, Placeholders.Default)

    // Assert
    let message = (outcome :?> SendBusMessage).Message :?> TogglePanel
    %message.Kind.Should().Be("git-log")

[<Fact>]
let ``route passes an agent command through as a slash prompt`` () =

    // Act
    let outcome = CommandRouter.Route("clear extra args", noAliases, catalog, claude [ "clear" ], Placeholders.Default)

    // Assert
    %((outcome :?> SendAgentPrompt).Text).Should().Be("/clear extra args")

[<Fact>]
let ``route returns NoMatch for unknown input`` () =

    // Act
    let outcome = CommandRouter.Route("totallyUnknown", noAliases, catalog, noClaude, Placeholders.Default)

    // Assert
    %(outcome :? NoMatch).Should().BeTrue()

[<Fact>]
let ``route returns an error for an unconvertible argument`` () =

    // Act
    let outcome =
        CommandRouter.Route(
            $"LogEntry Level=Nope Source=s Message=m {timestamp}", noAliases, catalog, noClaude, Placeholders.Default)

    // Assert
    %(outcome :? RouteError).Should().BeTrue()

[<Fact>]
let ``route reports an alias that references an unknown message`` () =

    // Act
    let outcome =
        CommandRouter.Route("bad", aliases [ "bad", "NoSuchMessage" ], catalog, noClaude, Placeholders.Default)

    // Assert
    %(outcome :? RouteError).Should().BeTrue()

[<Fact>]
let ``suggestions include messages aliases and the external commands`` () =

    // Arrange
    let external : IReadOnlyList<CommandItem> =
        [| CommandItem.FromAgentCommand("clear", "Clear the screen (user)")
           CommandItem.FromAgentCommand("compact", "Agent command") |] :> _

    // Act
    let items = CommandSuggestions.Build("", catalog, aliases [ "exit", "ApplicationShutdown" ], external)

    // Assert
    let clear = items |> Seq.find (fun item -> item.Name = "clear")
    %clear.Kind.Should().Be(CommandKind.Skill)
    %clear.Description.Should().Be("Clear the screen")
    let compact = items |> Seq.find (fun item -> item.Name = "compact")
    %compact.Kind.Should().Be(CommandKind.Agent)
    %(items |> Seq.exists (fun item -> item.Name = "LogEntry" && item.Kind = CommandKind.Message)).Should().BeTrue()
    %(items |> Seq.exists (fun item -> item.Name = "exit" && item.Kind = CommandKind.Alias)).Should().BeTrue()

[<Fact>]
let ``suggestions filter by the typed name`` () =

    // Arrange
    let noExternal : IReadOnlyList<CommandItem> = Array.empty<CommandItem> :> _

    // Act
    let items = CommandSuggestions.Build("LogEnt", catalog, noAliases, noExternal)

    // Assert
    %(items |> Seq.forall (fun item -> item.Name.Contains("LogEnt", StringComparison.OrdinalIgnoreCase))).Should().BeTrue()
    %(items.Count > 0).Should().BeTrue()

[<Fact>]
let ``bus sender dispatches under the concrete message type`` () =
    task {
        // Arrange
        use bus = new Bus(BusConfig.defaultConfig)
        let received = TaskCompletionSource<LogEntry>()
        use _ =
            bus.Subscribe<LogEntry>(Func<_, _>(fun entry ->
                received.TrySetResult(entry) |> ignore
                Task.CompletedTask))

        let entry =
            { Level = LogLevel.Info; Source = "src"; Message = "m"; Timestamp = DateTimeOffset.UtcNow }

        // Act
        BusSender.Send(bus, box entry)

        // Assert
        let! result = received.Task.WaitAsync(TimeSpan.FromSeconds(5.0))
        %result.Source.Should().Be("src")
    }
