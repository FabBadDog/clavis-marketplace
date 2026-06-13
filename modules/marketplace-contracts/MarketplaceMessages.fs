namespace FabioSoft.Contracts.Marketplace

open System.ComponentModel
open System.Collections.Generic

// Cross-plugin messages for the marketplace subsystem. Like every other contract group this is a cold
// module compiled on launch into the Default ALC; the kernel loads it before the MarketplacePlugin
// (its hot consumer) activates, so no host reference is needed. The interactive command/result messages
// (install, update, add-marketplace, authoring) are added alongside their consumer; what lives here is the
// stable long-running-operation lifecycle that any operation reports through.

/// Broadcast as a long-running marketplace operation moves through its phases (resolving, fetching,
/// compiling, placing, loading). `OperationId` correlates the progress, completion, and failure of one
/// operation; `Phase` is a short stage label; `Detail` is a human-readable line.
[<Sealed>]
[<Description("Progress of a running marketplace operation")>]
type MarketplaceProgress(operationId: string, phase: string, detail: string) =
    member _.OperationId = operationId
    member _.Phase = phase
    member _.Detail = detail

/// Broadcast when a marketplace operation finishes successfully. `Summary` describes what changed.
[<Sealed>]
[<Description("A marketplace operation completed successfully")>]
type MarketplaceCompleted(operationId: string, summary: string) =
    member _.OperationId = operationId
    member _.Summary = summary

/// Broadcast when a marketplace operation fails. `Reason` is the user-facing explanation (a dependency
/// conflict, an authentication failure, a missing item, and so on).
[<Sealed>]
[<Description("A marketplace operation failed")>]
type MarketplaceFailed(operationId: string, reason: string) =
    member _.OperationId = operationId
    member _.Reason = reason

/// Broadcast when an install or upgrade staged a module that can only take effect after a
/// restart (modules enter the Default ALC at boot and cannot be hot-loaded). The host surfaces
/// this so the user knows a restart is needed to complete the change.
[<Sealed>]
[<Description("A change was staged that requires a restart to take effect")>]
type RestartRequired(reason: string) =
    member _.Reason = reason

// --- registry: query and manage the registered marketplaces ---

[<Sealed>]
type MarketplaceSummary(id: string, sourceKind: string, sourceDetail: string) =
    member _.Id = id
    member _.SourceKind = sourceKind
    member _.SourceDetail = sourceDetail

/// Request the registered marketplaces; answered with MarketplaceList.
[<Sealed>]
[<Description("List the registered marketplaces")>]
type ListMarketplaces() =
    class
    end

[<Sealed>]
type MarketplaceList(marketplaces: IReadOnlyList<MarketplaceSummary>) =
    member _.Marketplaces = marketplaces

[<Sealed>]
[<Description("Register a marketplace by source (github owner/repo, git URL, or local path)")>]
type AddMarketplace(source: string) =
    member _.Source = source

[<Sealed>]
[<Description("Remove a registered marketplace")>]
type RemoveMarketplace(id: string) =
    member _.Id = id

// --- discovery: what a marketplace offers ---

[<Sealed>]
type AvailableItemSummary(name: string, marketplace: string, version: string, kind: string, description: string) =
    member _.Name = name
    member _.Marketplace = marketplace
    member _.Version = version
    member _.Kind = kind
    member _.Description = description

/// Request the items available across registered marketplaces matching a query; answered with
/// MarketplaceSearchResult.
[<Sealed>]
[<Description("Search the registered marketplaces for installable items")>]
type SearchMarketplace(query: string) =
    member _.Query = query

[<Sealed>]
type MarketplaceSearchResult(items: IReadOnlyList<AvailableItemSummary>) =
    member _.Items = items

// --- install / update / uninstall ---

[<Sealed>]
[<Description("Install a plugin (and its dependency closure) from a marketplace")>]
type InstallPlugin(name: string, marketplace: string, versionRange: string) =
    member _.Name = name
    member _.Marketplace = marketplace
    member _.VersionRange = versionRange

[<Sealed>]
[<Description("Update an installed plugin to the highest compatible version")>]
type UpdatePlugin(name: string) =
    member _.Name = name

[<Sealed>]
[<Description("Uninstall a plugin and prune any modules it alone required")>]
type UninstallPlugin(name: string) =
    member _.Name = name
