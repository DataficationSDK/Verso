using Verso.Abstractions;
using Verso.Contexts;
using Verso.Stubs;

namespace Verso.Testing.Stubs;

/// <summary>
/// Test double for <see cref="IToolbarActionContext"/> with configurable properties.
/// </summary>
public sealed class StubToolbarActionContext : IToolbarActionContext
{
    public IReadOnlyList<Guid> SelectedCellIds { get; set; } = Array.Empty<Guid>();
    public IReadOnlyList<CellModel> NotebookCells { get; set; } = Array.Empty<CellModel>();
    public string? ActiveKernelId { get; set; }

    // --- IVersoContext ---

    public IVariableStore Variables { get; } = new VariableStore();
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
    public IThemeContext Theme { get; } = new StubThemeContext();
    public LayoutCapabilities LayoutCapabilities { get; set; } = LayoutCapabilities.None;
    public IExtensionHostContext ExtensionHost { get; set; } = new StubExtensionHostContext(() => Array.Empty<ILanguageKernel>());
    public INotebookMetadata NotebookMetadata { get; } = new NotebookMetadataContext(new NotebookModel());
    public INotebookOperations Notebook { get; set; } = new StubNotebookOperations();

    public List<CellOutput> WrittenOutputs { get; } = new();
    public List<(string FileName, string ContentType, byte[] Data)> DownloadedFiles { get; } = new();

    public Task WriteOutputAsync(CellOutput output)
    {
        WrittenOutputs.Add(output);
        return Task.CompletedTask;
    }

    public Task RequestFileDownloadAsync(string fileName, string contentType, byte[] data)
    {
        DownloadedFiles.Add((fileName, contentType, data));
        return Task.CompletedTask;
    }
}
