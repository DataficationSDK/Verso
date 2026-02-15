using Verso.Abstractions;

namespace Verso.Extensions.ToolbarActions;

/// <summary>
/// Toolbar action that cycles to the next available layout engine.
/// </summary>
[VersoExtension]
public sealed class SwitchLayoutAction : IToolbarAction
{
    // --- IExtension ---

    public string ExtensionId => "verso.action.switch-layout";
    public string Name => "Switch Layout";
    public string Version => "0.5.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Cycles between available layout engines.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IToolbarAction ---

    public string ActionId => "verso.switchLayout";
    public string DisplayName => "Switch Layout";
    public string? Icon => null;
    public ToolbarPlacement Placement => ToolbarPlacement.MainToolbar;
    public int Order => 50;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
    {
        var layouts = context.ExtensionHost.GetLayouts();
        var enabled = context.NotebookCells.Count >= 0 && layouts.Count > 1;
        return Task.FromResult(enabled);
    }

    public Task ExecuteAsync(IToolbarActionContext context)
    {
        var layouts = context.ExtensionHost.GetLayouts();
        if (layouts.Count <= 1) return Task.CompletedTask;

        // Find current layout index and cycle to next
        var currentIndex = -1;
        for (int i = 0; i < layouts.Count; i++)
        {
            if (string.Equals(layouts[i].LayoutId, context.Notebook.ActiveLayoutId, StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = i;
                break;
            }
        }

        var nextIndex = (currentIndex + 1) % layouts.Count;
        var nextLayout = layouts[nextIndex];

        context.Notebook.SetActiveLayout(nextLayout.LayoutId);

        return Task.CompletedTask;
    }
}
