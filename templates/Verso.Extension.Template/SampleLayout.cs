using System.Text;

namespace MyExtension;

/// <summary>
/// Example <see cref="ILayoutEngine"/> scaffold: an inline layout that stacks cells
/// vertically with a density toggle in a small toolbar. It demonstrates the core layout
/// idioms: emitting a <c>data-cell-slot</c> per cell (the host mounts the live cell
/// component into each slot), routing toolbar clicks back to
/// <see cref="ILayoutInteractionHandler"/> through <c>data-action</c> attributes,
/// persisting state through layout metadata, and shipping a scoped stylesheet themed
/// with the host's <c>--verso-*</c> CSS variables. Replace this with your own layout logic.
/// </summary>
[VersoExtension]
public sealed class SampleLayout : ILayoutEngine, ILayoutInteractionHandler
{
    public string ExtensionId => "com.example.myextension.layout";
    public string Name => "Sample Layout";
    public string Version => "1.0.0";
    public string? Author => "Extension Author";
    public string? Description => "A sample stacked layout with a density toggle.";

    public string LayoutId => "myextension.stack";
    public string DisplayName => "Sample Stack";
    public string? Icon => "layout";
    public bool RequiresCustomRenderer => true;

    public LayoutCapabilities Capabilities =>
        LayoutCapabilities.CellInsert |
        LayoutCapabilities.CellDelete |
        LayoutCapabilities.CellReorder |
        LayoutCapabilities.CellEdit |
        LayoutCapabilities.CellExecute;

    private const double RowHeight = 140;

    // Persisted layout state: cell spacing. Interactions mutate it, metadata round-trips it.
    private string _density = "comfortable";

    // Cell order, refreshed on every render and kept current by the lifecycle notifications
    // so GetCellContainerAsync can answer between renders.
    private readonly List<Guid> _order = new();

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    // Scope every rule to this layout's host-injected root so styles cannot leak into the
    // host page or other extensions.
    private string RootPrefix =>
        $".verso-layout-root[data-extension-id=\"{ExtensionId}\"][data-layout-id=\"{LayoutId}\"] ";

    public Task<IReadOnlyList<LayoutStaticAsset>?> GetStaticAssetsAsync(
        IVersoContext context, LayoutHostCapabilities hostCapabilities)
    {
        if (!hostCapabilities.SupportedAssetContentTypes.Contains("text/css"))
            return Task.FromResult<IReadOnlyList<LayoutStaticAsset>?>(null);

        var p = RootPrefix;
        var css =
            $"{p}.verso-samplelayout-toolbar {{ display: flex; gap: 8px; padding: 8px; " +
            "background: var(--verso-bg-elevated); color: var(--verso-fg-default); }}\n" +
            $"{p}.verso-samplelayout-toolbar button {{ background: transparent; " +
            "color: var(--verso-fg-muted); border: 1px solid transparent; cursor: pointer; }}\n" +
            $"{p}.verso-samplelayout-toolbar button.active {{ color: var(--verso-fg-default); " +
            "border-color: var(--verso-accent); }}\n" +
            $"{p}.verso-samplelayout-cells {{ display: flex; flex-direction: column; gap: 16px; " +
            "background: var(--verso-bg-default); }}\n" +
            $"{p}.verso-samplelayout-cells.compact {{ gap: 4px; }}\n";

        IReadOnlyList<LayoutStaticAsset> assets = new[]
        {
            new LayoutStaticAsset("samplelayout.css", "text/css", Encoding.UTF8.GetBytes(css)),
        };
        return Task.FromResult<IReadOnlyList<LayoutStaticAsset>?>(assets);
    }

    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
    {
        _order.Clear();
        foreach (var cell in cells)
            _order.Add(cell.Id);

        var sb = new StringBuilder();

        // Buttons carry data-action; the host routes their clicks to OnLayoutInteractionAsync.
        sb.Append("<div class=\"verso-samplelayout-toolbar\">");
        foreach (var density in new[] { "comfortable", "compact" })
        {
            sb.Append($"<button data-action=\"set-density\" data-payload=\"{density}\"" +
                      $" class=\"{(_density == density ? "active" : "")}\">{density}</button>");
        }
        sb.Append("</div>");

        // One slot per cell; the host mounts the live cell component into each slot.
        sb.Append($"<div class=\"verso-samplelayout-cells {(_density == "compact" ? "compact" : "")}\">");
        foreach (var cell in cells)
            sb.Append($"<div data-cell-slot=\"{cell.Id}\"></div>");
        sb.Append("</div>");

        return Task.FromResult(new RenderResult("text/html", sb.ToString()));
    }

    public Task OnLayoutInteractionAsync(LayoutInteractionContext context)
    {
        if (context.InteractionType == "set-density" &&
            context.Payload is "comfortable" or "compact" &&
            context.Payload != _density)
        {
            _density = context.Payload;
            // The host does not refresh on its own; request a re-render for any
            // interaction that changes the layout's visible structure.
            context.RequestRender();
        }
        return Task.CompletedTask;
    }

    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
    {
        var index = _order.IndexOf(cellId);
        return Task.FromResult(index < 0
            ? new CellContainerInfo(cellId, 0, 0, 800, RowHeight)
            : new CellContainerInfo(cellId, 0, index * RowHeight, 800, RowHeight));
    }

    public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context)
    {
        _order.Remove(cellId);
        _order.Insert(Math.Clamp(index, 0, _order.Count), cellId);
        return Task.CompletedTask;
    }

    public Task OnCellRemovedAsync(Guid cellId, IVersoContext context)
    {
        _order.Remove(cellId);
        return Task.CompletedTask;
    }

    public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context)
    {
        _order.Remove(cellId);
        _order.Insert(Math.Clamp(newIndex, 0, _order.Count), cellId);
        return Task.CompletedTask;
    }

    public Dictionary<string, object> GetLayoutMetadata() =>
        new() { ["density"] = _density };

    public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context)
    {
        if (metadata.TryGetValue("density", out var value))
        {
            var density = value?.ToString();
            if (density is "comfortable" or "compact")
                _density = density;
        }
        return Task.CompletedTask;
    }
}
