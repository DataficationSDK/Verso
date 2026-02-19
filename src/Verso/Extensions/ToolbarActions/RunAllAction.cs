using Verso.Abstractions;

namespace Verso.Extensions.ToolbarActions;

/// <summary>
/// Toolbar action that executes all cells in the notebook.
/// </summary>
[VersoExtension]
public sealed class RunAllAction : IToolbarAction
{
    // --- IExtension ---

    public string ExtensionId => "verso.action.run-all";
    public string Name => "Run All";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Executes all cells in the notebook.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IToolbarAction ---

    public string ActionId => "verso.action.run-all";
    public string DisplayName => "Run All";
    public string? Icon => "<svg viewBox=\"0 0 16 16\" width=\"16\" height=\"16\" fill=\"currentColor\"><path d=\"M2 3v10l5-5z\"/><path d=\"M8 3v10l5-5z\"/></svg>";
    public ToolbarPlacement Placement => ToolbarPlacement.MainToolbar;
    public int Order => 10;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
    {
        var enabled = context.LayoutCapabilities.HasFlag(LayoutCapabilities.CellExecute)
                      && context.NotebookCells.Count > 0;
        return Task.FromResult(enabled);
    }

    public async Task ExecuteAsync(IToolbarActionContext context)
    {
        await context.Notebook.ExecuteAllAsync().ConfigureAwait(false);
    }
}
