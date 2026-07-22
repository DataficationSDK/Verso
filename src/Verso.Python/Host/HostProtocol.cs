using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Verso.Python.Host;

/// <summary>
/// Wire-protocol constants and JSON helpers for the Python host connection. Messages are
/// UTF-8, newline-delimited JSON objects; every message carries a <c>type</c>, requests
/// carry an <c>id</c>, and replies echo it as <c>req_id</c>.
/// </summary>
internal static class HostProtocol
{
    /// <summary>Protocol revision advertised in the handshake.</summary>
    public const int ProtocolVersion = 1;

    // Message types.
    public const string Hello = "hello";
    public const string HelloOk = "hello_ok";
    public const string Shutdown = "shutdown";

    // Field names.
    public const string TypeField = "type";
    public const string TokenField = "token";
    public const string ProtocolField = "protocol";
    public const string PythonField = "python";
    public const string CapabilitiesField = "capabilities";
    public const string IdField = "id";
    public const string ReqIdField = "req_id";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // Leave non-ASCII and HTML-sensitive characters unescaped, matching the Python side's
        // ensure_ascii=False. Control characters, including newline, are always escaped, so a
        // serialized frame never contains a raw newline.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Serialize a message to UTF-8 JSON bytes with no trailing newline.</summary>
    public static byte[] Serialize(JsonNode message)
        => Encoding.UTF8.GetBytes(message.ToJsonString(SerializerOptions));

    /// <summary>
    /// Parse a UTF-8 JSON frame into a <see cref="JsonObject"/>. Throws
    /// <see cref="PythonHostProtocolException"/> when the frame is not a JSON object.
    /// </summary>
    public static JsonObject Deserialize(ReadOnlySpan<byte> utf8)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(utf8);
        }
        catch (JsonException ex)
        {
            throw new PythonHostProtocolException($"Received a malformed JSON frame: {ex.Message}");
        }

        if (node is not JsonObject obj)
            throw new PythonHostProtocolException("Received a JSON frame that was not an object.");

        return obj;
    }

    /// <summary>Read the string <c>type</c> field, or null when it is absent or not a string.</summary>
    public static string? GetMessageType(JsonObject message) => TryGetString(message, TypeField);

    /// <summary>Read a string property without throwing when it is absent or not a string.</summary>
    public static string? TryGetString(JsonObject obj, string name)
        => obj.TryGetPropertyValue(name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            ? text
            : null;

    /// <summary>Read an integer property without throwing when it is absent or not a number.</summary>
    public static int? TryGetInt(JsonObject obj, string name)
        => obj.TryGetPropertyValue(name, out var node)
            && node is JsonValue value
            && value.TryGetValue<int>(out var number)
            ? number
            : null;
}
