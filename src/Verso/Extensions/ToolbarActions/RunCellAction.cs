using Verso.Abstractions;

namespace Verso.Extensions.ToolbarActions;

/// <summary>
/// Toolbar action that executes the currently selected cell(s).
/// </summary>
[VersoExtension]
public sealed class RunCellAction : IToolbarAction
{
    // --- IExtension ---

    public string ExtensionId => "verso.action.run-cell";
    public string Name => "Run Cell";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Executes the selected cell(s).";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IToolbarAction ---

    public string ActionId => "verso.action.run-cell";
    public string DisplayName => "Run Cell";
    public string? Icon => null;
    public ToolbarPlacement Placement => ToolbarPlacement.CellToolbar;
    public int Order => 20;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
    {
        var enabled = context.LayoutCapabilities.HasFlag(LayoutCapabilities.CellExecute)
                      && context.SelectedCellIds.Count > 0;
        return Task.FromResult(enabled);
    }

    public async Task ExecuteAsync(IToolbarActionContext context)
    {
        foreach (var cellId in context.SelectedCellIds)
        {
            await context.Notebook.ExecuteCellAsync(cellId).ConfigureAwait(false);
        }
    }
}
