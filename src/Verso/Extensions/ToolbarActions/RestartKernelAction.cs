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
    public string Version => "0.5.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Restarts the active language kernel.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- IToolbarAction ---

    public string ActionId => "verso.action.restart-kernel";
    public string DisplayName => "Restart Kernel";
    public string? Icon => "<svg viewBox=\"0 0 16 16\" width=\"16\" height=\"16\" fill=\"currentColor\"><path d=\"M12.75 8a4.5 4.5 0 0 1-8.61 1.83l-1.39.57A6 6 0 0 0 14.25 8 6 6 0 0 0 3.5 4.33V2.5H2v4l.75.75h3.5v-1.5H4.35A4.5 4.5 0 0 1 12.75 8z\"/></svg>";
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
