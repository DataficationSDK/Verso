using Microsoft.JSInterop;
using Verso.Abstractions;

namespace Verso.Blazor.Services;

/// <summary>
/// IToolbarActionContext implementation for Blazor toolbar actions.
/// Delegates to the Scaffold's subsystems for shared state.
/// </summary>
public sealed class BlazorToolbarActionContext : IToolbarActionContext
{
    private readonly Scaffold _scaffold;
    private readonly IJSRuntime? _jsRuntime;

    public BlazorToolbarActionContext(Scaffold scaffold, IReadOnlyList<Guid> selectedCellIds)
        : this(scaffold, selectedCellIds, null)
    {
    }

    public BlazorToolbarActionContext(Scaffold scaffold, IReadOnlyList<Guid> selectedCellIds, IJSRuntime? jsRuntime)
    {
        _scaffold = scaffold ?? throw new ArgumentNullException(nameof(scaffold));
        SelectedCellIds = selectedCellIds;
        _jsRuntime = jsRuntime;
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

    public async Task RequestFileDownloadAsync(string fileName, string contentType, byte[] data)
    {
        if (_jsRuntime is null)
            throw new NotSupportedException("File download requires JS interop but no IJSRuntime was provided.");

        var base64 = Convert.ToBase64String(data);
        await _jsRuntime.InvokeVoidAsync("versoFileDownload.triggerDownload", fileName, contentType, base64);
    }

    private sealed class BlazorNotebookMetadata : INotebookMetadata
    {
        private readonly Scaffold _scaffold;
        public BlazorNotebookMetadata(Scaffold scaffold) => _scaffold = scaffold;
        public string? Title => _scaffold.Title;
        public string? DefaultKernelId => _scaffold.DefaultKernelId;
        public string? FilePath => null;
    }
}
