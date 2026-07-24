using System.Text.Encodings.Web;
using System.Text.Json;

namespace FabioSoft.Nucleus.Plugins.Conversation;

/// Formats the expand-to-detail body: a JSON string is re-indented (two spaces) so it reads as a block
/// instead of a cramped one-liner; anything that is not JSON (plain text output, log lines, a partial
/// fragment) is returned unchanged so nothing is ever lost or garbled. The relaxed encoder keeps unicode
/// (umlauts, ·, €) as-is rather than escaping it to \uXXXX.
public static class JsonPretty
{
    private static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Format(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? "";
        }

        var trimmed = text.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return text;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, PrettyOptions);
        }
        catch (JsonException)
        {
            return text;
        }
    }
}
