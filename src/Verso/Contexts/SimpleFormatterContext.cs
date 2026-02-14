using Verso.Abstractions;
using Verso.Stubs;

namespace Verso.Contexts;

/// <summary>
/// Minimal <see cref="IFormatterContext"/> for variable inspection in the variable explorer.
/// </summary>
public sealed class SimpleFormatterContext : IFormatterContext
{
    public SimpleFormatterContext(IExtensionHostContext extensionHost, IVariableStore variables)
    {
        ExtensionHost = extensionHost ?? throw new ArgumentNullException(nameof(extensionHost));
        Variables = variables ?? throw new ArgumentNullException(nameof(variables));
    }

    public string MimeType { get; set; } = "text/html";
    public double MaxWidth { get; set; } = 800;
    public double MaxHeight { get; set; } = 600;

    public IVariableStore Variables { get; }
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
    public IThemeContext Theme { get; } = new StubThemeContext();
    public LayoutCapabilities LayoutCapabilities { get; set; } = LayoutCapabilities.None;
    public IExtensionHostContext ExtensionHost { get; }
    public INotebookMetadata NotebookMetadata { get; } = new NotebookMetadataContext(new NotebookModel());
    public INotebookOperations Notebook { get; } = new StubNotebookOperations();

    public Task WriteOutputAsync(CellOutput output) => Task.CompletedTask;
}
