using Verso.Abstractions;
using Verso.Extensions.Kernels;
using Verso.Extensions.Renderers;

namespace Verso.Extensions.CellTypes;

/// <summary>
/// HTML cell type combining the HTML renderer with the HTML kernel.
/// Registered as the entry point for HTML cells via <see cref="VersoExtensionAttribute"/>.
/// </summary>
[VersoExtension]
public sealed class HtmlCellType : ICellType
{
    // --- IExtension ---

    public string ExtensionId => "verso.celltype.html";
    public string Name => "HTML Cell Type";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "HTML cell type for authoring raw HTML with @variable substitution.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- ICellType ---

    public string CellTypeId => "html";
    public string DisplayName => "HTML";

    public string? Icon => "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"currentColor\">"
        + "<path d=\"M1.5 0h21l-1.91 21.56L11.5 24l-9.09-2.44L.5 0zm7.09 9.87L8.36 7.36l-.32-3.63h8.92l-.08.96-.16 1.82-.08.85H10.16l.22 2.51h6.4l-.24 2.68-.47 5.3-.07.74-4.5 1.25-4.5-1.25-.31-3.47h2.2l.16 1.76 2.45.66 2.45-.66.26-2.87H8.59z\"/>"
        + "</svg>";

    public ICellRenderer Renderer { get; } = new HtmlCellRenderer();
    public ILanguageKernel? Kernel { get; } = new HtmlKernel();
    public bool IsEditable => true;

    public string GetDefaultContent() => "<!-- Write your HTML here -->\n<h1>Hello World</h1>";
}
