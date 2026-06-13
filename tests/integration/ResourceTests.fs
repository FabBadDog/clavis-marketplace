module FabioSoft.Clavis.IntegrationTests.ResourceTests

open System
open System.IO
open System.Text
open System.Threading
open Faqt
open Faqt.Operators
open FabioSoft.Clavis.TestKit
open FabioSoft.Contracts.Resource
open FabioSoft.Nucleus.Contracts
open FabioSoft.Nucleus.Plugins.ResourceBroker
open FabioSoft.Nucleus.Plugins.FileSystem
open Xunit

let private bootResources () =
    // Broker first so it is subscribed before the file-system plugin announces the "file" scheme.
    let broker = ResourceBrokerPlugin()
    let fileSystem = FileSystemPlugin()

    Harness.boot
        [ (fun bus -> broker.ActivateAsync(bus, ResourceBrokerConfig()))
          (fun bus -> fileSystem.ActivateAsync(bus, FileSystemConfig())) ]

let private waitForFileSchemeRegistered (harness: Harness) =
    harness.WaitFor<LogEntry>(fun entry -> entry.Message.Contains("registered scheme handler: file"))

[<Fact>]
let ``loads a file resource through the broker and file-system handler`` () =
    task {
        // Arrange
        let path = Path.Combine(Path.GetTempPath(), $"clavis-res-{Guid.NewGuid():N}.txt")
        File.WriteAllText(path, "hello from disk")
        let uri = Uri(path).AbsoluteUri
        use! harness = bootResources ()
        let! _ = waitForFileSchemeRegistered harness

        // Act
        harness.Send(LoadResource(uri))
        let! loaded = harness.WaitFor<ResourceLoaded>(fun _ -> true)

        // Assert
        let! stream = loaded.Resource.OpenAsync(CancellationToken.None)

        let content =
            use reader = new StreamReader(stream)
            reader.ReadToEnd()

        %content.Should().Be("hello from disk")
        File.Delete(path)
    }

[<Fact>]
let ``writes a file resource through the broker and file-system handler`` () =
    task {
        // Arrange
        let path = Path.Combine(Path.GetTempPath(), $"clavis-res-{Guid.NewGuid():N}.txt")
        let uri = Uri(path).AbsoluteUri
        use! harness = bootResources ()
        let! _ = waitForFileSchemeRegistered harness

        // Act
        harness.Send(WriteResource(uri, Encoding.UTF8.GetBytes "written by test"))
        let! _ = harness.WaitFor<WriteSucceeded>(fun _ -> true)

        // Assert
        %File.ReadAllText(path).Should().Be("written by test")
        File.Delete(path)
    }

[<Fact>]
let ``reports an unknown scheme when no handler is registered`` () =
    task {
        // Arrange
        use! harness = bootResources ()

        // Act
        harness.Send(LoadResource "gopher://example.com/x")
        let! unknown = harness.WaitFor<UnknownScheme>(fun _ -> true)

        // Assert
        %unknown.Scheme.Should().Be("gopher")
    }
