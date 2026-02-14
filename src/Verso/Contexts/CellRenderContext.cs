using Verso.Abstractions;

namespace Verso.Contexts;

/// <summary>
/// <see cref="ICellRenderContext"/> implementation for rendering non-executable cells
/// within the execution pipeline.
/// </summary>
internal sealed class CellRenderContext : ICellRenderContext
{
    private readonly IVariableStore _variables;
    private readonly IThemeContext _theme;
    private readonly LayoutCapabilities _layoutCapabilities;
    private readonly IExtensionHostContext _extensionHost;
    private readonly INotebookMetadata _notebookMetadata;
    private readonly INotebookOperations _notebook;

    public CellRenderContext(
        Guid cellId,
        IReadOnlyDictionary<string, object> cellMetadata,
        IVariableStore variables,
        CancellationToken cancellationToken,
        IThemeContext theme,
        LayoutCapabilities layoutCapabilities,
        IExtensionHostContext extensionHost,
        INotebookMetadata notebookMetadata,
        INotebookOperations notebook)
    {
        CellId = cellId;
        CellMetadata = cellMetadata;
        _variables = variables;
        CancellationToken = cancellationToken;
        _theme = theme;
        _layoutCapabilities = layoutCapabilities;
        _extensionHost = extensionHost;
        _notebookMetadata = notebookMetadata;
        _notebook = notebook;
    }

    public Guid CellId { get; }
    public IReadOnlyDictionary<string, object> CellMetadata { get; }
    public (double Width, double Height) Dimensions => (800, 400);
    public bool IsSelected => false;
    public IVariableStore Variables => _variables;
    public CancellationToken CancellationToken { get; }
    public IThemeContext Theme => _theme;
    public LayoutCapabilities LayoutCapabilities => _layoutCapabilities;
    public IExtensionHostContext ExtensionHost => _extensionHost;
    public INotebookMetadata NotebookMetadata => _notebookMetadata;
    public INotebookOperations Notebook => _notebook;

    public Task WriteOutputAsync(CellOutput output) => Task.CompletedTask;
}
