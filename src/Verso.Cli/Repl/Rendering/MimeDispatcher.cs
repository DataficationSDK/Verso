using Spectre.Console;
using Spectre.Console.Rendering;
using Verso.Abstractions;
using Verso.Cli.Repl.Rendering.Renderers;

namespace Verso.Cli.Repl.Rendering;

/// <summary>
/// Routes a <see cref="CellOutput"/> to the renderer that best represents its
/// <see cref="CellOutput.MimeType"/> on a terminal. Unknown MIME types fall
/// back to a plain-text preview.
/// </summary>
public sealed class MimeDispatcher
{
    private readonly IAnsiConsole _console;
    private readonly bool _useColor;

    public MimeDispatcher(IAnsiConsole console, bool useColor)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _useColor = useColor;
    }

    /// <summary>Builds a renderable for the given cell output. Safe to compose into Panels and Rows.</summary>
    public IRenderable AsRenderable(CellOutput output, int cellCounter, int outputIndex, TruncationPolicy policy)
    {
        return output.MimeType switch
        {
            "text/plain" => PlainTextRenderer.AsRenderable(output, policy),
            "text/markdown" => MarkdownRenderer.AsRenderable(output, policy, _useColor),
            "text/html" => HtmlStripRenderer.AsRenderable(output, policy),
            "application/json" => JsonRenderer.AsRenderable(output, policy, _useColor),
            "text/csv" => CsvTableRenderer.AsRenderable(output, policy, _useColor),
            "image/png" or "image/jpeg" or "image/gif" or "image/webp" or "image/bmp" or "image/svg+xml"
                => ImagePlaceholderRenderer.AsRenderable(output, cellCounter, outputIndex, _useColor),
            "text/x-verso-mermaid" => PlainTextRenderer.AsRenderable(output, policy),
            _ => BuildUnknown(output, policy)
        };
    }

    /// <summary>Convenience wrapper that renders directly to the console — used by tests and simple paths.</summary>
    public void Render(CellOutput output, int cellCounter, int outputIndex, TruncationPolicy policy)
    {
        _console.Write(AsRenderable(output, cellCounter, outputIndex, policy));
        _console.WriteLine();
    }

    private IRenderable BuildUnknown(CellOutput output, TruncationPolicy policy)
    {
        var text = output.Content ?? string.Empty;
        var isProbablyText = text.Length == 0 || text.Take(256).All(c => c >= ' ' || c == '\n' || c == '\r' || c == '\t');
        if (!isProbablyText)
        {
            var msg = $"<output: {output.MimeType}, {text.Length} chars, binary>";
            return _useColor ? new Markup($"[dim]{Markup.Escape(msg)}[/]") : new Text(msg);
        }

        var header = $"<output: {output.MimeType}>";
        var headerRenderable = _useColor ? (IRenderable)new Markup($"[dim]{Markup.Escape(header)}[/]") : new Text(header);
        return new Rows(headerRenderable, new Text(policy.ClipLines(text)));
    }
}
