using System.Collections.ObjectModel;
using Verso.Abstractions;

namespace Verso.Blazor.Services;

/// <summary>
/// Lightweight <see cref="ICellRenderContext"/> for the Blazor properties panel.
/// Delegates to the <see cref="Scaffold"/> subsystems (same pattern as
/// <see cref="BlazorToolbarActionContext"/>).
/// </summary>
internal sealed class BlazorCellRenderContext : ICellRenderContext
{
    private readonly Scaffold _scaffold;

    public BlazorCellRenderContext(Scaffold scaffold, CellModel cell)
    {
        _scaffold = scaffold ?? throw new ArgumentNullException(nameof(scaffold));
        CellId = cell.Id;
        CellMetadata = new ReadOnlyDictionary<string, object>(cell.Metadata);
    }

    public Guid CellId { get; }
    public IReadOnlyDictionary<string, object> CellMetadata { get; }
    public (double Width, double Height) Dimensions => (800, 400);
    public bool IsSelected => true;
    public IVariableStore Variables => _scaffold.Variables;
    public CancellationToken CancellationToken => CancellationToken.None;
    public IThemeContext Theme => _scaffold.ThemeContext;
    public LayoutCapabilities LayoutCapabilities => _scaffold.LayoutCapabilities;
    public IExtensionHostContext ExtensionHost => _scaffold.ExtensionHostContext;
    public INotebookMetadata NotebookMetadata => new BlazorNotebookMetadata(_scaffold);
    public INotebookOperations Notebook => _scaffold.NotebookOps;
    public string? ActiveLayoutId => _scaffold.LayoutManager?.ActiveLayout?.LayoutId;

    public Task WriteOutputAsync(CellOutput output) => Task.CompletedTask;

    public Task UpdateOutputAsync(string outputBlockId, CellOutput output)
    {
        throw new NotSupportedException("In-place output update is not supported in Blazor cell render context.");
    }

    public Task RequestFileDownloadAsync(string fileName, string contentType, byte[] data)
    {
        throw new NotSupportedException("File download is not supported in Blazor cell render context.");
    }

    private sealed class BlazorNotebookMetadata : INotebookMetadata
    {
        private readonly Scaffold _scaffold;
        public BlazorNotebookMetadata(Scaffold scaffold) => _scaffold = scaffold;
        public string? Title => _scaffold.Title;
        public string? DefaultKernelId => _scaffold.DefaultKernelId;
        public string? FilePath => null;
        public Dictionary<string, NotebookParameterDefinition>? Parameters => _scaffold.Notebook.Parameters;
    }
}
