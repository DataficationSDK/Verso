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

    // Message types: editor assistance.
    public const string Complete = "complete";
    public const string CompleteReply = "complete_reply";
    public const string Hover = "hover";
    public const string HoverReply = "hover_reply";
    public const string Diagnostics = "diagnostics";
    public const string DiagnosticsReply = "diagnostics_reply";

    // Message types: package discovery.
    public const string ScanImports = "scan_imports";
    public const string ScanReply = "scan_reply";

    // Message types: widget comms. Both directions carry the same body; only the envelope
    // differs, because one is an event the subprocess raises and the other is something the
    // host tells it. Neither concludes a request.
    public const string Comm = "comm";
    public const string CommMsg = "comm_msg";

    /// <summary>
    /// Asks for a current page for one widget, named by <see cref="WidgetIdField"/>. Answered
    /// between cells, so an answer is bounded by the asking side rather than waited on.
    /// </summary>
    public const string WidgetSnapshot = "widget_snapshot";

    public const string WidgetSnapshotReply = "widget_snapshot_reply";

    // Message types: cross-kernel projection. A projection is set up and taken down by request,
    // and once it exists a value crosses in either direction as an event with nothing to answer:
    // both sides already know which name changed, and neither waits on the other.

    /// <summary>Asks for a trait to be projected into the shared variables under a name.</summary>
    public const string Bind = "bind";

    /// <summary>Asks for a projection to stop, named by <see cref="NameField"/>.</summary>
    public const string Unbind = "unbind";

    /// <summary>Answers either of the two, carrying the outcome under <see cref="BindingField"/>.</summary>
    public const string BindReply = "bind_reply";

    /// <summary>Carries a value another kernel wrote onto the trait it was projected from.</summary>
    public const string BindSet = "bind_set";

    /// <summary>Carries a changed trait out to the shared variables. Raised, never asked for.</summary>
    public const string BindUpdate = "bind_update";

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
    public const string EnableShellEscapesField = "enable_shell_escapes";
    public const string VariablePublishLimitField = "variable_publish_limit_bytes";
    public const string JediToolsPathField = "jedi_tools_path";
    public const string WidgetAssetSourceField = "widget_asset_source";

    // Field names: execute request and result.
    public const string CodeField = "code";
    public const string InjectField = "inject";
    public const string PublishField = "publish";

    /// <summary>
    /// Whether a widget shown by this cell can be given a channel to the view drawing it. Sent
    /// per cell because it describes the surface the output is going to rather than the session:
    /// the same interpreter answers a notebook being edited and a file being baked.
    /// </summary>
    public const string LiveWidgetsField = "live_widgets";

    /// <summary>
    /// Names the widget a display payload was made from, carried only when that widget asked to
    /// be live. It is what a later request for a fresh page names, and it is the same model id
    /// the view already addresses the widget by, so nothing is invented to carry it.
    /// </summary>
    public const string WidgetIdField = "widget_id";

    public const string StatusField = "status";
    public const string ResultField = "result";
    public const string ErrorField = "error";
    public const string VariablesField = "variables";
    public const string OversizedField = "oversized";
    public const string MimeField = "mime";
    public const string DataField = "data";
    public const string NameField = "name";
    public const string MessageField = "message";
    public const string TracebackField = "traceback";

    /// <summary>
    /// The module a <c>ModuleNotFoundError</c> could not import, carried on the error payload. A
    /// dynamic import is invisible to a pre-execution scan by definition, so this is the only
    /// signal that one was reached.
    /// </summary>
    public const string MissingModuleField = "missing_module";

    // Field names: package discovery request and reply.
    public const string RequirementsField = "requirements";
    public const string MissingField = "missing";
    public const string UnsatisfiedField = "unsatisfied";
    public const string ModuleField = "module";
    public const string OptionalField = "optional";

    // Field names: stream, display, and input events.
    public const string TextField = "text";
    public const string PromptField = "prompt";
    public const string PasswordField = "password";
    public const string ValueField = "value";
    public const string DisplayIdField = "display_id";

    /// <summary>
    /// A widget document asking for a channel to be opened for it. Never reaches a notebook: the
    /// managing side opens the channel and writes the ordinary widget output, holding the channel
    /// on it. What travels under this type is byte for byte what travels under the other, so a
    /// host with nowhere to send simply writes it out as it is.
    /// </summary>
    public const string LiveWidgetMime = "text/x-verso-widget-live";

    // Field names: widget comms.

    /// <summary>
    /// The output channel a comm message belongs to. Absent on a message the subprocess raised
    /// without being asked, which belongs to the session rather than to any one view.
    /// </summary>
    public const string ChannelIdField = "channel_id";

    /// <summary>The widget model a comm message is about, which is also the comm's own id.</summary>
    public const string CommIdField = "comm_id";

    /// <summary>
    /// Which of the three comm messages this is: <c>comm_open</c>, <c>comm_msg</c>, or
    /// <c>comm_close</c>. Carried inside the frame rather than as its type, so the host relays
    /// the body without reading it.
    /// </summary>
    public const string MsgTypeField = "msg_type";

    /// <summary>
    /// A view's own name for one message it sent, carried back on the acknowledgement so the view
    /// can tell which of its messages has been applied.
    /// </summary>
    /// <remarks>
    /// The widget front end sends one change at a time and holds the rest until the one in flight
    /// has been dealt with, merging what accumulates meanwhile into a single later message. That
    /// is what keeps a dragged slider from putting a message per frame in front of the
    /// interpreter, and it only works if something tells it when a message has landed.
    /// </remarks>
    public const string MsgIdField = "msg_id";

    public const string MetadataField = "metadata";
    public const string BuffersField = "buffers";
    public const string TargetNameField = "target_name";

    // Field names: cross-kernel projection.

    /// <summary>The expression naming the object a trait belongs to, read in the cell namespace.</summary>
    public const string ExpressionField = "expression";

    /// <summary>The trait being projected.</summary>
    public const string TraitField = "trait";

    /// <summary>The outcome of a bind or unbind request, carried on the reply.</summary>
    public const string BindingField = "binding";

    /// <summary>
    /// The name of a projection this one supersedes. The same trait of the same object is only
    /// ever projected once, so binding it again under a second name moves it rather than
    /// doubling it, and this is what lets the managing side drop the entry it held.
    /// </summary>
    public const string ReplacedField = "replaced";

    /// <summary>Whether an unbind request found a projection to stop.</summary>
    public const string RemovedField = "removed";

    /// <summary>Why a bind request was refused, written for the author of the cell.</summary>
    public const string ReasonField = "reason";

    /// <summary>
    /// The <see cref="MsgTypeField"/> value of an acknowledgement. Not one of the three comm
    /// messages: it belongs to this transport, and it exists because the front end is waiting for
    /// the signal a Jupyter kernel sends on a channel this one does not have.
    /// </summary>
    public const string CommAck = "comm_ack";

    // Field names: completion, hover, and diagnostic requests and replies.
    public const string CursorField = "cursor";
    public const string HistoryField = "history";
    public const string CompletionsField = "completions";
    public const string InsertField = "insert";
    public const string DocField = "doc";
    public const string HoverField = "hover";
    public const string ContentField = "content";
    public const string DiagnosticsField = "diagnostics";
    public const string LineField = "line";
    public const string ColumnField = "column";
    public const string EndLineField = "end_line";
    public const string EndColumnField = "end_column";

    /// <summary>
    /// A diagnostic's rule identifier. Shares its wire name with nothing else: it lives inside a
    /// diagnostics item, where <see cref="CodeField"/> (the source of a request) never appears.
    /// </summary>
    public const string DiagnosticCodeField = "code";

    /// <summary>
    /// A completion's kind as the analyzer reported it, before it is mapped to the vocabulary the
    /// editors use. Carries jedi's own type name when jedi answered the request.
    /// </summary>
    public const string KindField = "kind";

    // Execute result status values.
    public const string StatusOk = "ok";
    public const string StatusError = "error";
    public const string StatusCancelled = "cancelled";

    // Stream names.
    public const string StreamStdout = "stdout";
    public const string StreamStderr = "stderr";

    /// <summary>
    /// Advertised in the handshake when the interpreter's own environment already provides the
    /// analysis library, which is the managing side's cue not to provision a copy of it.
    /// </summary>
    public const string JediCapability = "jedi";

    /// <summary>
    /// Advertised in the handshake when the subprocess can answer import scans. A host script
    /// predating this phase ignores the request, so the capability is what keeps the managing
    /// side from waiting out a deadline for a reply that will never come.
    /// </summary>
    public const string ScanCapability = "scan_imports";

    /// <summary>
    /// Advertised in the handshake when the subprocess can carry widget comm traffic. A host
    /// script predating this exchanges nothing and drops what it is sent, so this is what lets
    /// the managing side offer a live view only when there is something on the other end of it.
    /// </summary>
    public const string CommCapability = "comm";

    /// <summary>
    /// Advertised in the handshake when the subprocess can project a trait into the shared
    /// variables. A host script predating this ignores the request, so this is what keeps a
    /// magic command from waiting out a deadline for a reply that will never come.
    /// </summary>
    public const string BindCapability = "bind";

    /// <summary>
    /// Nesting ceiling for a frame. Both sides reduce a variable to at most 100 levels, and the
    /// message envelope wraps that, so the transport has to allow more than the default 64 or a
    /// legitimately deep value would be rejected as a malformed frame. Frames come from this
    /// kernel's own subprocess over an authenticated loopback socket, so the wider limit is not
    /// an exposure.
    /// </summary>
    public const int MaxJsonDepth = 128;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // Leave non-ASCII and HTML-sensitive characters unescaped, matching the Python side's
        // ensure_ascii=False. Control characters, including newline, are always escaped, so a
        // serialized frame never contains a raw newline.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        MaxDepth = MaxJsonDepth,
    };

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = MaxJsonDepth };

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
            node = JsonNode.Parse(utf8, nodeOptions: null, DocumentOptions);
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
    public static bool IsReply(string? messageType)
        => messageType is ExecuteResult or CompleteReply or HoverReply or DiagnosticsReply
            or ScanReply or WidgetSnapshotReply or BindReply;

    /// <summary>
    /// Whether a message belongs to the cell that is running rather than to the session. Two
    /// handlers split the traffic between them: the per-execution handler answers these and lives
    /// only as long as a cell, and the session handler, attached whether or not a cell is running,
    /// answers everything else. This is the one place the split is written down, so a type named
    /// here has to be answered there, and a type answered there but missing here reaches neither.
    /// </summary>
    public static bool IsExecutionScoped(string? messageType)
        => messageType is Stream or Display or InputRequest;

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
