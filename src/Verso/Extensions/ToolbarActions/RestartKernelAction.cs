using Verso.Abstractions;

namespace Verso.Extensions.ToolbarActions;

/// <summary>
/// Toolbar action that restarts the active language kernel.
/// </summary>
[VersoExtension]
public sealed class RestartKernelAction : IToolbarAction
{
    // --- IExtension ---

    public string ExtensionId => "verso.action.restart-kernel";
    public string Name => "Restart Kernel";
    public string Version => "0.1.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Restarts the active language kernel.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IToolbarAction ---

    public string ActionId => "verso.action.restart-kernel";
    public string DisplayName => "Restart Kernel";
    public string? Icon => null;
    public ToolbarPlacement Placement => ToolbarPlacement.MainToolbar;
    public int Order => 40;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
    {
        var enabled = context.ActiveKernelId is not null;
        return Task.FromResult(enabled);
    }

    public async Task ExecuteAsync(IToolbarActionContext context)
    {
        await context.Notebook.RestartKernelAsync(context.ActiveKernelId).ConfigureAwait(false);
    }
}
