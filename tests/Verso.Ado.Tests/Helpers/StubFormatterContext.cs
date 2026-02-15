using Verso.Abstractions;
using Verso.Contexts;
using Verso.Stubs;

namespace Verso.Ado.Tests.Helpers;

/// <summary>
/// Minimal <see cref="IFormatterContext"/> stub for Verso.Ado formatter tests.
/// </summary>
internal sealed class StubFormatterContext : IFormatterContext
{
    public string MimeType { get; set; } = "text/html";
    public double MaxWidth { get; set; } = 800;
    public double MaxHeight { get; set; } = 600;

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
