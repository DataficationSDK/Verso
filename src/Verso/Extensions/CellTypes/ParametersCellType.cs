using Verso.Abstractions;
using Verso.Extensions.Renderers;

namespace Verso.Extensions.CellTypes;

/// <summary>
/// Built-in cell type for displaying and editing notebook parameter definitions.
/// The parameters cell renders a form from <c>NotebookModel.Parameters</c> and handles
/// user interactions for adding, removing, and updating parameter values.
/// </summary>
[VersoExtension]
public sealed class ParametersCellType : ICellType
{
    public string ExtensionId => "verso.celltype.parameters";
    public string Name => "Parameters Cell Type";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Displays and manages notebook parameter definitions as an interactive form.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public string CellTypeId => "parameters";
    public string DisplayName => "Parameters";

    public string? Icon => "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"currentColor\">"
        + "<path d=\"M3 17v2h6v-2H3zM3 5v2h10V5H3zm10 16v-2h8v-2h-8v-2h-2v6h2zM7 9v2H3v2h4v2h2V9H7zm14 4v-2H11v2h10zm-6-4h2V7h4V5h-4V3h-2v6z\"/>"
        + "</svg>";

    public ICellRenderer Renderer { get; } = new ParametersCellRenderer();
    public ILanguageKernel? Kernel => null;
    public bool IsEditable => false;

    public string GetDefaultContent() => string.Empty;
}
