using Verso.Abstractions;

namespace Verso.Extensions.Renderers;

/// <summary>
/// Renders HTML cells. Collapses the input editor on execute to show only the rendered output.
/// Owned by <see cref="CellTypes.HtmlCellType"/>; not independently registered.
/// </summary>
public sealed class HtmlCellRenderer : ICellRenderer
{
    // --- IExtension ---

    public string ExtensionId => "verso.renderer.html";
    public string Name => "HTML Renderer";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Renders HTML cells with input collapse on execute.";

    // --- ICellRenderer ---

    public string CellTypeId => "html";
    public string DisplayName => "HTML";
    public bool CollapsesInputOnExecute => true;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context)
    {
        return Task.FromResult(new RenderResult("text/html", source ?? string.Empty));
    }

    public Task<RenderResult> RenderOutputAsync(CellOutput output, ICellRenderContext context)
    {
        return Task.FromResult(new RenderResult(output.MimeType, output.Content));
    }

    public string? GetEditorLanguage() => "html";
}
