using System.Text.Json;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.AgentGateway;

/// The whitelist + deny-list backing the raw send_message tool. Contract messages are F# classes with
/// positional constructors, and IBus.Send dispatches by the COMPILE-TIME type, so a boxed object would
/// never reach subscribers. Each entry therefore constructs its concrete type and calls bus.Send with
/// that static type. Only registered, navigation-level messages can be sent; lifecycle/teardown types
/// are deliberately absent and the deny-list rejects them by name with a distinct error.
///
/// Public (not internal) only so it is unit-testable without InternalsVisibleTo, which the repo forbids.
public sealed class SendableMessages
{
    public readonly record struct Result(bool Ok, string Message);

    // Messages that must never be sendable from the agent, even if a future edit adds them to the map.
    private static readonly HashSet<string> Denied = new(StringComparer.Ordinal)
    {
        "ApplicationShutdown",
        "CloseWindow",
        "CloseActiveWindow",
        "DisposeSession",
        "UnloadPlugin",
        "LoadPlugin",
    };

    private readonly Dictionary<string, Action<IBus, JsonElement>> _factories;

    public SendableMessages()
    {
        _factories = new Dictionary<string, Action<IBus, JsonElement>>(StringComparer.Ordinal)
        {
            ["UserSubmittedPrompt"] = (bus, json) => bus.Send(new UserSubmittedPrompt(RequireString(json, "prompt"))),
            ["UserAborted"] = (bus, _) => bus.Send(new UserAborted()),
            ["FocusInputRequested"] = (bus, _) => bus.Send(new FocusInputRequested()),
            ["ToggleCommandPalette"] = (bus, _) => bus.Send(new ToggleCommandPalette()),
            ["ToggleShortcutHelp"] = (bus, _) => bus.Send(new ToggleShortcutHelp()),
            ["SummonClavis"] = (bus, _) => bus.Send(new SummonClavis()),
            ["OpenPanel"] = (bus, json) => bus.Send(new OpenPanel(RequireString(json, "kind"))),
            ["TogglePanel"] = (bus, json) => bus.Send(new TogglePanel(RequireString(json, "kind"))),
            ["CloseActivePanel"] = (bus, _) => bus.Send(new CloseActivePanel()),
            ["ClosePanel"] = (bus, json) => bus.Send(new ClosePanel(RequireGuid(json, "instanceId"))),
            ["ShowSlideIn"] = (bus, json) => bus.Send(new ShowSlideIn(RequireGuid(json, "instanceId"))),
            ["OpenConversation"] = (bus, _) => bus.Send(new OpenConversation()),
            ["RestorePanel"] = (bus, json) => bus.Send(new RestorePanel(
                RequireGuid(json, "instanceId"), RequireString(json, "kind"), RequireString(json, "savedState"))),
        };
    }

    public IEnumerable<string> SupportedTypes => _factories.Keys.Order();

    public Result Send(IBus bus, string typeName, string jsonPayload)
    {
        if (Denied.Contains(typeName))
        {
            return new Result(false, $"'{typeName}' is denied: it is a lifecycle/teardown message the agent may not send.");
        }

        if (!_factories.TryGetValue(typeName, out var factory))
        {
            return new Result(false, $"'{typeName}' is not a supported message. Supported: {string.Join(", ", SupportedTypes)}.");
        }

        JsonElement json;
        try
        {
            json = string.IsNullOrWhiteSpace(jsonPayload)
                ? default
                : JsonDocument.Parse(jsonPayload).RootElement;
        }
        catch (JsonException exception)
        {
            return new Result(false, $"jsonPayload is not valid JSON: {exception.Message}");
        }

        try
        {
            factory(bus, json);
            return new Result(true, $"sent {typeName}");
        }
        catch (ArgumentException exception)
        {
            return new Result(false, exception.Message);
        }
    }

    private static string RequireString(JsonElement json, string property)
    {
        if (json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()!;
        }

        throw new ArgumentException($"jsonPayload must contain a string '{property}'.");
    }

    private static Guid RequireGuid(JsonElement json, string property)
    {
        if (json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"jsonPayload must contain a GUID string '{property}'.");
    }
}
