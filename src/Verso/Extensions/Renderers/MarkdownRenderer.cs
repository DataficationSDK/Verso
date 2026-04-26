using Markdig;
using Verso.Abstractions;
using Verso.Extensions.Utilities;

namespace Verso.Extensions.Renderers;

/// <summary>
/// Built-in Markdown cell renderer using Markdig with advanced extensions.
/// </summary>
[VersoExtension]
public sealed class MarkdownRenderer : ICellRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    // --- IExtension ---

    public string ExtensionId => "verso.renderer.markdown";
    public string Name => "Markdown Renderer";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Renders Markdown cells using Markdig.";

    // --- ICellRenderer ---

    public string CellTypeId => "markdown";
    public string DisplayName => "Markdown";
    public bool CollapsesInputOnExecute => true;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context)
    {
        string html;
        if (string.IsNullOrEmpty(source))
        {
            html = string.Empty;
        }
        else
        {
            var processed = VariableSubstitution.Apply(source, context.Variables);
            html = Markdown.ToHtml(processed, Pipeline);
        }

        return Task.FromResult(new RenderResult("text/html", html));
    }

    public Task<RenderResult> RenderOutputAsync(CellOutput output, ICellRenderContext context)
    {
        return Task.FromResult(new RenderResult(output.MimeType, output.Content));
    }

    public string? GetEditorLanguage() => "markdown";
}
