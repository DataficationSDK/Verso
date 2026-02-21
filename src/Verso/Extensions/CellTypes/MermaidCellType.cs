using Verso.Abstractions;
using Verso.Extensions.Kernels;
using Verso.Extensions.Renderers;

namespace Verso.Extensions.CellTypes;

/// <summary>
/// Mermaid cell type combining the Mermaid renderer with the Mermaid kernel.
/// Registered as the entry point for Mermaid cells via <see cref="VersoExtensionAttribute"/>.
/// </summary>
[VersoExtension]
public sealed class MermaidCellType : ICellType
{
    // --- IExtension ---

    public string ExtensionId => "verso.celltype.mermaid";
    public string Name => "Mermaid Cell Type";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Mermaid cell type for creating diagrams with mermaid.js syntax.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- ICellType ---

    public string CellTypeId => "mermaid";
    public string DisplayName => "Mermaid";

    public string? Icon => "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"currentColor\">"
        + "<path d=\"M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.94-.49-7-3.85-7-7.93 0-.62.08-1.22.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z\"/>"
        + "</svg>";

    public ICellRenderer Renderer { get; } = new MermaidCellRenderer();
    public ILanguageKernel? Kernel { get; } = new MermaidKernel();
    public bool IsEditable => true;

    public string GetDefaultContent() => "graph TD\n    A[Start] --> B[End]";
}
