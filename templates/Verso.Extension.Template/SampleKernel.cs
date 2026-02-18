namespace MyExtension;

/// <summary>
/// Example <see cref="ILanguageKernel"/> scaffold.
/// Replace this with your own language kernel implementation.
/// </summary>
[VersoExtension]
public sealed class SampleKernel : ILanguageKernel
{
    public string ExtensionId => "com.example.myextension.kernel";
    public string Name => "Sample Kernel";
    public string Version => "1.0.0";
    public string? Author => "Extension Author";
    public string? Description => "A sample language kernel.";
    public string LanguageId => "sample";
    public string DisplayName => "Sample";
    public IReadOnlyList<string> FileExtensions => new[] { ".sample" };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;
    public Task InitializeAsync() => Task.CompletedTask;

    public Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        // TODO: Implement your execution logic here.
        var output = new CellOutput("text/plain", $"Executed: {code}");
        return Task.FromResult<IReadOnlyList<CellOutput>>(new[] { output });
    }

    public Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
        => Task.FromResult<IReadOnlyList<Completion>>(Array.Empty<Completion>());

    public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
        => Task.FromResult<IReadOnlyList<Diagnostic>>(Array.Empty<Diagnostic>());

    public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
        => Task.FromResult<HoverInfo?>(null);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
