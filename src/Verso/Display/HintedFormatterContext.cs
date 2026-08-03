using Verso.Abstractions;

namespace Verso.Display;

/// <summary>
/// Wraps a formatter context with an overridden MIME type preference.
/// </summary>
internal sealed class HintedFormatterContext : IFormatterContext
{
    private readonly IFormatterContext _inner;

    public HintedFormatterContext(IFormatterContext inner, string mimeType)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        MimeType = mimeType ?? throw new ArgumentNullException(nameof(mimeType));
    }

    public string MimeType { get; }
    public double MaxWidth => _inner.MaxWidth;
    public double MaxHeight => _inner.MaxHeight;
    public IVariableStore Variables => _inner.Variables;
    public CancellationToken CancellationToken => _inner.CancellationToken;
    public Task WriteOutputAsync(CellOutput output) => _inner.WriteOutputAsync(output);
    public IThemeContext Theme => _inner.Theme;
    public LayoutCapabilities LayoutCapabilities => _inner.LayoutCapabilities;
    public IExtensionHostContext ExtensionHost => _inner.ExtensionHost;
    public INotebookMetadata NotebookMetadata => _inner.NotebookMetadata;
    public INotebookOperations Notebook => _inner.Notebook;
}
