using Verso.Abstractions;

namespace Verso;

/// <summary>
/// Manages the active layout engine and exposes capability flags.
/// Handles metadata persistence for layout state across save/load cycles.
/// </summary>
public sealed class LayoutManager
{
    private IReadOnlyList<ILayoutEngine> _availableLayouts;
    private volatile ILayoutEngine? _activeLayout;

    public LayoutManager(IReadOnlyList<ILayoutEngine> availableLayouts, string? defaultLayoutId = null)
        : this(availableLayouts, defaultLayoutId is null ? null : new LayoutReference(string.Empty, defaultLayoutId))
    {
    }

    public LayoutManager(IReadOnlyList<ILayoutEngine> availableLayouts, LayoutReference? defaultLayout)
    {
        _availableLayouts = availableLayouts ?? throw new ArgumentNullException(nameof(availableLayouts));

        if (defaultLayout is { } defaultRef && !TryActivate(defaultRef))
        {
            // Layout referenced by the notebook isn't registered yet — typically because
            // it ships in an extension that the notebook will load via a #!extension or
            // #!nuget cell. Record the missing id so the host can surface it to the UI;
            // leave _activeLayout null so the default capability set takes over.
            MissingLayoutId = defaultRef.LayoutId;
        }
    }

    /// <summary>
    /// The layout id requested at construction that could not be resolved against
    /// the available layouts, or <c>null</c> if everything resolved. Consumers can
    /// inspect this after construction to decide whether to surface a banner.
    /// </summary>
    public string? MissingLayoutId { get; private set; }

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
    /// Replaces the available layouts list with an updated snapshot.
    /// Preserves the active layout if it still exists in the new list.
    /// </summary>
    public void Refresh(IReadOnlyList<ILayoutEngine> updatedLayouts)
    {
        var activeExtension = (_activeLayout as IExtension)?.ExtensionId;
        var activeId = _activeLayout?.LayoutId;
        _availableLayouts = updatedLayouts ?? throw new ArgumentNullException(nameof(updatedLayouts));

        if (activeId is not null)
        {
            _activeLayout = _availableLayouts.FirstOrDefault(l =>
                string.Equals(l.LayoutId, activeId, StringComparison.OrdinalIgnoreCase) &&
                (activeExtension is null ||
                 (l is IExtension ext && string.Equals(ext.ExtensionId, activeExtension, StringComparison.OrdinalIgnoreCase))));
        }

        // If the previously active layout was disabled, fall back to the well-known default
        // layout by id, then any non-custom-renderer layout, then the first available.
        if (_activeLayout is null && _availableLayouts.Count > 0)
        {
            var fallback = _availableLayouts.FirstOrDefault(l =>
                    string.Equals(l.LayoutId, LayoutDefaults.LayoutId, StringComparison.OrdinalIgnoreCase))
                ?? _availableLayouts.FirstOrDefault(l => !l.RequiresCustomRenderer)
                ?? _availableLayouts[0];
            _activeLayout = fallback;
            OnLayoutChanged?.Invoke(fallback);
        }
    }

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
    /// Switches the active layout by layout ID. Throws when the id is unknown.
    /// Use <see cref="TryActivate(string)"/> when a missing id should not throw.
    /// </summary>
    public void SetActiveLayout(string layoutId)
        => SetActiveLayout(new LayoutReference(string.Empty, layoutId));

    /// <summary>
    /// Switches the active layout by qualified <see cref="LayoutReference"/>. Throws when
    /// no matching layout is registered.
    /// </summary>
    public void SetActiveLayout(LayoutReference reference)
    {
        if (!TryActivate(reference))
            throw new InvalidOperationException($"Layout '{reference}' not found.");
    }

    /// <summary>
    /// Attempts to activate the named layout by bare id (legacy compat path). Resolves
    /// across all available layouts; if more than one matches, the request is treated
    /// as ambiguous and activation fails.
    /// </summary>
    public bool TryActivate(string layoutId)
        => TryActivate(new LayoutReference(string.Empty, layoutId));

    /// <summary>
    /// Attempts to activate the layout by qualified reference. Returns <c>true</c> on
    /// success, or <c>false</c> when no matching layout is registered. An unqualified
    /// reference (empty <see cref="LayoutReference.ExtensionId"/>) falls back to the
    /// legacy resolution path — matching on LayoutId alone — and fails when more than
    /// one loaded extension provides that LayoutId.
    /// </summary>
    public bool TryActivate(LayoutReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference.LayoutId);

        ILayoutEngine? layout;
        if (string.IsNullOrEmpty(reference.ExtensionId))
        {
            // Unqualified — match by LayoutId across loaded extensions.
            var candidates = _availableLayouts
                .Where(l => string.Equals(l.LayoutId, reference.LayoutId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count != 1) return false;
            layout = candidates[0];
        }
        else
        {
            layout = _availableLayouts.FirstOrDefault(l =>
                l is IExtension ext &&
                string.Equals(ext.ExtensionId, reference.ExtensionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(l.LayoutId, reference.LayoutId, StringComparison.OrdinalIgnoreCase));
        }

        if (layout is null) return false;

        _activeLayout = layout;
        MissingLayoutId = null;
        OnLayoutChanged?.Invoke(layout);
        return true;
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
            if (metadata.Count == 0) continue;

            var extensionId = (layout as IExtension)?.ExtensionId ?? string.Empty;
            var key = string.IsNullOrEmpty(extensionId)
                ? layout.LayoutId
                : new LayoutReference(extensionId, layout.LayoutId).Qualified;

            // Drop any stale legacy bare-id entry so the saved notebook only carries
            // the qualified key once we've promoted it.
            if (!string.IsNullOrEmpty(extensionId) &&
                !string.Equals(key, layout.LayoutId, StringComparison.OrdinalIgnoreCase))
            {
                notebook.Layouts.Remove(layout.LayoutId);
            }

            notebook.Layouts[key] = metadata;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Restores layout metadata from the notebook model into matching layout engines.
    /// Accepts both qualified (<c>"&lt;extensionId&gt;:&lt;layoutId&gt;"</c>) and legacy
    /// bare-id keys; the qualified form takes precedence when both are present.
    /// </summary>
    public async Task RestoreMetadataAsync(NotebookModel notebook, IVersoContext context)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var layout in _availableLayouts)
        {
            var extensionId = (layout as IExtension)?.ExtensionId ?? string.Empty;
            var qualifiedKey = string.IsNullOrEmpty(extensionId)
                ? layout.LayoutId
                : new LayoutReference(extensionId, layout.LayoutId).Qualified;

            // Prefer the qualified key; fall back to the bare layout id for legacy notebooks.
            if (!notebook.Layouts.TryGetValue(qualifiedKey, out var metadataObj))
                notebook.Layouts.TryGetValue(layout.LayoutId, out metadataObj);

            if (metadataObj is Dictionary<string, object> metadata)
                await layout.ApplyLayoutMetadata(metadata, context).ConfigureAwait(false);
        }
    }
}
