using Verso.Abstractions;

namespace Verso.Extensions.Layouts;

/// <summary>
/// Minimal <see cref="ICellRenderer"/> implementation used as a fallback when no
/// renderer is registered for a cell's type. Provides <see cref="CellVisibilityHint.Content"/>
/// as the default visibility hint so unregistered cell types resolve to <see cref="CellVisibilityState.Visible"/>.
/// </summary>
internal sealed class ContentFallbackRenderer : ICellRenderer
{
    public string ExtensionId => "verso.internal.fallback-renderer";
    public string Name => "Fallback Renderer";
    public string Version => "1.0.0";
    public string? Author => null;
    public string? Description => null;

    public string CellTypeId => "";
    public string DisplayName => "Fallback";
    public bool CollapsesInputOnExecute => false;
    public CellVisibilityHint DefaultVisibility => CellVisibilityHint.Content;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context)
        => Task.FromResult(new RenderResult("text/plain", source));

    public Task<RenderResult> RenderOutputAsync(CellOutput output, ICellRenderContext context)
        => Task.FromResult(new RenderResult("text/plain", output.Content));

    public string? GetEditorLanguage() => null;
}
