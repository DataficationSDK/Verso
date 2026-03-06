using System.Net;
using System.Text;
using Verso.Abstractions;

namespace Verso.Extensions.Layouts;

/// <summary>
/// Read-only presentation layout that shows only cell outputs in a clean linear flow.
/// Hides all editing chrome (toolbar, editor, gutter) so interactive outputs can be
/// clicked without triggering cell selection or layout shifts.
/// </summary>
[VersoExtension]
public sealed class PresentationLayout : ILayoutEngine
{
    // --- IExtension ---

    public string ExtensionId => "verso.layout.presentation";
    public string Name => "Presentation Layout";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Output-only presentation layout for consuming interactive notebooks.";

    // --- ILayoutEngine ---

    public string LayoutId => "presentation";
    public string DisplayName => "Presentation";
    public string? Icon => null;

    public LayoutCapabilities Capabilities => LayoutCapabilities.None;

    public bool RequiresCustomRenderer => true;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"verso-presentation-view\">");

        foreach (var cell in cells)
        {
            if (cell.Outputs.Count == 0)
                continue;

            sb.Append("<div class=\"verso-presentation-cell\" data-cell-id=\"")
              .Append(cell.Id)
              .Append("\">");

            foreach (var output in cell.Outputs)
            {
                if (output.IsError)
                {
                    sb.Append("<div class=\"verso-output verso-output--error\">");
                    sb.Append(WebUtility.HtmlEncode(output.Content));
                    sb.Append("</div>");
                }
                else if (output.MimeType == "text/html" || output.MimeType == "image/svg+xml")
                {
                    sb.Append("<div class=\"verso-output verso-output--html\">");
                    sb.Append(output.Content);
                    sb.Append("</div>");
                }
                else
                {
                    sb.Append("<div class=\"verso-output verso-output--text\"><pre style=\"margin:0;white-space:pre-wrap;\">");
                    sb.Append(WebUtility.HtmlEncode(output.Content));
                    sb.Append("</pre></div>");
                }
            }

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
