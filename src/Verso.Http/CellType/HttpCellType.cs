using Verso.Abstractions;
using Verso.Http.Kernel;

namespace Verso.Http.CellType;

/// <summary>
/// HTTP cell type combining the HTTP renderer with the HTTP kernel.
/// Registered as the entry point for HTTP cells via <see cref="VersoExtensionAttribute"/>.
/// </summary>
[VersoExtension]
public sealed class HttpCellType : ICellType
{
    // --- IExtension ---

    public string ExtensionId => "verso.http.celltype.http";
    public string Name => "HTTP Cell Type";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "HTTP request cell type for sending REST API requests.";

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- ICellType ---

    public string CellTypeId => "http";
    public string DisplayName => "HTTP";

    public string? Icon => "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"currentColor\">"
        + "<circle cx=\"12\" cy=\"12\" r=\"10\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"/>"
        + "<ellipse cx=\"12\" cy=\"12\" rx=\"4\" ry=\"10\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"/>"
        + "<line x1=\"2\" y1=\"12\" x2=\"22\" y2=\"12\" stroke=\"currentColor\" stroke-width=\"2\"/>"
        + "</svg>";

    public ICellRenderer Renderer { get; } = new HttpCellRenderer();
    public ILanguageKernel? Kernel { get; } = new HttpKernel();
    public bool IsEditable => true;

    public string GetDefaultContent() => "GET https://api.example.com\nAccept: application/json";
}
