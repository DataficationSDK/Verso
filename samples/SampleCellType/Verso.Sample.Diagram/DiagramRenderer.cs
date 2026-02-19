using System.Web;
using Verso.Abstractions;

namespace Verso.Sample.Diagram;

/// <summary>
/// Renders diagram cells with syntax-highlighted input and SVG output.
/// Collapses input on execute to show only the rendered diagram.
/// </summary>
[VersoExtension]
public sealed class DiagramRenderer : ICellRenderer
{
    public string ExtensionId => "com.verso.sample.diagram.renderer";
    public string Name => "Diagram Renderer";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Renders diagram cells with SVG flowchart output.";
    public string CellTypeId => "diagram";
    public string DisplayName => "Diagram";
    public bool CollapsesInputOnExecute => true;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context)
    {
        var escaped = HttpUtility.HtmlEncode(source);
        var html = "<pre style=\"font-family:monospace;padding:8px;background:#f8f8f8;border-radius:4px;border:1px solid #e0e0e0;\">" +
                   $"<code>{escaped}</code></pre>";
        return Task.FromResult(new RenderResult("text/html", html));
    }

    public Task<RenderResult> RenderOutputAsync(CellOutput output, ICellRenderContext context)
    {
        if (output.IsError)
        {
            var errorHtml = $"<div style=\"color:#d32f2f;padding:4px 8px;\">{HttpUtility.HtmlEncode(output.Content)}</div>";
            return Task.FromResult(new RenderResult("text/html", errorHtml));
        }

        // SVG output: wrap in a container div
        var html = $"<div class=\"verso-diagram-output\" style=\"padding:8px;text-align:center;\">{output.Content}</div>";
        return Task.FromResult(new RenderResult("text/html", html));
    }

    public string? GetEditorLanguage() => null;
}
