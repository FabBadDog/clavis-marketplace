module FabioSoft.Nucleus.Kernel.Tests.ResourcePluginTests

open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open FabioSoft.Nucleus.Bus
open FabioSoft.Nucleus.Contracts
open FabioSoft.Contracts.Resource
open FabioSoft.Nucleus.Plugins.FileSystem
open FabioSoft.Nucleus.Plugins.Http
open FabioSoft.Nucleus.Plugins.ResourceBroker
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``resource broker dispatches to file scheme handler`` () =
    task {
        // Arrange
        let tempFile = Path.GetTempFileName()
        File.WriteAllText(tempFile, "hello from file")

        try
            use bus = new Bus(BusConfig.defaultConfig)
            let broker = ResourceBrokerPlugin()
            let fileSystem = FileSystemPlugin()

            use! _ = broker.ActivateAsync(bus :> IBus, ResourceBrokerConfig())
            do! Task.Delay(50)
            use! _ = fileSystem.ActivateAsync(bus :> IBus, FileSystemConfig())
            do! Task.Delay(50)

            let resultReceived = TaskCompletionSource<LoadResourceResult>()
            use _ = bus.Subscribe<LoadResourceResult>(Func<_, _>(fun result ->
                resultReceived.TrySetResult(result) |> ignore
                Task.CompletedTask))

            // Act
            let fileUri = Uri(tempFile).AbsoluteUri
            bus.Send(LoadResource(fileUri))

            // Assert
            let! result = resultReceived.Task.WaitAsync(TimeSpan.FromSeconds(5.0))
            match result with
            | :? ResourceLoaded as loaded ->
                use! stream = loaded.Resource.OpenAsync(CancellationToken.None)
                use reader = new StreamReader(stream)
                let! content = reader.ReadToEndAsync()
                % content.Should().Be("hello from file")
            | _ ->
                failwith $"Expected ResourceLoaded, got {result.GetType().Name}"
        finally
            try File.Delete(tempFile) with _ -> ()
    }

[<Fact>]
let ``resource broker returns unknown scheme for unregistered scheme`` () =
    task {
        // Arrange
        use bus = new Bus(BusConfig.defaultConfig)
        let broker = ResourceBrokerPlugin()
        use! _ = broker.ActivateAsync(bus :> IBus, ResourceBrokerConfig())

        let resultReceived = TaskCompletionSource<LoadResourceResult>()
        use _ = bus.Subscribe<LoadResourceResult>(Func<_, _>(fun result ->
            resultReceived.TrySetResult(result) |> ignore
            Task.CompletedTask))

        // Act
        bus.Send(LoadResource("ftp://example.com/file.txt"))

        // Assert
        let! result = resultReceived.Task.WaitAsync(TimeSpan.FromSeconds(5.0))
        % (result :? UnknownScheme).Should().BeTrue()
    }

[<Fact>]
let ``file system returns load failed for missing file`` () =
    task {
        // Arrange
        use bus = new Bus(BusConfig.defaultConfig)
        let broker = ResourceBrokerPlugin()
        let fileSystem = FileSystemPlugin()

        use! _ = broker.ActivateAsync(bus :> IBus, ResourceBrokerConfig())
        do! Task.Delay(50)
        use! _ = fileSystem.ActivateAsync(bus :> IBus, FileSystemConfig())
        do! Task.Delay(50)

        let resultReceived = TaskCompletionSource<LoadResourceResult>()
        use _ = bus.Subscribe<LoadResourceResult>(Func<_, _>(fun result ->
            resultReceived.TrySetResult(result) |> ignore
            Task.CompletedTask))

        // Act
        bus.Send(LoadResource("file:///C:/nonexistent/file.txt"))

        // Assert
        let! result = resultReceived.Task.WaitAsync(TimeSpan.FromSeconds(5.0))
        % (result :? LoadFailed).Should().BeTrue()
    }

[<Fact>]
let ``file system writes content via broker and reads it back`` () =
    task {
        // Arrange
        let tempFile = Path.Combine(Path.GetTempPath(), $"nucleus-write-{Guid.NewGuid():N}.txt")

        try
            use bus = new Bus(BusConfig.defaultConfig)
            let broker = ResourceBrokerPlugin()
            let fileSystem = FileSystemPlugin()

            use! _ = broker.ActivateAsync(bus :> IBus, ResourceBrokerConfig())
            do! Task.Delay(50)
            use! _ = fileSystem.ActivateAsync(bus :> IBus, FileSystemConfig())
            do! Task.Delay(50)

            let resultReceived = TaskCompletionSource<WriteResourceResult>()
            use _ = bus.Subscribe<WriteResourceResult>(Func<_, _>(fun result ->
                resultReceived.TrySetResult(result) |> ignore
                Task.CompletedTask))

            // Act
            let fileUri = Uri(tempFile).AbsoluteUri
            bus.Send(WriteResource(fileUri, Encoding.UTF8.GetBytes("written via broker")))

            // Assert
            let! result = resultReceived.Task.WaitAsync(TimeSpan.FromSeconds(5.0))
            % (result :? WriteSucceeded).Should().BeTrue()
            % (File.ReadAllText(tempFile)).Should().Be("written via broker")
        finally
            try File.Delete(tempFile) with _ -> ()
    }

[<Fact>]
let ``write to unregistered scheme returns write unknown scheme`` () =
    task {
        // Arrange
        use bus = new Bus(BusConfig.defaultConfig)
        let broker = ResourceBrokerPlugin()
        use! _ = broker.ActivateAsync(bus :> IBus, ResourceBrokerConfig())

        let resultReceived = TaskCompletionSource<WriteResourceResult>()
        use _ = bus.Subscribe<WriteResourceResult>(Func<_, _>(fun result ->
            resultReceived.TrySetResult(result) |> ignore
            Task.CompletedTask))

        // Act
        bus.Send(WriteResource("ftp://example.com/file.txt", Array.empty<byte>))

        // Assert
        let! result = resultReceived.Task.WaitAsync(TimeSpan.FromSeconds(5.0))
        % (result :? WriteUnknownScheme).Should().BeTrue()
    }

[<Fact>]
let ``http scheme does not support writing`` () =
    task {
        // Arrange
        use bus = new Bus(BusConfig.defaultConfig)
        let broker = ResourceBrokerPlugin()
        let http = HttpPlugin()

        use! _ = broker.ActivateAsync(bus :> IBus, ResourceBrokerConfig())
        do! Task.Delay(50)
        use! _ = http.ActivateAsync(bus :> IBus, HttpConfig())
        do! Task.Delay(50)

        let resultReceived = TaskCompletionSource<WriteResourceResult>()
        use _ = bus.Subscribe<WriteResourceResult>(Func<_, _>(fun result ->
            resultReceived.TrySetResult(result) |> ignore
            Task.CompletedTask))

        // Act
        bus.Send(WriteResource("https://example.com/resource", Array.empty<byte>))

        // Assert
        let! result = resultReceived.Task.WaitAsync(TimeSpan.FromSeconds(5.0))
        % (result :? WriteFailed).Should().BeTrue()
    }
