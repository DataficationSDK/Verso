using Verso.Abstractions;

namespace Verso.Extensions.ToolbarActions;

/// <summary>
/// Toolbar action that clears all cell outputs in the notebook.
/// </summary>
[VersoExtension]
public sealed class ClearOutputsAction : IToolbarAction
{
    // --- IExtension ---

    public string ExtensionId => "verso.action.clear-outputs";
    public string Name => "Clear Outputs";
    public string Version => "0.5.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Clears all cell outputs in the notebook.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IToolbarAction ---

    public string ActionId => "verso.action.clear-outputs";
    public string DisplayName => "Clear Outputs";
    public string? Icon => null;
    public ToolbarPlacement Placement => ToolbarPlacement.MainToolbar;
    public int Order => 30;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
    {
        var enabled = context.NotebookCells.Count > 0;
        return Task.FromResult(enabled);
    }

    public async Task ExecuteAsync(IToolbarActionContext context)
    {
        await context.Notebook.ClearAllOutputsAsync().ConfigureAwait(false);
    }
}
