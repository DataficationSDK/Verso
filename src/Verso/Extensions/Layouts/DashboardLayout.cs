using System.Net;
using System.Text;
using System.Text.Json;
using Verso.Abstractions;

namespace Verso.Extensions.Layouts;

/// <summary>
/// Grid-based dashboard layout that arranges cells in a 12-column CSS Grid.
/// Shows output-only cells with optional edit toggles and resize handles.
/// </summary>
[VersoExtension]
public sealed class DashboardLayout : ILayoutEngine
{
    private const int GridColumns = 12;
    private const int DefaultCellWidth = 6;
    private const int DefaultCellHeight = 4;

    private readonly Dictionary<Guid, GridPosition> _gridPositions = new();
    private bool _isEditMode;

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

    public LayoutCapabilities Capabilities
    {
        get
        {
            var caps = LayoutCapabilities.CellResize | LayoutCapabilities.CellExecute | LayoutCapabilities.CellEdit;
            if (_isEditMode)
                caps |= LayoutCapabilities.CellInsert | LayoutCapabilities.CellDelete | LayoutCapabilities.CellReorder;
            return caps;
        }
    }

    /// <summary>
    /// Gets or sets whether the dashboard is in edit mode.
    /// Edit mode enables cell insert, delete, and reorder capabilities.
    /// </summary>
    public bool IsEditMode
    {
        get => _isEditMode;
        set => _isEditMode = value;
    }

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"verso-dashboard-grid\" style=\"display:grid;grid-template-columns:repeat(12,1fr);grid-auto-rows:50px;gap:8px;padding:16px;align-content:start;\">");

        foreach (var cell in cells)
        {
            var pos = GetOrCreatePosition(cell.Id);
            if (!pos.Visible) continue;

            sb.Append("<div class=\"verso-dashboard-cell\" data-cell-id=\"")
              .Append(cell.Id)
              .Append("\" style=\"grid-column:")
              .Append(pos.Column + 1).Append("/span ").Append(pos.Width)
              .Append(";grid-row:")
              .Append(pos.Row + 1).Append("/span ").Append(pos.Height)
              .Append(";position:relative;border:1px solid #e0e0e0;border-radius:6px;overflow:hidden;display:flex;flex-direction:column;box-shadow:0 1px 3px rgba(0,0,0,0.08);\">");

            // Drag handle + toolbar
            sb.Append("<div class=\"verso-dashboard-cell-toolbar verso-dashboard-drag-handle\" style=\"display:flex;gap:4px;padding:4px 8px;border-bottom:1px solid #e0e0e0;background:#f5f5f5;cursor:grab;flex-shrink:0;\">");
            sb.Append("<button data-action=\"run\" data-cell-id=\"").Append(cell.Id).Append("\" style=\"cursor:pointer;\">&#x25B6; Run</button>");
            sb.Append("<button data-action=\"edit\" data-cell-id=\"").Append(cell.Id).Append("\" style=\"cursor:pointer;\">Edit</button>");
            sb.Append("<span class=\"verso-dashboard-drag-icon\" style=\"margin-left:auto;color:#858585;font-size:14px;cursor:grab;user-select:none;\">&#x2630;</span>");
            sb.Append("</div>");

            // Output-only rendering
            sb.Append("<div class=\"verso-dashboard-cell-output\" style=\"flex:1;overflow:auto;padding:10px 12px;\">");
            if (cell.Outputs.Count > 0)
            {
                foreach (var output in cell.Outputs)
                {
                    if (output.IsError)
                    {
                        sb.Append("<div class=\"verso-output verso-output--error\">");
                        sb.Append(WebUtility.HtmlEncode(output.Content));
                        sb.Append("</div>");
                    }
                    else if (output.MimeType == "text/html")
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
            }
            else
            {
                sb.Append("<div class=\"verso-output verso-output--text\"><pre style=\"margin:0;white-space:pre-wrap;\">");
                sb.Append(WebUtility.HtmlEncode(cell.Source));
                sb.Append("</pre></div>");
            }
            sb.Append("</div>");

            // Resize handle
            sb.Append("<div class=\"verso-dashboard-resize-handle\" data-cell-id=\"").Append(cell.Id).Append("\" style=\"position:absolute;bottom:0;right:0;width:20px;height:20px;cursor:se-resize;\"></div>");

            sb.Append("</div>");
        }

        sb.Append("</div>");

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
