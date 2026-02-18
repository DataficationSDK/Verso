using Verso.Abstractions;
using Verso.Contexts;
using Verso.Stubs;

namespace Verso.Testing.Stubs;

/// <summary>
/// Test stub implementing <see cref="IMagicCommandContext"/> with settable properties
/// and output tracking for verification.
/// </summary>
public sealed class StubMagicCommandContext : IMagicCommandContext
{
    public IVariableStore Variables { get; } = new VariableStore();
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
    public IThemeContext Theme { get; } = new StubThemeContext();
    public LayoutCapabilities LayoutCapabilities { get; set; } = LayoutCapabilities.None;
    public IExtensionHostContext ExtensionHost { get; set; } = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>());
    public INotebookMetadata NotebookMetadata { get; set; } = new NotebookMetadataContext(new NotebookModel());
    public INotebookOperations Notebook { get; set; } = new StubNotebookOperations();

    public string RemainingCode { get; set; } = "";
    public bool SuppressExecution { get; set; }

    public List<CellOutput> WrittenOutputs { get; } = new();

    public Task WriteOutputAsync(CellOutput output)
    {
        WrittenOutputs.Add(output);
        return Task.CompletedTask;
    }
}
