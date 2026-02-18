using Verso.Abstractions;

namespace Verso.Sample.Dice;

/// <summary>
/// Toolbar action that re-executes all dice cells in the notebook.
/// </summary>
[VersoExtension]
public sealed class DiceRollAction : IToolbarAction
{
    public string ExtensionId => "com.verso.sample.dice.rollall";
    public string Name => "Roll All Dice";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Re-rolls all dice cells in the notebook";

    public string ActionId => "dice.action.roll-all";
    public string DisplayName => "Roll All";
    public string? Icon => "casino";
    public ToolbarPlacement Placement => ToolbarPlacement.MainToolbar;
    public int Order => 100;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
    {
        var hasDiceCells = context.NotebookCells.Any(c =>
            string.Equals(c.Language, "dice", StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(hasDiceCells);
    }

    public async Task ExecuteAsync(IToolbarActionContext context)
    {
        var diceCells = context.NotebookCells
            .Where(c => string.Equals(c.Language, "dice", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var cell in diceCells)
        {
            await context.Notebook.ExecuteCellAsync(cell.Id);
        }
    }
}
