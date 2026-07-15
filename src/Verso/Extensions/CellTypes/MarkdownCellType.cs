using Verso.Abstractions;
using Verso.Extensions.Renderers;

namespace Verso.Extensions.CellTypes;

/// <summary>
/// Built-in cell type for Markdown prose cells. Combines the Markdig-based
/// <see cref="MarkdownRenderer"/> with no kernel: running a Markdown cell renders its
/// source to HTML rather than executing code.
/// </summary>
[VersoExtension]
public sealed class MarkdownCellType : ICellType
{
    // --- IExtension ---

    public string ExtensionId => "verso.celltype.markdown";
    public string Name => "Markdown Cell Type";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Markdown prose cells rendered to HTML with Markdig.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- ICellType ---

    public string CellTypeId => "markdown";
    public string DisplayName => "Markdown";

    public string? Icon => "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"currentColor\">"
        + "<path d=\"M20.56 18H3.44C2.65 18 2 17.37 2 16.59V7.41C2 6.63 2.65 6 3.44 6h17.12c.79 0 1.44.63 1.44 1.41v9.18c0 .78-.65 1.41-1.44 1.41M6.81 15.19v-3.66l1.92 2.35 1.92-2.35v3.66h1.93V8.81h-1.93l-1.92 2.35-1.92-2.35H4.89v6.38h1.92M19.69 12h-1.92V8.81h-1.92V12h-1.93l2.89 3.28z\"/>"
        + "</svg>";

    public ICellRenderer Renderer { get; } = new MarkdownRenderer();
    public ILanguageKernel? Kernel => null;
    public bool IsEditable => true;

    // The rendered HTML is derived entirely from the cell source, so it is transient:
    // it is re-rendered when the notebook opens, stripped from the saved file, and
    // rendering it does not mark the notebook as edited.
    public bool PersistsOutputs => false;

    public string GetDefaultContent() => string.Empty;
}
