using System.Text.Json;
using Verso.Abstractions;

namespace Verso.Extensions.Utilities;

/// <summary>
/// Resolves cell visibility by evaluating the three-layer model:
/// cell type default, user metadata override, and layout-supported states.
/// Layouts call this as a convenience but are not required to use it.
/// </summary>
public static class CellVisibilityResolver
{
    private const string MetadataKey = "verso:visibility";

    /// <summary>
    /// Resolves the visibility state for a cell within a specific layout.
    /// </summary>
    /// <param name="cell">The cell to resolve visibility for.</param>
    /// <param name="renderer">The cell's renderer, providing the default visibility hint.</param>
    /// <param name="layoutId">The target layout identifier.</param>
    /// <param name="supportedStates">The set of visibility states the layout supports.</param>
    /// <returns>The resolved visibility state, guaranteed to be within <paramref name="supportedStates"/>.</returns>
    public static CellVisibilityState Resolve(
        CellModel cell,
        ICellRenderer renderer,
        string layoutId,
        IReadOnlySet<CellVisibilityState> supportedStates)
        => Resolve(cell, renderer.DefaultVisibility, layoutId, supportedStates);

    /// <summary>
    /// Resolves the visibility state for a cell within a specific layout using an explicit hint
    /// rather than an <see cref="ICellRenderer"/> instance.
    /// </summary>
    /// <param name="cell">The cell to resolve visibility for.</param>
    /// <param name="defaultHint">The default visibility hint for the cell type.</param>
    /// <param name="layoutId">The target layout identifier.</param>
    /// <param name="supportedStates">The set of visibility states the layout supports.</param>
    /// <returns>The resolved visibility state, guaranteed to be within <paramref name="supportedStates"/>.</returns>
    public static CellVisibilityState Resolve(
        CellModel cell,
        CellVisibilityHint defaultHint,
        string layoutId,
        IReadOnlySet<CellVisibilityState> supportedStates)
    {
        // Layer 1: Check for user override in cell metadata
        if (TryGetUserOverride(cell, layoutId, out var overrideState))
            return ConstrainToSupported(overrideState, supportedStates);

        // Layer 2: Fall back to cell type default hint
        var hintState = MapHintToState(defaultHint, supportedStates);
        return ConstrainToSupported(hintState, supportedStates);
    }

    private static bool TryGetUserOverride(
        CellModel cell, string layoutId, out CellVisibilityState state)
    {
        state = default;

        if (!cell.Metadata.TryGetValue(MetadataKey, out var visibilityObj))
            return false;

        string? valueStr = null;

        switch (visibilityObj)
        {
            case Dictionary<string, string> dictStr:
                dictStr.TryGetValue(layoutId, out valueStr);
                break;

            case Dictionary<string, object> dictObj:
                if (dictObj.TryGetValue(layoutId, out var objVal))
                    valueStr = objVal?.ToString();
                break;

            case JsonElement jsonEl when jsonEl.ValueKind == JsonValueKind.Object:
                if (jsonEl.TryGetProperty(layoutId, out var prop) &&
                    prop.ValueKind == JsonValueKind.String)
                    valueStr = prop.GetString();
                break;
        }

        if (valueStr is not null &&
            Enum.TryParse<CellVisibilityState>(valueStr, ignoreCase: true, out state))
            return true;

        state = default;
        return false;
    }

    private static CellVisibilityState MapHintToState(
        CellVisibilityHint hint, IReadOnlySet<CellVisibilityState> supportedStates)
    {
        return hint switch
        {
            CellVisibilityHint.Content => CellVisibilityState.Visible,
            CellVisibilityHint.Infrastructure =>
                supportedStates.Contains(CellVisibilityState.Hidden)
                    ? CellVisibilityState.Hidden
                    : CellVisibilityState.Visible,
            CellVisibilityHint.OutputOnly =>
                supportedStates.Contains(CellVisibilityState.OutputOnly)
                    ? CellVisibilityState.OutputOnly
                    : CellVisibilityState.Visible,
            _ => CellVisibilityState.Visible,
        };
    }

    private static CellVisibilityState ConstrainToSupported(
        CellVisibilityState desired, IReadOnlySet<CellVisibilityState> supportedStates)
    {
        if (supportedStates.Contains(desired))
            return desired;

        // Fall back to nearest supported state
        return desired switch
        {
            CellVisibilityState.Collapsed =>
                supportedStates.Contains(CellVisibilityState.Hidden)
                    ? CellVisibilityState.Hidden
                    : CellVisibilityState.Visible,
            CellVisibilityState.OutputOnly => CellVisibilityState.Visible,
            CellVisibilityState.Hidden => CellVisibilityState.Visible,
            _ => CellVisibilityState.Visible,
        };
    }
}
