using Verso.Abstractions;
using Verso.Contexts;
using Verso.Stubs;

namespace Verso.Ado.Tests.Helpers;

/// <summary>
/// Test stub implementing <see cref="IExecutionContext"/> with output tracking for verification.
/// </summary>
internal sealed class StubExecutionContext : IExecutionContext
{
    public IVariableStore Variables { get; } = new VariableStore();
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
    public IThemeContext Theme { get; } = new StubThemeContext();
    public LayoutCapabilities LayoutCapabilities { get; set; } = LayoutCapabilities.None;
    public IExtensionHostContext ExtensionHost { get; set; } = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>());
    public INotebookMetadata NotebookMetadata { get; } = new NotebookMetadataContext(new NotebookModel());
    public INotebookOperations Notebook { get; set; } = new StubNotebookOperations();

    public Guid CellId { get; set; } = Guid.NewGuid();
    public int ExecutionCount { get; set; } = 1;

    public List<CellOutput> WrittenOutputs { get; } = new();
    public List<CellOutput> DisplayedOutputs { get; } = new();

    public Task WriteOutputAsync(CellOutput output)
    {
        WrittenOutputs.Add(output);
        return Task.CompletedTask;
    }

    public Task DisplayAsync(CellOutput output)
    {
        DisplayedOutputs.Add(output);
        return Task.CompletedTask;
    }
}
