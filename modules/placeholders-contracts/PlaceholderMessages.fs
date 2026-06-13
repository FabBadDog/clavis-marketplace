namespace FabioSoft.Contracts.Placeholders

open System.Collections.Generic

/// One discoverable placeholder a provider offers, e.g. key "git.branch". Kind is "value" - components
/// (bar, badge, limitPlane, microstat) and formats (uppercase, ...) are engine built-ins, not
/// provider-contributed. Sample and Description feed IntelliSense and the placeholder catalog.
[<Sealed>]
type PlaceholderDescriptor(key: string, kind: string, sample: string, description: string) =
    member _.Key = key
    member _.Kind = kind
    member _.Sample = sample
    member _.Description = description

/// A plugin announces the placeholder keys it can resolve (its namespace's tokens). Broadcast on
/// activation and again in response to PlaceholdersRequested, so the aggregating consumers (status line,
/// editor IntelliSense) build their catalog regardless of activation order.
[<Sealed>]
type RegisterPlaceholderProvider(providerId: string, descriptors: IReadOnlyList<PlaceholderDescriptor>) =
    member _.ProviderId = providerId
    member _.Descriptors = descriptors

/// Current values for a provider's placeholder keys, broadcast whenever they change (git on a refresh,
/// system metrics on each sample, the agent on stream events). Keys are the fully-qualified placeholder
/// names ("git.branch"); consumers merge the latest snapshot per provider and re-render.
[<Sealed>]
type PlaceholderSnapshot(providerId: string, values: IReadOnlyDictionary<string, string>) =
    member _.ProviderId = providerId
    member _.Values = values

/// Broadcast by an aggregating consumer on its activation; providers re-announce their descriptors and
/// re-publish their current snapshot. Mirrors PanelKindsRequested so activation order is irrelevant.
[<Sealed>]
type PlaceholdersRequested() =
    do ()
