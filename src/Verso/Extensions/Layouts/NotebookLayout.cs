using System.Text;
using Verso.Abstractions;
using Verso.Resources;

namespace Verso.Extensions.Layouts;

/// <summary>
/// The built-in linear, top-to-bottom notebook layout. Every cell is the live, editable cell
/// component (its own editor, run button, outputs, and move/delete controls), arranged one per row
/// in document order, and the host's main toolbar drives the notebook-level actions (run all,
/// clear, restart). Each cell sits in an elevated surface card, and the insert affordances are
/// rendered by the layout itself.
///
/// It renders inline as a custom layout: the cell list is built here as HTML with one
/// <c>data-cell-slot</c> per cell, and the host's portal moves the live cell component into each
/// slot. The scoped stylesheet and interactive script are shipped to the host through
/// <see cref="GetStaticAssetsAsync"/>. Every color, surface, and font derives from the host
/// theme tokens (<c>--verso-*</c>), so the layout follows the active light/dark/high-contrast
/// theme with no re-render.
/// </summary>
[VersoExtension]
public sealed class NotebookLayout : ILayoutEngine, ILayoutInteractionHandler
{
    // --- IExtension ---

    public string ExtensionId => "verso.layout.notebook";
    public string Name => Strings.Layout_Notebook;
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => Strings.Layout_Notebook_Description;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // --- ILayoutEngine ---

    public string LayoutId => "notebook";
    public string DisplayName => Strings.Layout_Notebook_Label;
    public string? Icon => null;
    public bool RequiresCustomRenderer => true;

    /// <summary>
    /// The cell affordances plus <c>NotebookEvents</c> so the script receives host state-change
    /// pushes and can keep the slot set in sync when cells are added or removed (from the insert
    /// affordances, a cell's own controls, or the host toolbar).
    /// </summary>
    public LayoutCapabilities Capabilities =>
        LayoutCapabilities.CellInsert |
        LayoutCapabilities.CellDelete |
        LayoutCapabilities.CellReorder |
        LayoutCapabilities.CellEdit |
        LayoutCapabilities.CellResize |
        LayoutCapabilities.CellExecute |
        LayoutCapabilities.MultiSelect |
        LayoutCapabilities.NotebookEvents;

    public bool SupportsPropertiesPanel => true;

    /// <summary>
    /// The layout-root selector prefix the host wraps around this layout's HTML. Both the CSS and
    /// the script scope themselves to it so nothing leaks into the host or a sibling layout.
    /// </summary>
    private string RootPrefix =>
        $".verso-layout-root[data-extension-id=\"{ExtensionId}\"][data-layout-id=\"{LayoutId}\"] ";

    public Task<IReadOnlyList<LayoutStaticAsset>?> GetStaticAssetsAsync(
        IVersoContext context, LayoutHostCapabilities hostCapabilities)
    {
        var assets = new List<LayoutStaticAsset>();
        if (hostCapabilities.SupportedAssetContentTypes.Contains("text/css"))
        {
            assets.Add(new LayoutStaticAsset(
                AssetId: "notebook.css",
                ContentType: "text/css",
                Content: Encoding.UTF8.GetBytes(NotebookLayoutStyles.BuildCss(RootPrefix))));
        }
        if (hostCapabilities.SupportedAssetContentTypes.Contains("text/javascript"))
        {
            assets.Add(new LayoutStaticAsset(
                AssetId: "notebook.js",
                ContentType: "text/javascript",
                Content: Encoding.UTF8.GetBytes(NotebookLayoutStyles.BuildJs(ExtensionId, LayoutId)))
            {
                LoadHints = new LayoutStaticAssetLoadHints(
                    ModuleKind: LayoutScriptModuleKind.Classic,
                    LoadMode: LayoutScriptLoadMode.Defer,
                    Placement: LayoutScriptPlacement.AfterLayoutHtml),
            });
        }
        return Task.FromResult<IReadOnlyList<LayoutStaticAsset>?>(assets.Count == 0 ? null : assets);
    }

    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
    {
        // The dynamic set of addable cell types: Code, then Markdown (if registered), then any
        // other extension-registered cell types.
        var cellTypes = GetAvailableCellTypes(context);

        // Heading cells whose sections the user collapsed. The cells beneath such a heading are
        // folded (no slot emitted) until the next heading at the same or a higher level. The host
        // re-renders this layout when the collapsed set changes.
        var collapsed = context.CollapsedSections;

        var sb = new StringBuilder(8 * 1024);
        sb.Append("<div class=\"vmd-root\">");

        sb.Append("<div class=\"vmd-cells\">");
        if (cells.Count == 0)
        {
            sb.Append("<div class=\"vmd-empty\">")
              .Append("<p class=\"vmd-empty-title\">").Append(Escape(Strings.Layout_EmptyTitle)).Append("</p>")
              .Append("<p class=\"vmd-empty-sub\">").Append(Escape(Strings.Layout_EmptySubtitle)).Append("</p>")
              .Append("</div>");
        }

        int? skipUntilLevel = null;
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var headingLevel = GetHeadingLevel(cell);

            if (skipUntilLevel is not null)
            {
                if (headingLevel is not null && headingLevel.Value <= skipUntilLevel.Value)
                    skipUntilLevel = null;   // this heading closes the collapsed range; render it
                else
                    continue;                // still inside a collapsed section; omit the slot
            }

            var typeClass = string.Equals(cell.Type, "markdown", StringComparison.OrdinalIgnoreCase)
                ? "vmd-cell--markdown"
                : "vmd-cell--code";

            // The slot wrapper carries data-cell-slot so the portal moves the live <Cell>
            // component in here. It deliberately has no data-cell-id of its own: that attribute
            // belongs to the portal-mounted cell DOM node living inside. data-cell-index is the
            // cell's position in the full notebook, so drag-to-reorder stays correct even when
            // folded sections mean the visible slots are not contiguous.
            sb.Append("<section class=\"vmd-cell ").Append(typeClass)
              .Append("\" data-cell-slot=\"").Append(cell.Id)
              .Append("\" data-cell-index=\"").Append(i).Append("\">");
            // The portaled live cell mounts here. Empty until the host runs the portal.
            sb.Append("</section>");

            // Begin folding the cells beneath this heading if its section is collapsed.
            if (headingLevel is not null && collapsed.Contains(cell.Id))
                skipUntilLevel = headingLevel;

            // Insert affordance between this cell and the next; the bottom row covers append.
            if (i < cells.Count - 1)
                AppendInsertRail(sb, i + 1, cellTypes);
        }

        sb.Append("</div>"); // .vmd-cells

        // Bottom add row: one button per available cell type, appending at the end.
        sb.Append("<div class=\"vmd-add-row\">");
        foreach (var ct in cellTypes)
        {
            sb.Append("<button type=\"button\" class=\"vmd-add-btn\" data-action=\"insert-cell\" data-index=\"")
              .Append(cells.Count)
              .Append("\" data-type=\"").Append(ct.Id).Append("\">")
              .Append("<span class=\"vmd-add-glyph\">&#x2B;</span> ")
              .Append(Escape(string.Format(Strings.Layout_AddCell, ct.DisplayName)))
              .Append("</button>");
        }
        sb.Append("</div>");

        sb.Append("</div>"); // .vmd-root
        return Task.FromResult(new RenderResult("text/html", sb.ToString()));
    }

    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
        // Cells flow in a linear column inside the layout's own HTML, so the host's container
        // metrics are not used for positioning. Return a sensible placeholder.
        => Task.FromResult(new CellContainerInfo(cellId, 0, 0, 800, 120));

    public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context) => Task.CompletedTask;
    public Task OnCellRemovedAsync(Guid cellId, IVersoContext context) => Task.CompletedTask;
    public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context) => Task.CompletedTask;

    public Dictionary<string, object> GetLayoutMetadata() => new();
    public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context)
        => Task.CompletedTask;

    // --- ILayoutInteractionHandler ---

    public async Task OnLayoutInteractionAsync(LayoutInteractionContext context)
    {
        switch (context.InteractionType)
        {
            case "insert-cell":
                // The add button posts the target index as its payload and the cell type as
                // targetId. A null language lets the host resolve the default kernel for code
                // cells (markdown and other non-executable types stay null).
                if (int.TryParse(context.Payload, out var index) && index >= 0)
                {
                    var type = string.IsNullOrWhiteSpace(context.TargetId) ? "code" : context.TargetId!;
                    await context.Verso.Notebook.InsertCellAsync(index, type).ConfigureAwait(false);
                    context.RequestRender();
                }
                break;

            case "move-cell":
                // Drag-to-reorder posts the destination index as its payload and the moved cell's
                // id as targetId. The index is already in MoveCell's final-index semantics.
                if (int.TryParse(context.Payload, out var newIndex) && newIndex >= 0
                    && Guid.TryParse(context.TargetId, out var cellId))
                {
                    await context.Verso.Notebook.MoveCellAsync(cellId, newIndex).ConfigureAwait(false);
                    context.RequestRender();
                }
                break;
        }
    }

    // --- HTML helpers ---

    // Text drawn into the layout's own markup passes through here first. Cell type names reach
    // this file from extensions, and the words around them from a resource file, so neither is
    // guaranteed to be free of characters that would otherwise close a tag or an attribute.
    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value);

    private static void AppendInsertRail(StringBuilder sb, int index, IReadOnlyList<CellTypeOption> cellTypes)
    {
        sb.Append("<div class=\"vmd-insert\"><div class=\"vmd-insert-buttons\">");
        foreach (var ct in cellTypes)
        {
            sb.Append("<button type=\"button\" class=\"vmd-insert-btn\" data-action=\"insert-cell\" data-index=\"")
              .Append(index)
              .Append("\" data-type=\"").Append(ct.Id)
              .Append("\" title=\"")
              .Append(Escape(string.Format(Strings.Layout_InsertCellHere, ct.DisplayName)))
              .Append("\">")
              .Append("<span class=\"vmd-insert-glyph\">&#x2B;</span>")
              .Append(Escape(ct.DisplayName)).Append("</button>");
        }
        sb.Append("</div></div>");
    }

    /// <summary>
    /// Builds the addable cell-type list: Code is always first, Markdown is included when a
    /// markdown cell type or renderer is registered, and any other extension-registered cell types
    /// follow in registration order.
    /// </summary>
    private static IReadOnlyList<CellTypeOption> GetAvailableCellTypes(IVersoContext context)
    {
        // Code has no ICellType of its own: it is what a cell is when nothing else claims it,
        // so the engine has to name it here rather than reading the name off a registration.
        var types = new List<CellTypeOption> { new("code", Strings.CellType_Code) };
        var host = context.ExtensionHost;

        var registeredTypes = host.GetCellTypes();
        var hasMarkdown =
            registeredTypes.Any(ct => string.Equals(ct.CellTypeId, "markdown", StringComparison.OrdinalIgnoreCase))
            || host.GetRenderers().Any(r => string.Equals(r.CellTypeId, "markdown", StringComparison.OrdinalIgnoreCase));
        if (hasMarkdown)
            types.Add(new CellTypeOption("markdown", "Markdown"));

        foreach (var ct in registeredTypes)
        {
            if (!string.Equals(ct.CellTypeId, "code", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ct.CellTypeId, "markdown", StringComparison.OrdinalIgnoreCase))
                types.Add(new CellTypeOption(ct.CellTypeId, ct.DisplayName));
        }

        return types;
    }

    private readonly record struct CellTypeOption(string Id, string DisplayName);

    /// <summary>
    /// The heading level of a cell (1-6), or <c>null</c> when it is not a heading. A markdown cell
    /// that starts with one to six <c>#</c> characters followed by a space or tab, or an HTML cell
    /// that opens with an <c>h1</c>-<c>h6</c> tag.
    /// </summary>
    private static int? GetHeadingLevel(CellModel cell)
    {
        var source = cell.Source;
        if (string.IsNullOrEmpty(source)) return null;

        if (string.Equals(cell.Type, "markdown", StringComparison.OrdinalIgnoreCase))
        {
            var text = source.TrimStart();
            var hashes = 0;
            while (hashes < text.Length && text[hashes] == '#') hashes++;
            if (hashes is >= 1 and <= 6 && hashes < text.Length
                && (text[hashes] == ' ' || text[hashes] == '\t'))
                return hashes;
            return null;
        }

        if (string.Equals(cell.Type, "html", StringComparison.OrdinalIgnoreCase))
        {
            var text = source.TrimStart();
            if (text.Length >= 3 && text[0] == '<' && (text[1] == 'h' || text[1] == 'H')
                && text[2] is >= '1' and <= '6')
                return text[2] - '0';
        }

        return null;
    }
}
