namespace FabioSoft.Contracts.Resource

open System.ComponentModel
open System.IO
open System.Threading
open System.Threading.Tasks

type IResource =
    abstract Uri: string
    abstract OpenAsync: cancellationToken: CancellationToken -> ValueTask<Stream>

[<Sealed>]
type RegisterScheme(scheme: string, handlerPluginId: string) =
    member _.Scheme = scheme
    member _.HandlerPluginId = handlerPluginId

[<Sealed>]
[<Description("Load a resource by URI")>]
type LoadResource(uri: string) =
    member _.Uri = uri

/// Broker -> scheme-handler dispatch message. Lives in this shared contract assembly (not in a plugin
/// assembly) so it has a single type identity across plugin load contexts and the bus can route it.
[<Sealed>]
type LoadSchemeResource(scheme: string, uri: string) =
    member _.Scheme = scheme
    member _.Uri = uri

[<AbstractClass>]
type LoadResourceResult() = class end

[<Sealed>]
type ResourceLoaded(resource: IResource) =
    inherit LoadResourceResult()

    member _.Resource = resource

[<Sealed>]
type UnknownScheme(scheme: string) =
    inherit LoadResourceResult()

    member _.Scheme = scheme

[<Sealed>]
type LoadFailed(error: string) =
    inherit LoadResourceResult()

    member _.Error = error

[<Sealed>]
[<Description("Write content to a resource URI")>]
type WriteResource(uri: string, content: byte[]) =
    member _.Uri = uri
    member _.Content = content

[<Sealed>]
type WriteSchemeResource(scheme: string, uri: string, content: byte[]) =
    member _.Scheme = scheme
    member _.Uri = uri
    member _.Content = content

[<AbstractClass>]
type WriteResourceResult() = class end

[<Sealed>]
type WriteSucceeded(uri: string) =
    inherit WriteResourceResult()

    member _.Uri = uri

[<Sealed>]
type WriteUnknownScheme(scheme: string) =
    inherit WriteResourceResult()

    member _.Scheme = scheme

[<Sealed>]
type WriteFailed(error: string) =
    inherit WriteResourceResult()

    member _.Error = error
