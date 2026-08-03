using System.Net;
using System.Text;
using Verso.Abstractions;
using Verso.Resources;
using Verso.Extensions.Utilities;

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
    public string Name => Strings.Layout_Presentation;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => Strings.Layout_Presentation_Description;

    // --- ILayoutEngine ---

    public string LayoutId => "presentation";
    public string DisplayName => Strings.Layout_Presentation_Label;
    public string? Icon => null;

    public LayoutCapabilities Capabilities => LayoutCapabilities.None;

    public bool RequiresCustomRenderer => true;

    public IReadOnlySet<CellVisibilityState> SupportedVisibilityStates { get; } =
        new HashSet<CellVisibilityState>
        {
            CellVisibilityState.Visible,
            CellVisibilityState.Hidden,
            CellVisibilityState.OutputOnly,
        };

    private static readonly ICellRenderer _fallbackRenderer = new ContentFallbackRenderer();

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
    {
        var renderers = context.ExtensionHost.GetRenderers();
        var sb = new StringBuilder();
        sb.Append("<div class=\"verso-presentation-view\">");

        foreach (var cell in cells)
        {
            var renderer = renderers.FirstOrDefault(r => r.CellTypeId == cell.Type) ?? _fallbackRenderer;
            var visibility = CellVisibilityResolver.Resolve(cell, renderer, LayoutId, SupportedVisibilityStates);

            if (visibility == CellVisibilityState.Hidden)
                continue;

            // Cells without any output have nothing meaningful to show in a read-only
            // presentation; the portal would mount the Cell component into an empty
            // wrapper that the presentation CSS strips down to nothing.
            if (cell.Outputs.Count == 0)
                continue;

            // For cells the author marked Visible (source + output), render the source as
            // a static read-only block before the slot. OutputOnly cells skip this block.
            // The portal then injects the live <Cell> Blazor component into the slot below
            // for the outputs; CSS under .verso-presentation-cell hides the inner editor
            // chrome to keep the presentation read-only.
            if (visibility == CellVisibilityState.Visible && !string.IsNullOrEmpty(cell.Source))
            {
                sb.Append("<div class=\"verso-presentation-input\"><pre>")
                  .Append(WebUtility.HtmlEncode(cell.Source))
                  .Append("</pre></div>");
            }

            var visibilityClass = visibility == CellVisibilityState.OutputOnly
                ? " verso-presentation-cell--output-only"
                : " verso-presentation-cell--visible";
            sb.Append("<div class=\"verso-presentation-cell")
              .Append(visibilityClass)
              .Append("\" data-cell-slot=\"")
              .Append(cell.Id)
              .Append("\"></div>");
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
