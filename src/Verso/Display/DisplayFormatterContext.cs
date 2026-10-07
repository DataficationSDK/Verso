using Verso.Abstractions;

namespace Verso.Display;

/// <summary>
/// Adapts an <see cref="IExecutionContext"/> to <see cref="IFormatterContext"/> for use
/// by the <see cref="DisplayHandler"/> during <c>.Display()</c> calls.
/// </summary>
internal sealed class DisplayFormatterContext : IFormatterContext
{
    private readonly IExecutionContext _inner;

    public DisplayFormatterContext(IExecutionContext inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public string MimeType => "text/html";
    public double MaxWidth => 800;
    public double MaxHeight => 600;

    public IVariableStore Variables => _inner.Variables;
    public CancellationToken CancellationToken => _inner.CancellationToken;
    public Task WriteOutputAsync(CellOutput output) => _inner.WriteOutputAsync(output);
    public IThemeContext Theme => _inner.Theme;
    public LayoutCapabilities LayoutCapabilities => _inner.LayoutCapabilities;
    public IExtensionHostContext ExtensionHost => _inner.ExtensionHost;
    public INotebookMetadata NotebookMetadata => _inner.NotebookMetadata;
    public INotebookOperations Notebook => _inner.Notebook;
    public string? ActiveLayoutId => _inner.ActiveLayoutId;
    public IReadOnlySet<Guid> CollapsedSections => _inner.CollapsedSections;
    public IOutputChannelHost? OutputChannels => _inner.OutputChannels;
    public System.Globalization.CultureInfo UICulture => _inner.UICulture;
    public Guid? CellId => _inner.CellId;
    public Task RequestFileDownloadAsync(string fileName, string contentType, byte[] data) =>
        _inner.RequestFileDownloadAsync(fileName, contentType, data);
    public Task UpdateOutputAsync(string outputBlockId, CellOutput output) =>
        _inner.UpdateOutputAsync(outputBlockId, output);
}
