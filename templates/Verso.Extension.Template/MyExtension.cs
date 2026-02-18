namespace MyExtension;

/// <summary>
/// Main extension entry point. Implements <see cref="IExtension"/> to participate
/// in the Verso extension lifecycle.
/// </summary>
[VersoExtension]
public sealed class MyExtensionEntry : IExtension
{
    public string ExtensionId => "com.example.myextension";
    public string Name => "MyExtension";
    public string Version => "1.0.0";
    public string? Author => "Extension Author";
    public string? Description => "A Verso extension.";

    public Task OnLoadedAsync(IExtensionHostContext context)
    {
        // Called when the extension is loaded by the host.
        // Use this to register services or perform one-time setup.
        return Task.CompletedTask;
    }

    public Task OnUnloadedAsync()
    {
        // Called when the extension is unloaded.
        // Use this to clean up resources.
        return Task.CompletedTask;
    }
}
