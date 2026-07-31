using System.Text;

namespace MyExtension;

/// <summary>
/// Example <see cref="INotebookPanel"/> that lists the notebook's cells and lets the
/// user pin one. Replace this with your own panel.
/// </summary>
/// <remarks>
/// <para>
/// A panel describes its content rather than drawing it. <see cref="RenderAsync"/>
/// returns representations richest first and each host renders the first media type
/// it understands, so offering plain text alongside markup keeps the panel useful on
/// hosts that do not draw HTML.
/// </para>
/// <para>
/// This class also implements <see cref="IPanelInteractionHandler"/>, which is the
/// usual arrangement: one class owns the panel and the actions raised from it.
/// </para>
/// </remarks>
[VersoExtension]
public sealed class SamplePanel : INotebookPanel, IPanelInteractionHandler
{
    private readonly HashSet<Guid> _pinned = new();

    public string ExtensionId => "com.example.myextension.panel";
    public string Name => "Sample Panel";
    public string Version => "1.0.0";
    public string? Author => "Extension Author";
    public string? Description => "A sample notebook panel.";

    public string PanelId => "cells";
    public string DisplayName => "Cells";

    // A name from the host's icon set. Hosts map it to their own glyphs, and one that
    // does not recognize the name falls back rather than failing.
    public string? IconName => "list";

    // Built-in panels occupy 100 through 500, so this one lands after them.
    public int Order => 600;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // Offered whenever a notebook is open. A panel that only makes sense for certain
    // notebooks would inspect the context here instead.
    public Task<bool> IsAvailableAsync(IPanelContext context)
        => Task.FromResult(true);

    public Task<IReadOnlyList<RenderResult>> RenderAsync(IPanelContext context)
    {
        var cells = context.NotebookCells;

        IReadOnlyList<RenderResult> representations = new[]
        {
            new RenderResult("text/html", BuildHtml(cells, context.SelectedCellId)),
            new RenderResult("text/plain", BuildText(cells))
        };

        return Task.FromResult(representations);
    }

    public Task OnPanelInteractionAsync(PanelInteractionContext context)
    {
        if (context.InteractionType == "toggle-pin"
            && Guid.TryParse(context.TargetId, out var cellId))
        {
            if (!_pinned.Remove(cellId))
                _pinned.Add(cellId);

            // Tell the host the content changed so it asks for it again. Without this
            // the panel keeps showing what it last rendered.
            context.RequestRefresh();
        }

        return Task.CompletedTask;
    }

    private string BuildHtml(IReadOnlyList<CellModel> cells, Guid? selectedCellId)
    {
        if (cells.Count == 0)
            return "<div class=\"verso-panel-empty\"><p>This notebook has no cells.</p></div>";

        var sb = new StringBuilder();
        sb.Append("<div class=\"verso-panel-section\">");
        sb.Append("<div class=\"verso-panel-section-title\">Cells</div>");

        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var isPinned = _pinned.Contains(cell.Id);
            var isSelected = cell.Id == selectedCellId;

            sb.Append("<div class=\"verso-panel-row\">");
            sb.Append($"<span>{Escape($"{i + 1}. {cell.Type}")}</span>");

            if (isSelected)
                sb.Append("<span class=\"verso-panel-chip verso-panel-chip--info\">selected</span>");
            if (isPinned)
                sb.Append("<span class=\"verso-panel-chip verso-panel-chip--success\">pinned</span>");

            // data-panel-action names the action and data-target-id says which item it
            // applies to. Both arrive on PanelInteractionContext.
            sb.Append("<span class=\"verso-panel-actions\">");
            sb.Append($"<button class=\"verso-panel-action\" data-panel-action=\"toggle-pin\" ");
            sb.Append($"data-target-id=\"{cell.Id}\">{(isPinned ? "Unpin" : "Pin")}</button>");
            sb.Append("</span>");

            sb.Append("</div>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    private string BuildText(IReadOnlyList<CellModel> cells)
    {
        if (cells.Count == 0) return "This notebook has no cells.";

        var lines = cells.Select((cell, i) =>
            $"{i + 1}. {cell.Type}{(_pinned.Contains(cell.Id) ? " (pinned)" : "")}");

        return string.Join(Environment.NewLine, lines);
    }

    // Panel markup goes into the host's document as-is, so anything derived from
    // notebook content has to be escaped on the way out.
    private static string Escape(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
