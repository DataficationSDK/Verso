using Verso.Abstractions;
using Verso.Resources;

namespace Verso.Extensions.Renderers;

/// <summary>
/// Renders Mermaid diagram cells. Collapses the input editor on execute to show only the rendered diagram.
/// Owned by <see cref="CellTypes.MermaidCellType"/>; not independently registered.
/// </summary>
public sealed class MermaidCellRenderer : ICellRenderer
{
    // --- IExtension ---

    public string ExtensionId => "verso.renderer.mermaid";
    public string Name => Strings.Renderer_Mermaid;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => Strings.Renderer_Mermaid_Description;

    // --- ICellRenderer ---

    public string CellTypeId => "mermaid";
    // Format and product names read the same in every language.
    public string DisplayName => "Mermaid";
    public bool CollapsesInputOnExecute => true;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context)
    {
        return Task.FromResult(new RenderResult("text/x-verso-mermaid", source ?? string.Empty));
    }

    public Task<RenderResult> RenderOutputAsync(CellOutput output, ICellRenderContext context)
    {
        return Task.FromResult(new RenderResult(output.MimeType, output.Content));
    }

    public string? GetEditorLanguage() => "mermaid";
}
