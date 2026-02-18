namespace MyExtension;

/// <summary>
/// Example <see cref="IToolbarAction"/> that writes a greeting message.
/// Replace this with your own toolbar action logic.
/// </summary>
[VersoExtension]
public sealed class SampleToolbarAction : IToolbarAction
{
    public string ExtensionId => "com.example.myextension.action";
    public string Name => "Sample Action";
    public string Version => "1.0.0";
    public string? Author => "Extension Author";
    public string? Description => "A sample toolbar action.";

    public string ActionId => "myextension.action.greet";
    public string DisplayName => "Greet";
    public string? Icon => "hand-wave";
    public ToolbarPlacement Placement => ToolbarPlacement.MainToolbar;
    public int Order => 100;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<bool> IsEnabledAsync(IToolbarActionContext context)
        => Task.FromResult(true);

    public async Task ExecuteAsync(IToolbarActionContext context)
    {
        await context.WriteOutputAsync(
            new CellOutput("text/plain", "Hello from MyExtension!"));
    }
}
