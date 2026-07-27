using System;
using System.Text.Json;

namespace FabioSoft.Nucleus.Plugins.Conversation;

/// What a chat panel instance persists in its opaque per-instance blob: which workspace it belongs to and
/// which chat inside that workspace it shows. A restored panel re-attaches to that chat instead of being
/// re-seeded, which is what makes the chat an ordinary panel kind rather than a host-owned singleton.
///
/// Guid.Empty in either field means "whichever one is current" - the state of every panel saved before
/// workspaces existed, and of a chat panel opened by hand today.
public sealed record ChatPanelState(Guid WorkspaceId, Guid ChatId)
{
    public static ChatPanelState Empty { get; } = new(Guid.Empty, Guid.Empty);

    /// Read a saved blob. Anything unreadable - empty, truncated, not JSON, a field that is not a Guid -
    /// yields Empty, so a corrupted blob restores a fresh chat rather than failing the whole layout restore.
    public static ChatPanelState Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return new ChatPanelState(ReadGuid(document, "workspaceId"), ReadGuid(document, "chatId"));
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public string Serialize() =>
        JsonSerializer.Serialize(new { workspaceId = WorkspaceId, chatId = ChatId });

    private static Guid ReadGuid(JsonDocument document, string property) =>
        document.RootElement.ValueKind == JsonValueKind.Object
        && document.RootElement.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && Guid.TryParse(value.GetString(), out var parsed)
            ? parsed
            : Guid.Empty;
}
