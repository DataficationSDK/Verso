namespace Verso.Abstractions;

/// <summary>
/// Represents an action that can appear on the notebook toolbar or in context menus.
/// Actions expose a command that the user can trigger, with optional enable/disable logic.
/// </summary>
public interface IToolbarAction : IExtension
{
    /// <summary>
    /// Unique identifier for this action (e.g. "run-all", "export-pdf").
    /// </summary>
    string ActionId { get; }

    /// <summary>
    /// Human-readable label displayed on the toolbar button or menu item.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Optional icon name or path for the action button.
    /// </summary>
    string? Icon { get; }

    /// <summary>
    /// Specifies where the action should appear (e.g. main toolbar, cell toolbar, context menu).
    /// </summary>
    ToolbarPlacement Placement { get; }

    /// <summary>
    /// Sort order within the placement group. Lower values appear first.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Determines whether the action is currently enabled based on the notebook state.
    /// </summary>
    /// <param name="context">Context providing access to the current notebook, selection, and services.</param>
    /// <returns><c>true</c> if the action should be enabled; otherwise <c>false</c>.</returns>
    Task<bool> IsEnabledAsync(IToolbarActionContext context);

    /// <summary>
    /// Executes the action.
    /// </summary>
    /// <param name="context">Context providing access to the current notebook, selection, and services.</param>
    /// <returns>A task that completes when the action has finished executing.</returns>
    Task ExecuteAsync(IToolbarActionContext context);
}
