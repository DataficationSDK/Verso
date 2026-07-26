namespace Verso.Abstractions;

/// <summary>
/// Represents the output produced by executing a notebook cell.
/// </summary>
/// <param name="MimeType">The MIME type of <paramref name="Content"/> (e.g. "text/plain", "text/html").</param>
/// <param name="Content">The output content in the format indicated by <paramref name="MimeType"/>.</param>
/// <param name="IsError">
/// Whether this output is an execution failure: an uncaught exception, a compiler error, or a
/// validation error that stopped the cell. Setting it marks the whole cell failed, which the status
/// icon, the command line summary, and the process exit code all follow. Text is not a failure
/// merely because it arrived on standard error, so record that with <see cref="Channel"/> instead.
/// Defaults to <see langword="false"/>.
/// </param>
/// <param name="ErrorName">The error type or exception name when <paramref name="IsError"/> is <see langword="true"/>.</param>
/// <param name="ErrorStackTrace">The stack trace associated with the error, if available.</param>
public sealed record CellOutput(
    string MimeType,
    string Content,
    bool IsError = false,
    string? ErrorName = null,
    string? ErrorStackTrace = null)
{
    /// <summary>
    /// Which of the kernel's text streams this output came from, when it came from one at all.
    /// Null for outputs that are not stream text, such as a rendered image or an exception report.
    /// Declared here rather than as a constructor parameter so that adding it does not change the
    /// signature existing compiled extensions call.
    /// </summary>
    public OutputChannel? Channel { get; init; }

    /// <summary>Creates a <c>text/plain</c> output.</summary>
    public static CellOutput Plain(string content) => new("text/plain", content);

    /// <summary>Creates a <c>text/html</c> output.</summary>
    public static CellOutput Html(string content) => new("text/html", content);

    /// <summary>Creates an <c>image/svg+xml</c> output.</summary>
    public static CellOutput Svg(string content) => new("image/svg+xml", content);

    /// <summary>Creates an <c>image/png</c> output from a Base64-encoded string.</summary>
    public static CellOutput Png(string base64) => new("image/png", base64);

    /// <summary>Creates an <c>image/jpeg</c> output from a Base64-encoded string.</summary>
    public static CellOutput Jpeg(string base64) => new("image/jpeg", base64);

    /// <summary>Creates an <c>image/gif</c> output from a Base64-encoded string.</summary>
    public static CellOutput Gif(string base64) => new("image/gif", base64);

    /// <summary>Creates an <c>image/webp</c> output from a Base64-encoded string.</summary>
    public static CellOutput WebP(string base64) => new("image/webp", base64);

    /// <summary>Creates an <c>image/bmp</c> output from a Base64-encoded string.</summary>
    public static CellOutput Bmp(string base64) => new("image/bmp", base64);

    /// <summary>Creates an <c>application/json</c> output.</summary>
    public static CellOutput Json(string content) => new("application/json", content);

    /// <summary>Creates a <c>text/csv</c> output.</summary>
    public static CellOutput Csv(string content) => new("text/csv", content);

    /// <summary>Creates a <c>text/x-verso-mermaid</c> output.</summary>
    public static CellOutput Mermaid(string content) => new("text/x-verso-mermaid", content);

    /// <summary>
    /// The MIME type of a self-contained widget document: a whole page, carrying its own state
    /// and loader, that draws itself once it is running in a browser.
    /// <para>
    /// It is kept apart from <c>text/html</c> because it is a document rather than a fragment,
    /// and because its loader resolves paths against the location of the page it runs in. A
    /// surface showing one has to give it a frame with a real address; a surface that cannot
    /// says so rather than pasting the markup inline.
    /// </para>
    /// </summary>
    public const string WidgetMimeType = "text/x-verso-widget";

    /// <summary>Creates a <c>text/x-verso-widget</c> output.</summary>
    public static CellOutput Widget(string document) => new(WidgetMimeType, document);

    /// <summary>
    /// The drawable part of a widget document: whatever its body element contains.
    /// <para>
    /// A surface that is itself a page at a real address, such as an exported file, can put this
    /// straight into its own body instead of giving the widget a frame, and the widget's loader
    /// then resolves against that page. A document this cannot make sense of is returned whole,
    /// since a browser ignores a nested <c>html</c> element rather than choking on it.
    /// </para>
    /// </summary>
    public static string WidgetBody(string? document)
    {
        if (string.IsNullOrEmpty(document)) return string.Empty;

        var open = document.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return document;

        var start = document.IndexOf('>', open);
        if (start < 0) return document;

        var end = document.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return end <= start ? document[(start + 1)..] : document[(start + 1)..end];
    }

    /// <summary>
    /// Creates a progress output, which every surface renders as a progress indicator rather than
    /// as text.
    /// <para>
    /// Report progress by writing this to the same output block repeatedly through
    /// <c>UpdateOutputAsync</c>, so the notebook shows one indicator that advances rather than a
    /// column of stale ones. Send a final value with <paramref name="done"/> set so the indicator
    /// settles instead of appearing to stall.
    /// </para>
    /// </summary>
    /// <param name="activity">What is being done, shown as the label.</param>
    /// <param name="status">Optional detail about the current step.</param>
    /// <param name="percent">Completion from 0 to 100, or null when it cannot be known.</param>
    /// <param name="done">Whether the work has finished.</param>
    public static CellOutput Progress(
        string activity, string? status = null, int? percent = null, bool done = false)
    {
        var clamped = percent is null ? null : (int?)Math.Clamp(percent.Value, 0, 100);

        var payload = new System.Text.Json.Nodes.JsonObject
        {
            ["activity"] = activity,
            ["status"] = status,
            ["percent"] = clamped,
            ["done"] = done
        };

        return new CellOutput(ProgressMimeType, payload.ToJsonString());
    }

    /// <summary>The MIME type carrying a progress payload.</summary>
    public const string ProgressMimeType = "text/x-verso-progress";

    /// <summary>
    /// Renders a progress payload as a single line of text, for surfaces with no live indicator to
    /// show: an exported page, a terminal, or anything read after the work has finished. Content
    /// that cannot be read is returned unchanged rather than throwing, because a progress report is
    /// never worth failing on.
    /// </summary>
    public static string DescribeProgress(string content)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(content);
            var root = document.RootElement;

            var activity = root.TryGetProperty("activity", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.String
                ? a.GetString() ?? "Working"
                : "Working";

            var status = root.TryGetProperty("status", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.String
                ? s.GetString()
                : null;

            var done = root.TryGetProperty("done", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.True;

            int? percent = root.TryGetProperty("percent", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.Number
                ? Math.Clamp(p.GetInt32(), 0, 100)
                : null;

            var text = activity;
            if (!string.IsNullOrEmpty(status))
                text += $" ({status})";

            if (done)
                text += " 100%";
            else if (percent is not null)
                text += $" {percent}%";

            return text;
        }
        catch (System.Text.Json.JsonException)
        {
            return content;
        }
    }

    /// <summary>Creates a <c>text/plain</c> output carrying text a kernel wrote to standard output.</summary>
    public static CellOutput Stdout(string content) =>
        new("text/plain", content) { Channel = OutputChannel.Stdout };

    /// <summary>
    /// Creates a <c>text/plain</c> output carrying text a kernel wrote to standard error. This does
    /// not fail the cell; use <see cref="Error"/> for that.
    /// </summary>
    public static CellOutput Stderr(string content) =>
        new("text/plain", content) { Channel = OutputChannel.Stderr };

    /// <summary>Creates an error output with the specified message and optional error name.</summary>
    public static CellOutput Error(string message, string? errorName = null, string? stackTrace = null) =>
        new("text/plain", message, IsError: true, ErrorName: errorName, ErrorStackTrace: stackTrace);
}
