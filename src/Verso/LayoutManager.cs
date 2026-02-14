using Verso.Abstractions;

namespace Verso;

/// <summary>
/// Manages the active layout engine and exposes capability flags.
/// Handles metadata persistence for layout state across save/load cycles.
/// </summary>
public sealed class LayoutManager
{
    private readonly IReadOnlyList<ILayoutEngine> _availableLayouts;
    private volatile ILayoutEngine? _activeLayout;

    public LayoutManager(IReadOnlyList<ILayoutEngine> availableLayouts, string? defaultLayoutId = null)
    {
        _availableLayouts = availableLayouts ?? throw new ArgumentNullException(nameof(availableLayouts));

        if (defaultLayoutId is not null)
            SetActiveLayout(defaultLayoutId);
    }

    /// <summary>
    /// Gets the currently active layout engine, or <c>null</c> if none is active.
    /// </summary>
    public ILayoutEngine? ActiveLayout => _activeLayout;

    /// <summary>
    /// Gets the list of available layout engines.
    /// </summary>
    public IReadOnlyList<ILayoutEngine> AvailableLayouts => _availableLayouts;

    /// <summary>
    /// Raised when the active layout changes.
    /// </summary>
    public event Action<ILayoutEngine>? OnLayoutChanged;

    /// <summary>
    /// Gets whether the active layout requires a custom renderer.
    /// </summary>
    public bool RequiresCustomRenderer => _activeLayout?.RequiresCustomRenderer ?? false;

    /// <summary>
    /// Gets the capabilities supported by the active layout.
    /// When no layout is active, all capabilities are granted so that the notebook is fully functional.
    /// </summary>
    public LayoutCapabilities Capabilities => _activeLayout?.Capabilities ??
        (LayoutCapabilities.CellInsert | LayoutCapabilities.CellDelete |
         LayoutCapabilities.CellReorder | LayoutCapabilities.CellEdit |
         LayoutCapabilities.CellResize | LayoutCapabilities.CellExecute |
         LayoutCapabilities.MultiSelect);

    /// <summary>
    /// Switches the active layout by layout ID.
    /// </summary>
    public void SetActiveLayout(string layoutId)
    {
        ArgumentNullException.ThrowIfNull(layoutId);
        var layout = _availableLayouts.FirstOrDefault(
            l => string.Equals(l.LayoutId, layoutId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Layout '{layoutId}' not found.");
        _activeLayout = layout;
        OnLayoutChanged?.Invoke(layout);
    }

    /// <summary>
    /// Saves layout metadata from all known layouts into the notebook model.
    /// </summary>
    public Task SaveMetadataAsync(NotebookModel notebook)
    {
        ArgumentNullException.ThrowIfNull(notebook);

        foreach (var layout in _availableLayouts)
        {
            var metadata = layout.GetLayoutMetadata();
            if (metadata.Count > 0)
                notebook.Layouts[layout.LayoutId] = metadata;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Restores layout metadata from the notebook model into matching layout engines.
    /// </summary>
    public async Task RestoreMetadataAsync(NotebookModel notebook, IVersoContext context)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var (layoutId, metadataObj) in notebook.Layouts)
        {
            var layout = _availableLayouts.FirstOrDefault(
                l => string.Equals(l.LayoutId, layoutId, StringComparison.OrdinalIgnoreCase));

            if (layout is null) continue;

            if (metadataObj is Dictionary<string, object> metadata)
            {
                await layout.ApplyLayoutMetadata(metadata, context).ConfigureAwait(false);
            }
        }
    }
}
