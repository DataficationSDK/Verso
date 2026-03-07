using Verso.Abstractions;

namespace Verso.Http.CellType;

/// <summary>
/// Renders HTTP cell input and output.
/// Owned by <see cref="HttpCellType"/>; not independently registered.
/// </summary>
public sealed class HttpCellRenderer : ICellRenderer
{
    // --- IExtension ---

    public string ExtensionId => "verso.http.renderer.http";
    public string Name => "HTTP Renderer";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Renders HTTP request cells.";

    // --- ICellRenderer ---

    public string CellTypeId => "http";
    public string DisplayName => "HTTP";
    public bool CollapsesInputOnExecute => false;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context)
    {
        // No special input decoration needed
        return Task.FromResult(new RenderResult("text/plain", ""));
    }

    public Task<RenderResult> RenderOutputAsync(CellOutput output, ICellRenderContext context)
    {
        // Pass through — kernel already produces formatted HTML
        return Task.FromResult(new RenderResult(output.MimeType, output.Content));
    }

    public string? GetEditorLanguage() => "plaintext";
}
