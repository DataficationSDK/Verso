using Verso.Abstractions;

namespace Verso.Blazor.Services;

/// <summary>
/// IToolbarActionContext implementation for Blazor toolbar actions.
/// Delegates to the Scaffold's subsystems for shared state.
/// </summary>
public sealed class BlazorToolbarActionContext : IToolbarActionContext
{
    private readonly Scaffold _scaffold;

    public BlazorToolbarActionContext(Scaffold scaffold, IReadOnlyList<Guid> selectedCellIds)
    {
        _scaffold = scaffold ?? throw new ArgumentNullException(nameof(scaffold));
        SelectedCellIds = selectedCellIds;
    }

    public IReadOnlyList<Guid> SelectedCellIds { get; }
    public IReadOnlyList<CellModel> NotebookCells => _scaffold.Cells;
    public string? ActiveKernelId => _scaffold.DefaultKernelId;
    public IVariableStore Variables => _scaffold.Variables;
    public CancellationToken CancellationToken => CancellationToken.None;
    public IThemeContext Theme => _scaffold.ThemeContext;
    public LayoutCapabilities LayoutCapabilities => _scaffold.LayoutCapabilities;
    public IExtensionHostContext ExtensionHost => _scaffold.ExtensionHostContext;
    public INotebookMetadata NotebookMetadata => new BlazorNotebookMetadata(_scaffold);
    public INotebookOperations Notebook => _scaffold.NotebookOps;

    public Task WriteOutputAsync(CellOutput output) => Task.CompletedTask;

    private sealed class BlazorNotebookMetadata : INotebookMetadata
    {
        private readonly Scaffold _scaffold;
        public BlazorNotebookMetadata(Scaffold scaffold) => _scaffold = scaffold;
        public string? Title => _scaffold.Title;
        public string? DefaultKernelId => _scaffold.DefaultKernelId;
        public string? FilePath => null;
    }
}
