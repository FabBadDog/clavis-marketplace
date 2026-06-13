module FabioSoft.Nucleus.Kernel.Tests.ConfigurationPluginTests

open System
open System.IO
open System.Threading.Tasks
open FabioSoft.Nucleus.Bus
open FabioSoft.Nucleus.Contracts
open FabioSoft.Contracts.Services
open FabioSoft.Nucleus.Plugins.Configuration
open Faqt
open Faqt.Operators
open Xunit

let private createTempConfigDirectory () =
    let directory = Path.Combine(Path.GetTempPath(), $"nucleus-config-{Guid.NewGuid():N}")
    Directory.CreateDirectory(directory) |> ignore
    directory

let private cleanupDirectory (directory: string) =
    try Directory.Delete(directory, true) with _ -> ()

[<Fact>]
let ``configuration plugin validates empty directory`` () =
    task {
        // Arrange
        let plugin = ConfigurationPlugin()

        // Act
        let! result = plugin.ValidateConfigAsync(ConfigurationConfig(""))

        // Assert
        % (result :? ConfigInvalid).Should().BeTrue()
    }

[<Fact>]
let ``configuration plugin validates valid directory`` () =
    task {
        // Arrange
        let plugin = ConfigurationPlugin()

        // Act
        let! result = plugin.ValidateConfigAsync(ConfigurationConfig(@"C:\temp"))

        // Assert
        % (result :? ConfigValid).Should().BeTrue()
    }

[<Fact>]
let ``configuration plugin returns not found for missing config`` () =
    task {
        // Arrange
        let configDirectory = createTempConfigDirectory ()

        try
            use bus = new Bus(BusConfig.defaultConfig)
            let plugin = ConfigurationPlugin()
            let config = ConfigurationConfig(configDirectory)
            use! _ = plugin.ActivateAsync(bus :> IBus, config)

            let resultReceived = TaskCompletionSource<ConfigResult>()
            use _ = bus.Subscribe<ConfigResult>(Func<_, _>(fun result ->
                resultReceived.TrySetResult(result) |> ignore
                Task.CompletedTask))

            // Act
            bus.Send(GetConfig("NonExistent"))

            // Assert
            let! result = resultReceived.Task.WaitAsync(TimeSpan.FromSeconds(5.0))
            % (result :? ConfigNotFound).Should().BeTrue()
        finally
            cleanupDirectory configDirectory
    }

[<Fact>]
let ``configuration plugin saves and loads config`` () =
    task {
        // Arrange
        let configDirectory = createTempConfigDirectory ()

        try
            use bus = new Bus(BusConfig.defaultConfig)
            let plugin = ConfigurationPlugin()
            let config = ConfigurationConfig(configDirectory)
            use! _ = plugin.ActivateAsync(bus :> IBus, config)

            let savedReceived = TaskCompletionSource<ConfigSaved>()
            use _ = bus.Subscribe<ConfigSaved>(Func<_, _>(fun result ->
                savedReceived.TrySetResult(result) |> ignore
                Task.CompletedTask))

            let configYaml = "setting: value\ncount: 42\n"

            // Act
            bus.Send(SaveConfig("TestPlugin", configYaml))
            let! _ = savedReceived.Task.WaitAsync(TimeSpan.FromSeconds(5.0))

            let resultReceived = TaskCompletionSource<ConfigResult>()
            use _ = bus.Subscribe<ConfigResult>(Func<_, _>(fun result ->
                resultReceived.TrySetResult(result) |> ignore
                Task.CompletedTask))

            bus.Send(GetConfig("TestPlugin"))

            // Assert
            % File.Exists(Path.Combine(configDirectory, "TestPlugin.yaml")).Should().BeTrue()
            let! result = resultReceived.Task.WaitAsync(TimeSpan.FromSeconds(5.0))
            match result with
            | :? ConfigFound as found ->
                % found.PluginId.Should().Be("TestPlugin")
                % found.RawConfig.Should().Be(configYaml)
            | _ ->
                failwith "Expected ConfigFound"
        finally
            cleanupDirectory configDirectory
    }

[<Fact>]
let ``configuration plugin publishes config changed on save`` () =
    task {
        // Arrange
        let configDirectory = createTempConfigDirectory ()

        try
            use bus = new Bus(BusConfig.defaultConfig)
            let plugin = ConfigurationPlugin()
            let config = ConfigurationConfig(configDirectory)
            use! _ = plugin.ActivateAsync(bus :> IBus, config)

            let changedReceived = TaskCompletionSource<ConfigChanged>()
            use _ = bus.Subscribe<ConfigChanged>(Func<_, _>(fun result ->
                changedReceived.TrySetResult(result) |> ignore
                Task.CompletedTask))

            let configYaml = "key: value\n"

            // Act
            bus.Send(SaveConfig("MyPlugin", configYaml))

            // Assert
            let! changed = changedReceived.Task.WaitAsync(TimeSpan.FromSeconds(5.0))
            % changed.PluginId.Should().Be("MyPlugin")
            % changed.RawConfig.Should().Be(configYaml)
        finally
            cleanupDirectory configDirectory
    }

[<Fact>]
let ``configuration plugin defaults its config directory under the user profile`` () =

    // Arrange
    let plugin = ConfigurationPlugin()
    let expected =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".clavis", "config")

    // Act
    let config = plugin.DefaultConfig

    // Assert
    %config.Should().NotBeNull()
    %config.ConfigDirectory.Should().Be(expected)
