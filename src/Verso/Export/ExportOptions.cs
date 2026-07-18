using Verso.Abstractions;

namespace Verso.Export;

/// <summary>
/// Options that control visibility-aware export. When <see cref="LayoutId"/> is non-null
/// and <see cref="SupportedVisibilityStates"/> and <see cref="Renderers"/> are provided,
/// cells are filtered through <see cref="Extensions.Utilities.CellVisibilityResolver"/> before export.
/// When <see cref="RespectCellViewState"/> is true (the default), exporters also honor
/// the user-actionable subset of per-cell view-state metadata
/// (<c>verso:ui.outputVisibility</c>, <c>verso:ui.inputCollapsed</c>,
/// <c>verso:ui.outputPreviewLineCount</c>).
/// </summary>
public record ExportOptions(
    string? LayoutId = null,
    IReadOnlySet<CellVisibilityState>? SupportedVisibilityStates = null,
    IReadOnlyList<ICellRenderer>? Renderers = null,
    bool RespectCellViewState = true);
