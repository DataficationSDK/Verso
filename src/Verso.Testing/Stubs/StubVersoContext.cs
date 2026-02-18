using Verso.Abstractions;
using Verso.Contexts;
using Verso.Stubs;

namespace Verso.Testing.Stubs;

/// <summary>
/// Minimal <see cref="IVersoContext"/> stub for testing extensions that need a context.
/// </summary>
public sealed class StubVersoContext : IVersoContext
{
    public IVariableStore Variables { get; } = new VariableStore();
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
    public IThemeContext Theme { get; } = new StubThemeContext();
    public LayoutCapabilities LayoutCapabilities { get; set; } = LayoutCapabilities.None;
    public IExtensionHostContext ExtensionHost { get; } = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>());
    public INotebookMetadata NotebookMetadata { get; } = new NotebookMetadataContext(new NotebookModel());
    public INotebookOperations Notebook { get; set; } = new StubNotebookOperations();

    public List<CellOutput> WrittenOutputs { get; } = new();

    public Task WriteOutputAsync(CellOutput output)
    {
        WrittenOutputs.Add(output);
        return Task.CompletedTask;
    }
}
