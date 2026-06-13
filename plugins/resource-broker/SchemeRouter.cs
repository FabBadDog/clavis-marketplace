using System;
using System.Collections.Concurrent;

namespace FabioSoft.Nucleus.Plugins.ResourceBroker;

public enum RouteKind
{
    Handled,
    Unknown,
    Invalid,
}

/// The result of routing a URI: a registered scheme is Handled (carrying the lowercased scheme), an
/// unregistered one is Unknown (carrying the scheme), and an unparsable URI is Invalid (carrying the parse
/// error message).
public sealed class RouteOutcome
{
    private RouteOutcome(RouteKind kind, string? scheme, string? message)
    {
        Kind = kind;
        Scheme = scheme;
        Message = message;
    }

    public RouteKind Kind { get; }

    public string? Scheme { get; }

    public string? Message { get; }

    public static RouteOutcome Handled(string scheme) => new(RouteKind.Handled, scheme, null);

    public static RouteOutcome Unknown(string scheme) => new(RouteKind.Unknown, scheme, null);

    public static RouteOutcome Invalid(string message) => new(RouteKind.Invalid, null, message);
}

/// The pure scheme-routing core of the broker: it holds the scheme -> handler-plugin registrations and, for
/// a given URI, decides whether a handler is registered for that scheme. No bus or I/O, so it is fully
/// unit-testable; the plugin maps the RouteOutcome onto bus sends.
public sealed class SchemeRouter
{
    private readonly ConcurrentDictionary<string, string> _handlers = new();

    public void Register(string scheme, string handlerPluginId) =>
        _handlers[scheme.ToLowerInvariant()] = handlerPluginId;

    public RouteOutcome Resolve(string uri)
    {
        string scheme;
        try
        {
            scheme = new Uri(uri).Scheme.ToLowerInvariant();
        }
        catch (UriFormatException ex)
        {
            return RouteOutcome.Invalid(ex.Message);
        }

        return _handlers.ContainsKey(scheme)
            ? RouteOutcome.Handled(scheme)
            : RouteOutcome.Unknown(scheme);
    }
}
