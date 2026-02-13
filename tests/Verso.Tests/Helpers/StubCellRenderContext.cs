using Verso.Abstractions;
using Verso.Contexts;
using Verso.Stubs;

namespace Verso.Tests.Helpers;

/// <summary>
/// Minimal <see cref="ICellRenderContext"/> stub with fixed defaults for renderer tests.
/// </summary>
public sealed class StubCellRenderContext : ICellRenderContext
{
    public Guid CellId { get; set; } = Guid.NewGuid();
    public IReadOnlyDictionary<string, object> CellMetadata { get; set; } = new Dictionary<string, object>();
    public (double Width, double Height) Dimensions { get; set; } = (800, 600);
    public bool IsSelected { get; set; } = false;

    // --- IVersoContext ---

    public IVariableStore Variables { get; } = new VariableStore();
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
    public IThemeContext Theme { get; } = new StubThemeContext();
    public LayoutCapabilities LayoutCapabilities { get; set; } = LayoutCapabilities.None;
    public IExtensionHostContext ExtensionHost { get; } = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>());
    public INotebookMetadata NotebookMetadata { get; } = new NotebookMetadataContext(new NotebookModel());
    public INotebookOperations Notebook { get; } = new StubNotebookOperations();

    public Task WriteOutputAsync(CellOutput output) => Task.CompletedTask;
}
