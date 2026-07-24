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

    /// <summary>
    /// Request id reserved for events the subprocess raises on its own behalf rather than in
    /// response to a request, such as a failure while replaying bootstrap configuration.
    /// </summary>
    public const int UnsolicitedRequestId = 0;

    // Message types: handshake and lifecycle.
    public const string Hello = "hello";
    public const string HelloOk = "hello_ok";
    public const string Shutdown = "shutdown";
    public const string Interrupt = "interrupt";

    // Message types: execution.
    public const string Execute = "execute";
    public const string ExecuteResult = "execute_result";
    public const string Stream = "stream";
    public const string Display = "display";
    public const string InputRequest = "input_request";
    public const string InputReply = "input_reply";

    // Field names: envelope.
    public const string TypeField = "type";
    public const string TokenField = "token";
    public const string ProtocolField = "protocol";
    public const string PythonField = "python";
    public const string CapabilitiesField = "capabilities";
    public const string ConfigField = "config";
    public const string IdField = "id";
    public const string ReqIdField = "req_id";

    // Field names: bootstrap configuration carried on hello_ok.
    public const string DefaultImportsField = "default_imports";
    public const string StartupCodeField = "startup_code";

    // Field names: execute request and result.
    public const string CodeField = "code";
    public const string InjectField = "inject";
    public const string PublishField = "publish";
    public const string StatusField = "status";
    public const string ResultField = "result";
    public const string ErrorField = "error";
    public const string VariablesField = "variables";
    public const string MimeField = "mime";
    public const string DataField = "data";
    public const string NameField = "name";
    public const string MessageField = "message";
    public const string TracebackField = "traceback";

    // Field names: stream, display, and input events.
    public const string TextField = "text";
    public const string PromptField = "prompt";
    public const string PasswordField = "password";
    public const string ValueField = "value";
    public const string DisplayIdField = "display_id";

    // Execute result status values.
    public const string StatusOk = "ok";
    public const string StatusError = "error";
    public const string StatusCancelled = "cancelled";

    // Stream names.
    public const string StreamStdout = "stdout";
    public const string StreamStderr = "stderr";

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

    /// <summary>
    /// Whether a message type terminates a request and should complete its awaiting caller.
    /// Events such as <c>stream</c> also carry a <c>req_id</c>, but they correlate without
    /// concluding the request, so type is what distinguishes the two.
    /// </summary>
    public static bool IsReply(string? messageType) => messageType is ExecuteResult;

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
