using Verso.Abstractions;

namespace Verso.Export;

/// <summary>
/// Options that control visibility-aware export. When <see cref="LayoutId"/> is non-null
/// and <see cref="SupportedVisibilityStates"/> and <see cref="Renderers"/> are provided,
/// cells are filtered through <see cref="CellVisibilityResolver"/> before export.
/// </summary>
public record ExportOptions(
    string? LayoutId = null,
    IReadOnlySet<CellVisibilityState>? SupportedVisibilityStates = null,
    IReadOnlyList<ICellRenderer>? Renderers = null);
