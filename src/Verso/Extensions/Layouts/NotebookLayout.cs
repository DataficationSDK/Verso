using System.Net;
using System.Text;
using Verso.Abstractions;

namespace Verso.Extensions.Layouts;

/// <summary>
/// Built-in linear notebook layout that arranges cells in a vertical stack.
/// </summary>
[VersoExtension]
public sealed class NotebookLayout : ILayoutEngine
{
    // --- IExtension ---

    public string ExtensionId => "verso.layout.notebook";
    public string Name => "Notebook Layout";
    public string Version => "0.5.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Linear top-to-bottom notebook layout.";

    // --- ILayoutEngine ---

    public string LayoutId => "notebook";
    public string DisplayName => "Notebook";
    public string? Icon => null;

    public LayoutCapabilities Capabilities =>
        LayoutCapabilities.CellInsert |
        LayoutCapabilities.CellDelete |
        LayoutCapabilities.CellReorder |
        LayoutCapabilities.CellEdit |
        LayoutCapabilities.CellResize |
        LayoutCapabilities.CellExecute |
        LayoutCapabilities.MultiSelect;

    public bool RequiresCustomRenderer => false;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"verso-notebook-layout\">");

        foreach (var cell in cells)
        {
            sb.Append("<div class=\"verso-cell-container\" data-cell-id=\"")
              .Append(cell.Id)
              .Append("\">");
            sb.Append(WebUtility.HtmlEncode(cell.Source));
            sb.Append("</div>");
        }

        sb.Append("</div>");

        return Task.FromResult(new RenderResult("text/html", sb.ToString()));
    }

    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
    {
        return Task.FromResult(new CellContainerInfo(cellId, 0, 0, 800, 120));
    }

    public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context) => Task.CompletedTask;
    public Task OnCellRemovedAsync(Guid cellId, IVersoContext context) => Task.CompletedTask;
    public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context) => Task.CompletedTask;

    public Dictionary<string, object> GetLayoutMetadata() => new();

    public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context)
        => Task.CompletedTask;
}
