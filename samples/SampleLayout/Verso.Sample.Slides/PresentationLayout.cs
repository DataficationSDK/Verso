using System.Net;
using System.Text;
using System.Text.Json;
using Verso.Abstractions;
using Verso.Sample.Slides.Models;

namespace Verso.Sample.Slides;

/// <summary>
/// Presentation layout that maps notebook cells to numbered slides with navigation controls.
/// Each cell is assigned to a slide; only cells on the current slide are visible.
/// </summary>
[VersoExtension]
public sealed class PresentationLayout : ILayoutEngine
{
    private readonly Dictionary<Guid, SlideAssignment> _slideAssignments = new();
    private int _currentSlide;
    private int _nextSlideNumber = 1;

    // --- IExtension ---

    public string ExtensionId => "com.verso.sample.presentation";
    public string Name => "Presentation Layout";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Slide-based presentation layout for notebook cells.";

    // --- ILayoutEngine ---

    public string LayoutId => "presentation";
    public string DisplayName => "Presentation";
    public string? Icon => null;
    public bool RequiresCustomRenderer => true;

    public LayoutCapabilities Capabilities =>
        LayoutCapabilities.CellInsert |
        LayoutCapabilities.CellDelete |
        LayoutCapabilities.CellReorder |
        LayoutCapabilities.CellEdit |
        LayoutCapabilities.CellExecute;

    /// <summary>
    /// Gets the current slide number being displayed (0 = no slides).
    /// </summary>
    public int CurrentSlide => _currentSlide;

    /// <summary>
    /// Gets the total number of distinct slides.
    /// </summary>
    public int SlideCount => _slideAssignments.Count > 0
        ? _slideAssignments.Values.Max(s => s.SlideNumber)
        : 0;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
    {
        var sb = new StringBuilder();
        var totalSlides = SlideCount;

        sb.Append("<div class=\"verso-presentation\" style=\"display:flex;flex-direction:column;align-items:center;width:100%;\">");

        // Slide container
        sb.Append("<div class=\"verso-slide-container\" style=\"width:1024px;height:768px;position:relative;border:1px solid #ccc;overflow:hidden;background:#fff;\">");

        foreach (var cell in cells)
        {
            if (!_slideAssignments.TryGetValue(cell.Id, out var assignment))
                continue;

            var isCurrentSlide = assignment.SlideNumber == _currentSlide;
            var display = isCurrentSlide ? "block" : "none";
            var titleClass = assignment.IsTitle ? " verso-slide-title" : "";

            sb.Append("<div class=\"verso-slide-cell")
              .Append(titleClass)
              .Append("\" data-cell-id=\"")
              .Append(cell.Id)
              .Append("\" data-slide=\"")
              .Append(assignment.SlideNumber)
              .Append("\" style=\"display:")
              .Append(display)
              .Append(";padding:24px;\">");

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
        }

        sb.Append("</div>");

        // Navigation controls
        sb.Append("<div class=\"verso-slide-nav\" style=\"display:flex;gap:16px;align-items:center;padding:12px;\">");
        sb.Append("<button data-action=\"prev-slide\" style=\"cursor:pointer;\">&#x25C0; Previous</button>");
        sb.Append("<span class=\"verso-slide-counter\">");
        sb.Append(_currentSlide).Append(" / ").Append(totalSlides);
        sb.Append("</span>");
        sb.Append("<button data-action=\"next-slide\" style=\"cursor:pointer;\">Next &#x25B6;</button>");
        sb.Append("</div>");

        sb.Append("</div>");

        return Task.FromResult(new RenderResult("text/html", sb.ToString()));
    }

    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
    {
        var isVisible = _slideAssignments.TryGetValue(cellId, out var assignment)
            && assignment.SlideNumber == _currentSlide;

        return Task.FromResult(new CellContainerInfo(
            cellId,
            X: 0,
            Y: 0,
            Width: 1024,
            Height: 768,
            IsVisible: isVisible));
    }

    public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context)
    {
        if (!_slideAssignments.ContainsKey(cellId))
        {
            _slideAssignments[cellId] = new SlideAssignment(_nextSlideNumber);
            _nextSlideNumber++;

            if (_currentSlide == 0)
                _currentSlide = 1;
        }
        return Task.CompletedTask;
    }

    public Task OnCellRemovedAsync(Guid cellId, IVersoContext context)
    {
        _slideAssignments.Remove(cellId);
        return Task.CompletedTask;
    }

    public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context)
    {
        // Slide assignment is independent of cell order
        return Task.CompletedTask;
    }

    /// <summary>
    /// Navigates to the specified slide number.
    /// </summary>
    /// <param name="slideNumber">The 1-based slide number to navigate to.</param>
    public void NavigateToSlide(int slideNumber)
    {
        if (slideNumber >= 1 && slideNumber <= SlideCount)
            _currentSlide = slideNumber;
    }

    /// <summary>
    /// Gets the slide assignment for a specific cell, or null if not assigned.
    /// </summary>
    public SlideAssignment? GetSlideAssignment(Guid cellId)
        => _slideAssignments.TryGetValue(cellId, out var assignment) ? assignment : null;

    /// <summary>
    /// Assigns a cell to a specific slide.
    /// </summary>
    public void AssignCellToSlide(Guid cellId, int slideNumber, string transition = "none", bool isTitle = false)
    {
        _slideAssignments[cellId] = new SlideAssignment(slideNumber, transition, isTitle);
        if (slideNumber >= _nextSlideNumber)
            _nextSlideNumber = slideNumber + 1;
    }

    public Dictionary<string, object> GetLayoutMetadata()
    {
        if (_slideAssignments.Count == 0)
            return new Dictionary<string, object>();

        var cells = new Dictionary<string, object>();
        foreach (var (id, assignment) in _slideAssignments)
        {
            cells[id.ToString()] = new Dictionary<string, object>
            {
                ["slide"] = assignment.SlideNumber,
                ["transition"] = assignment.Transition,
                ["isTitle"] = assignment.IsTitle
            };
        }

        return new Dictionary<string, object>
        {
            ["currentSlide"] = _currentSlide,
            ["cells"] = cells
        };
    }

    public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context)
    {
        if (metadata.TryGetValue("currentSlide", out var csObj))
        {
            _currentSlide = csObj is JsonElement cje ? cje.GetInt32() : Convert.ToInt32(csObj);
        }

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

        int maxSlide = 0;
        foreach (var (key, value) in cellsDict)
        {
            if (!Guid.TryParse(key, out var cellId)) continue;

            int slide = 1;
            string transition = "none";
            bool isTitle = false;

            if (value is Dictionary<string, object> posDict)
            {
                if (posDict.TryGetValue("slide", out var s)) slide = Convert.ToInt32(s);
                if (posDict.TryGetValue("transition", out var t)) transition = t.ToString()!;
                if (posDict.TryGetValue("isTitle", out var ti)) isTitle = Convert.ToBoolean(ti);
            }
            else if (value is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                if (je.TryGetProperty("slide", out var ss)) slide = ss.GetInt32();
                if (je.TryGetProperty("transition", out var tt)) transition = tt.GetString()!;
                if (je.TryGetProperty("isTitle", out var tti)) isTitle = tti.GetBoolean();
            }

            _slideAssignments[cellId] = new SlideAssignment(slide, transition, isTitle);
            if (slide > maxSlide) maxSlide = slide;
        }

        _nextSlideNumber = maxSlide + 1;
        return Task.CompletedTask;
    }
}
