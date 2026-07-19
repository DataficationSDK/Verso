using System.Text;
using Verso.Abstractions;
using Verso.Serializers;

namespace Verso.Extensions.ToolbarActions;

/// <summary>
/// Toolbar action that exports the notebook as a native <c>.verso</c> file. Shown only for
/// notebooks opened from a Markdown file while the default notebook layout is active, as the
/// escape hatch for notebooks that outgrow what plain Markdown can persist (outputs, layouts,
/// parameters, and custom cell state).
/// </summary>
[VersoExtension]
public sealed class ExportVersoAction : IToolbarAction
{
    // --- IExtension ---

    public string ExtensionId => "verso.action.export-verso";
    public string Name => "Export Verso";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Exports a Markdown-backed notebook as a native .verso file.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IToolbarAction ---

    public string ActionId => "verso.action.export-verso";
    public string DisplayName => "Verso";
    public string? Icon => null;
    public ToolbarPlacement Placement => ToolbarPlacement.ExportMenu;
    public int Order => 66;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
    {
        var hasCells = context.NotebookCells.Count > 0;
        var fromMarkdown = context.NotebookMetadata.FilePath?
            .EndsWith(".md", StringComparison.OrdinalIgnoreCase) == true;
        var onNotebookLayout = context.ActiveLayoutId is null
            || string.Equals(context.ActiveLayoutId, LayoutDefaults.LayoutId, StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(hasCells && fromMarkdown && onNotebookLayout);
    }

    public async Task ExecuteAsync(IToolbarActionContext context)
    {
        var metadata = context.NotebookMetadata;
        var notebook = new NotebookModel
        {
            Title = metadata.Title,
            DefaultKernelId = metadata.DefaultKernelId,
            Parameters = metadata.Parameters,
        };

        foreach (var cell in context.NotebookCells)
        {
            notebook.Cells.Add(new CellModel
            {
                Id = cell.Id,
                Type = cell.Type,
                Language = cell.Language,
                Source = cell.Source,
                Outputs = new List<CellOutput>(cell.Outputs),
                // Markdown bookkeeping keys describe the source file's fence layout and have
                // no meaning in the native format.
                Metadata = cell.Metadata
                    .Where(kv => !kv.Key.StartsWith("md.", StringComparison.Ordinal))
                    .ToDictionary(kv => kv.Key, kv => kv.Value),
            });
        }

        var serializer = new VersoSerializer(context.ExtensionHost.GetCellTypes());
        var json = await serializer.SerializeAsync(notebook).ConfigureAwait(false);
        var data = Encoding.UTF8.GetBytes(json);

        var fileName = metadata.FilePath is { Length: > 0 } path
            ? Path.GetFileNameWithoutExtension(path) + ".verso"
            : ExportHtmlAction.SanitizeFileName(metadata.Title, ".verso");

        // Deliberately not application/json: save dialogs append the extension registered
        // for the content type when it disagrees with the file name, which would turn
        // notebook.verso into notebook.verso.json.
        await context.RequestFileDownloadAsync(fileName, "application/octet-stream", data).ConfigureAwait(false);
    }
}
