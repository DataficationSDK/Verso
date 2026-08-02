using Verso.Abstractions;
using Verso.Resources;

namespace Verso.Extensions.ToolbarActions;

[VersoExtension]
public sealed class ClearCellOutputAction : IToolbarAction
{
    public string ExtensionId => "verso.action.clear-cell-output";
    public string Name => Strings.Action_ClearCellOutput;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => Strings.Action_ClearCellOutput_Description;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public string ActionId => "verso.action.clear-cell-output";
    public string DisplayName => Strings.Action_ClearCellOutput_Label;
    public string? Icon => "<svg viewBox=\"0 0 16 16\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.3\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><line x1=\"2\" y1=\"4\" x2=\"11\" y2=\"4\"/><line x1=\"2\" y1=\"8\" x2=\"11\" y2=\"8\"/><line x1=\"2\" y1=\"12\" x2=\"8\" y2=\"12\"/><path d=\"M11.5 5.5l2.5 2.5m0-2.5L11.5 8\"/></svg>";
    public ToolbarPlacement Placement => ToolbarPlacement.CellToolbar;
    public int Order => 31;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
    {
        if (context.SelectedCellIds.Count == 0)
            return Task.FromResult(false);

        var enabled = context.SelectedCellIds.Any(selectedId =>
            context.NotebookCells.Any(cell => cell.Id == selectedId && cell.Outputs.Count > 0));

        return Task.FromResult(enabled);
    }

    public async Task ExecuteAsync(IToolbarActionContext context)
    {
        foreach (var cellId in context.SelectedCellIds)
        {
            await context.Notebook.ClearOutputAsync(cellId).ConfigureAwait(false);
        }
    }
}