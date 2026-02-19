using Verso.Abstractions;
using Verso.Ado.Kernel;

namespace Verso.Ado.CellType;

/// <summary>
/// SQL cell type combining the SQL renderer with the SQL kernel.
/// Registered as the entry point for SQL cells via <see cref="VersoExtensionAttribute"/>.
/// </summary>
[VersoExtension]
public sealed class SqlCellType : ICellType
{
    // --- IExtension ---

    public string ExtensionId => "verso.ado.celltype.sql";
    public string Name => "SQL Cell Type";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "SQL cell type for querying databases via ADO.NET.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- ICellType ---

    public string CellTypeId => "sql";
    public string DisplayName => "SQL";

    public string? Icon => "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"currentColor\">"
        + "<ellipse cx=\"12\" cy=\"6\" rx=\"8\" ry=\"3\"/>"
        + "<path d=\"M4 6v4c0 1.66 3.58 3 8 3s8-1.34 8-3V6\"/>"
        + "<path d=\"M4 10v4c0 1.66 3.58 3 8 3s8-1.34 8-3v-4\"/>"
        + "<path d=\"M4 14v4c0 1.66 3.58 3 8 3s8-1.34 8-3v-4\"/>"
        + "</svg>";

    public ICellRenderer Renderer { get; } = new SqlCellRenderer();
    public ILanguageKernel? Kernel { get; } = new SqlKernel();
    public bool IsEditable => true;

    public string GetDefaultContent() => "-- Write your SQL query here\nSELECT ";
}
