using Verso.Abstractions;
using Verso.Resources;
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
    public string Name => Strings.CellType_Parameters;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => Strings.CellType_Parameters_Description;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public string CellTypeId => "parameters";
    public string DisplayName => Strings.CellType_Parameters_Label;

    public string? Icon => "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"currentColor\">"
        + "<path d=\"M3 17v2h6v-2H3zM3 5v2h10V5H3zm10 16v-2h8v-2h-8v-2h-2v6h2zM7 9v2H3v2h4v2h2V9H7zm14 4v-2H11v2h10zm-6-4h2V7h4V5h-4V3h-2v6z\"/>"
        + "</svg>";

    public ICellRenderer Renderer { get; } = new ParametersCellRenderer();
    public ILanguageKernel? Kernel => null;
    public bool IsEditable => false;

    // The form is always re-rendered from metadata.parameters on open, so its output is
    // transient: it is not written to the saved file and re-rendering it does not dirty the notebook.
    public bool PersistsOutputs => false;

    public string GetDefaultContent() => string.Empty;
}
