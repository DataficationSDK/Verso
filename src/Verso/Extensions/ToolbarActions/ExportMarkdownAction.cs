using Verso.Abstractions;
using Verso.Export;

namespace Verso.Extensions.ToolbarActions;

/// <summary>
/// Toolbar action that exports the notebook as a Markdown document.
/// </summary>
[VersoExtension]
public sealed class ExportMarkdownAction : IToolbarAction
{
    // --- IExtension ---

    public string ExtensionId => "verso.action.export-markdown";
    public string Name => "Export Markdown";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Exports the notebook as a Markdown document.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IToolbarAction ---

    public string ActionId => "verso.action.export-markdown";
    public string DisplayName => "Markdown";
    public string? Icon => null;
    public ToolbarPlacement Placement => ToolbarPlacement.ExportMenu;
    public int Order => 65;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
    {
        var enabled = context.NotebookCells.Count > 0;
        return Task.FromResult(enabled);
    }

    public async Task ExecuteAsync(IToolbarActionContext context)
    {
        var title = context.NotebookMetadata.Title;

        ExportOptions? options = null;
        var layoutId = context.ActiveLayoutId;
        if (layoutId is not null)
        {
            var layout = context.ExtensionHost.GetLayouts()
                .FirstOrDefault(l => string.Equals(l.LayoutId, layoutId, StringComparison.OrdinalIgnoreCase));
            if (layout is not null)
                options = new ExportOptions(layoutId, layout.SupportedVisibilityStates, context.ExtensionHost.GetRenderers());
        }

        var data = NotebookMarkdownExporter.Export(title, context.NotebookCells, options);
        var fileName = ExportHtmlAction.SanitizeFileName(title, ".md");

        await context.RequestFileDownloadAsync(fileName, "text/markdown", data).ConfigureAwait(false);
    }
}
