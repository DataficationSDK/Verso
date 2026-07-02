using System.Text;
using System.Text.Json;
using Verso.Abstractions;
using Verso.Extensions.Utilities;

namespace Verso.Extensions.Layouts;

/// <summary>
/// Grid-based dashboard layout that arranges cells in a 12-column CSS Grid.
/// Shows output-only cells that can be moved, resized, and run, but never code-edited.
/// </summary>
[VersoExtension]
public sealed class DashboardLayout : ILayoutEngine, ILayoutInteractionHandler
{
    private const int GridColumns = 12;
    private const int DefaultCellWidth = 6;
    private const int DefaultCellHeight = 4;

    private readonly Dictionary<Guid, GridPosition> _gridPositions = new();

    // --- IExtension ---

    public string ExtensionId => "verso.layout.dashboard";
    public string Name => "Dashboard Layout";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Grid-based dashboard layout showing output-only cells.";

    // --- ILayoutEngine ---

    public string LayoutId => "dashboard";
    public string DisplayName => "Dashboard";
    public string? Icon => null;
    public bool RequiresCustomRenderer => true;

    public IReadOnlySet<CellVisibilityState> SupportedVisibilityStates { get; } =
        new HashSet<CellVisibilityState>
        {
            CellVisibilityState.Visible,
            CellVisibilityState.Hidden,
            CellVisibilityState.OutputOnly,
        };

    public bool SupportsPropertiesPanel => false;

    // The dashboard is a view-and-arrange surface: tiles can be moved, resized, and run, but
    // their code is never edited here. So it advertises resize and execute, and deliberately not
    // CellEdit, CellInsert, CellDelete, or CellReorder.
    public LayoutCapabilities Capabilities =>
        LayoutCapabilities.CellResize | LayoutCapabilities.CellExecute;

    private static readonly ICellRenderer _fallbackRenderer = new ContentFallbackRenderer();

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
    {
        var renderers = context.ExtensionHost.GetRenderers();
        var sb = new StringBuilder();

        sb.Append("<div class=\"verso-dashboard-root\">");
        sb.Append("<div class=\"verso-dashboard-grid\">");

        foreach (var cell in cells)
        {
            bool alreadyPositioned = _gridPositions.ContainsKey(cell.Id);
            var pos = GetOrCreatePosition(cell.Id);

            if (!alreadyPositioned)
            {
                var renderer = renderers.FirstOrDefault(r => r.CellTypeId == cell.Type) ?? _fallbackRenderer;
                var state = CellVisibilityResolver.Resolve(cell, renderer, LayoutId, SupportedVisibilityStates);
                bool visible = state != CellVisibilityState.Hidden;
                pos = pos with { Visible = visible };
                _gridPositions[cell.Id] = pos;
            }

            if (!pos.Visible) continue;

            // Slot wrapper: the WASM portal injects the live <Cell> Blazor component here.
            // The toolbar and resize handle are siblings of the slot content so portal nodes
            // stay intact across layout updates.
            // No data-cell-id on the slot wrapper: that attribute is reserved for the
            // portal-mounted cell DOM node living inside. Tagging the wrapper would
            // confuse the [data-action] router into routing the toolbar buttons through
            // cell/interact rather than layout/interact.
            sb.Append("<div class=\"verso-dashboard-cell\" data-cell-slot=\"")
              .Append(cell.Id)
              .Append("\" data-target-id=\"")
              .Append(cell.Id)
              .Append("\" style=\"grid-column:")
              .Append(pos.Column + 1).Append("/span ").Append(pos.Width)
              .Append(";grid-row:")
              .Append(pos.Row + 1).Append("/span ").Append(pos.Height)
              .Append("\">");

            // Toolbar IS the drag handle: clicking empty space drags, clicking the Run button
            // fires its data-action via layout-interact. dashboard-interop.js skips drag when the
            // mousedown target is inside a <button>.
            sb.Append("<div class=\"verso-dashboard-cell-toolbar verso-dashboard-drag-handle\" title=\"Drag to move\">")
              .Append("<button class=\"verso-cell-btn verso-cell-btn--run\" data-action=\"run\" data-target-id=\"")
              .Append(cell.Id)
              .Append("\" title=\"Run\"><span>&#x25B6;</span></button>")
              .Append("<span class=\"verso-dashboard-drag-icon\" title=\"Drag to move\">&#x2630;</span>")
              .Append("</div>");

            // Resize handle (bottom-right corner).
            sb.Append("<div class=\"verso-dashboard-resize-handle\"></div>");

            sb.Append("</div>");
        }

        sb.Append("</div>"); // close .verso-dashboard-grid
        sb.Append("</div>"); // close .verso-dashboard-root

        return Task.FromResult(new RenderResult("text/html", sb.ToString()));
    }

    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
    {
        var pos = GetOrCreatePosition(cellId);
        return Task.FromResult(new CellContainerInfo(
            cellId,
            pos.Column,
            pos.Row,
            pos.Width,
            pos.Height,
            pos.Visible));
    }

    public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context)
    {
        if (!_gridPositions.ContainsKey(cellId))
        {
            _gridPositions[cellId] = FindNextAvailablePosition(DefaultCellWidth, DefaultCellHeight);
        }
        return Task.CompletedTask;
    }

    public Task OnCellRemovedAsync(Guid cellId, IVersoContext context)
    {
        _gridPositions.Remove(cellId);
        return Task.CompletedTask;
    }

    public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context)
    {
        // Grid position is independent of cell order
        return Task.CompletedTask;
    }

    public Dictionary<string, object> GetLayoutMetadata()
    {
        if (_gridPositions.Count == 0)
            return new Dictionary<string, object>();

        var cells = new Dictionary<string, object>();
        foreach (var (id, pos) in _gridPositions)
        {
            cells[id.ToString()] = new Dictionary<string, object>
            {
                ["row"] = pos.Row,
                ["col"] = pos.Column,
                ["width"] = pos.Width,
                ["height"] = pos.Height,
                ["visible"] = pos.Visible
            };
        }

        return new Dictionary<string, object> { ["cells"] = cells };
    }

    public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context)
    {
        if (!metadata.TryGetValue("cells", out var cellsObj))
            return Task.CompletedTask;

        Dictionary<string, object>? cellsDict = null;

        if (cellsObj is Dictionary<string, object> dict)
        {
            cellsDict = dict;
        }
        else if (cellsObj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            cellsDict = new Dictionary<string, object>();
            foreach (var prop in jsonElement.EnumerateObject())
                cellsDict[prop.Name] = prop.Value;
        }

        if (cellsDict is null) return Task.CompletedTask;

        foreach (var (key, value) in cellsDict)
        {
            if (!Guid.TryParse(key, out var cellId)) continue;

            int row = 0, col = 0, width = DefaultCellWidth, height = DefaultCellHeight;
            bool visible = true;

            if (value is Dictionary<string, object> posDict)
            {
                if (posDict.TryGetValue("row", out var r)) row = Convert.ToInt32(r);
                if (posDict.TryGetValue("col", out var c)) col = Convert.ToInt32(c);
                if (posDict.TryGetValue("width", out var w)) width = Convert.ToInt32(w);
                if (posDict.TryGetValue("height", out var h)) height = Convert.ToInt32(h);
                if (posDict.TryGetValue("visible", out var v)) visible = Convert.ToBoolean(v);
            }
            else if (value is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                if (je.TryGetProperty("row", out var rr)) row = rr.GetInt32();
                if (je.TryGetProperty("col", out var cc)) col = cc.GetInt32();
                if (je.TryGetProperty("width", out var ww)) width = ww.GetInt32();
                if (je.TryGetProperty("height", out var hh)) height = hh.GetInt32();
                if (je.TryGetProperty("visible", out var vv)) visible = vv.GetBoolean();
            }

            _gridPositions[cellId] = new GridPosition(row, col, width, height, visible);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Updates the grid position for a specific cell.
    /// </summary>
    public void UpdateCellPosition(Guid cellId, int row, int column, int width, int height)
    {
        var visible = _gridPositions.TryGetValue(cellId, out var existing) ? existing.Visible : true;
        _gridPositions[cellId] = new GridPosition(row, column, width, height, visible);
    }

    // --- ILayoutInteractionHandler ---

    public async Task OnLayoutInteractionAsync(LayoutInteractionContext context)
    {
        switch (context.InteractionType)
        {
            case "updateCellPosition":
                ApplyUpdateCellPosition(context);
                // The client gesture is a ghost/placeholder overlay that is torn down on
                // mouseup, leaving the real cell at its original grid slot. Only a re-render
                // emits the new grid-column/grid-row, so the move/resize must request one or
                // the tile snaps back until the next layout switch.
                context.RequestRender();
                return;

            case "run":
                if (TryParseTargetCellId(context, out var runCellId))
                    await context.Verso.Notebook.ExecuteCellAsync(runCellId);
                return;

            default:
                // Unknown interaction types are ignored rather than faulting the host. Throwing
                // here surfaces as a transport-level error and is forward-incompatible: a newer
                // client emitting an interaction this build doesn't know would break an otherwise
                // healthy dashboard. This also absorbs the retired "setEditMode"/"toggleCellEdit"
                // interactions (the dashboard no longer edits code) as graceful no-ops. Matches
                // the built-in notebook layout's no-op handling.
                System.Diagnostics.Debug.WriteLine(
                    $"[DashboardLayout] Ignoring unsupported interactionType '{context.InteractionType}'.");
                return;
        }
    }

    private static bool TryParseTargetCellId(LayoutInteractionContext context, out Guid cellId)
    {
        if (!string.IsNullOrWhiteSpace(context.TargetId) && Guid.TryParse(context.TargetId, out cellId))
            return true;
        cellId = Guid.Empty;
        return false;
    }

    private void ApplyUpdateCellPosition(LayoutInteractionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Payload))
            throw new InvalidOperationException(
                "DASHBOARD_INTERACTION_INVALID_PAYLOAD: updateCellPosition requires a JSON payload.");

        using var doc = JsonDocument.Parse(context.Payload);
        var root = doc.RootElement;

        Guid cellId;
        if (root.TryGetProperty("cellId", out var cellIdProp) && cellIdProp.ValueKind == JsonValueKind.String)
        {
            if (!Guid.TryParse(cellIdProp.GetString(), out cellId))
                throw new InvalidOperationException(
                    "DASHBOARD_INTERACTION_INVALID_PAYLOAD: 'cellId' is not a valid Guid.");
        }
        else if (!string.IsNullOrWhiteSpace(context.TargetId) && Guid.TryParse(context.TargetId, out cellId))
        {
            // TargetId fallback covers DOM-originated events that bind the cell id
            // to the data-target-id attribute instead of including it in the payload.
        }
        else
        {
            throw new InvalidOperationException(
                "DASHBOARD_INTERACTION_INVALID_PAYLOAD: updateCellPosition requires a 'cellId' " +
                "property in the payload or a Guid-shaped 'targetId'.");
        }

        int row = root.TryGetProperty("row", out var rowProp) ? rowProp.GetInt32() : 0;
        int col = root.TryGetProperty("col", out var colProp) ? colProp.GetInt32() : 0;
        int width = root.TryGetProperty("width", out var widthProp) ? widthProp.GetInt32() : DefaultCellWidth;
        int height = root.TryGetProperty("height", out var heightProp) ? heightProp.GetInt32() : DefaultCellHeight;

        UpdateCellPosition(cellId, row, col, width, height);
    }

    // --- Private helpers ---

    private GridPosition GetOrCreatePosition(Guid cellId)
    {
        if (_gridPositions.TryGetValue(cellId, out var pos))
            return pos;

        var newPos = FindNextAvailablePosition(DefaultCellWidth, DefaultCellHeight);
        _gridPositions[cellId] = newPos;
        return newPos;
    }

    /// <summary>
    /// Simple bin-packing: scans row-by-row, column-by-column for the first
    /// available slot that fits the requested width and height.
    /// </summary>
    private GridPosition FindNextAvailablePosition(int width, int height)
    {
        var maxRow = _gridPositions.Count > 0
            ? _gridPositions.Values.Max(p => p.Row + p.Height)
            : 0;

        // Scan from row 0 up to maxRow + height (enough room to find a slot)
        for (int row = 0; row <= maxRow + height; row++)
        {
            for (int col = 0; col <= GridColumns - width; col++)
            {
                if (IsAreaFree(row, col, width, height))
                    return new GridPosition(row, col, width, height, true);
            }
        }

        // Fallback: place below everything
        return new GridPosition(maxRow, 0, width, height, true);
    }

    private bool IsAreaFree(int row, int col, int width, int height)
    {
        foreach (var pos in _gridPositions.Values)
        {
            if (!pos.Visible) continue;
            // Check for overlap (AABB intersection)
            if (col < pos.Column + pos.Width &&
                col + width > pos.Column &&
                row < pos.Row + pos.Height &&
                row + height > pos.Row)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Describes a cell's position and size within the 12-column grid.
    /// </summary>
    public sealed record GridPosition(int Row, int Column, int Width, int Height, bool Visible);
}
